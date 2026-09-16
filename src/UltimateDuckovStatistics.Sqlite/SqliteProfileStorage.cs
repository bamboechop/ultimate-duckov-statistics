using System.Security.Cryptography;
using System.Text;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Sqlite;

/// <summary>One exclusively owned generation, with a queue containing only durability work.</summary>
public sealed partial class SqliteProfileStorage : IIncrementalProfileStorage, IProfileExportSource, IProfileStorageMaintenance
{
    private const int StorageVersion = 8;
    private readonly object gate = new();
    private readonly ProfileRecordCodec codec;
    private readonly FileStream ownership;
    private readonly string exportRoot;
    private readonly Action<string>? diagnostic;
    private Task tail = Task.CompletedTask;
    private SqliteStore? connection;
    private bool disposed;
    private string? owner;
    private volatile bool readFailure;

    public SqliteProfileStorage(string path, ProfileRecordCodec codec, string? exportRoot = null, Action<string>? diagnostic = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        this.exportRoot = System.IO.Path.GetFullPath(exportRoot ?? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "export-staging"));
        this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        this.diagnostic = diagnostic;
        readFailure = File.Exists(Path + ".read-failure");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        // The OS releases this lease on process death. A second generation writer
        // cannot take over a live owner's persisted receipt cursor.
        ownership = new FileStream(Path + ".owner", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public string Path { get; }

    public IncrementalProfileState? Load()
    {
        Drain().GetAwaiter().GetResult();
        if (!File.Exists(Path)) return null;
        return Queue(() =>
        {
            var db = Open();
            if (db.ScalarLong("PRAGMA user_version") != StorageVersion
                || db.ScalarLong("SELECT complete FROM profile_state WHERE id=1") != 1)
                throw new InvalidDataException("SQLite profile version or import marker is invalid.");
            if (db.ScalarText("PRAGMA quick_check") != "ok") throw new InvalidDataException("SQLite profile integrity check failed.");
            return ReadIndexed(db);
        }).GetAwaiter().GetResult();
    }

    public void Import(ProfileDocument profile, SessionCheckpoint? session, ActiveRunCheckpoint? checkpoint, bool sessionEvidencePresent = false)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var failure = ProfileFormat.ValidateRecoveryCandidate(profile);
        if (failure != null) throw new ArgumentException("Import profile is invalid: " + failure, nameof(profile));
        if (checkpoint != null) ProfileRepository.ValidateActiveCheckpointForStorage(checkpoint, profile.GenerationId);
        historyGeneration = profile.GenerationId;
        var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        journal.Import(profile);
        var write = journal.Capture(profile, session, true, checkpoint, true);
        // Freeze on the caller's ownership boundary; import work itself is allowed
        // to be large and runs before the generation is published to consumers.
        Queue(() =>
        {
            if (File.Exists(Path)) throw new IOException("An existing SQLite profile cannot be overwritten by import.");
            var staging = Path + ".import-" + Guid.NewGuid().ToString("N");
            using (var db = new SqliteStore(staging))
            {
                db.Configure("DELETE");
                CreateSchema(db);
                db.Exec("BEGIN IMMEDIATE");
                try
                {
                    foreach (var record in write.Records.OrderBy(record => record.Address.Kind)) Put(db, record);
                    db.Exec("INSERT INTO profile_state VALUES(1,?, ?,1,?)", profile.GenerationId, profile.Revision, sessionEvidencePresent || session != null ? 1 : 0);
                    // Read the exact persisted records before promotion. Public
                    // JSON canonical equality also checks ordering and omitted fields.
                    var rebuilt = Read(db).Profile;
                    var reconstructedFailure = ProfileFormat.ValidateRecoveryCandidate(rebuilt);
                    if (reconstructedFailure != null || !Canonical(profile).SequenceEqual(Canonical(rebuilt)))
                        throw new InvalidDataException("Imported records did not reconstruct the exact profile.");
                    db.Exec("COMMIT");
                }
                catch { TryRollback(db); throw; }
                if (db.ScalarText("PRAGMA integrity_check") != "ok") throw new InvalidDataException("Imported database integrity failed.");
            }
            // DELETE/EXTRA committed the staging database and its directory entry.
            // Promotion cannot replace an existing primary or the source JSON.
            File.Move(staging, Path);
            return true;
        }).GetAwaiter().GetResult();
    }

    public Task Commit(IncrementalProfileWrite write)
    {
        if (write == null) throw new ArgumentNullException(nameof(write));
        return Queue(() => { CommitCore(write); return true; });
    }

    public Task Drain() { lock (gate) return tail; }

    private Task<T> Queue<T>(Func<T> work)
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SqliteProfileStorage));
            // Continue after a failed command. A later command must prove it
            // covers the failed dirty range before it can commit.
            var result = tail.ContinueWith(_ => work(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            tail = result;
            return result;
        }
    }

    private void CommitCore(IncrementalProfileWrite write)
    {
        var db = Open();
        if (db.ScalarText("SELECT generation FROM profile_state WHERE id=1") != write.GenerationId)
            throw new InvalidOperationException("A write cannot cross profile generations.");
        if (owner != null && owner != write.Owner) throw new InvalidOperationException("A different writer owns this open generation.");
        var digest = Digest(write);
        db.Exec("BEGIN IMMEDIATE");
        try
        {
            var cursor = db.Rows("SELECT owner,sequence,through_version,digest FROM receipt WHERE id=1");
            var sameOwner = cursor.Count != 0 && (string)cursor[0][0] == write.Owner;
            var committed = sameOwner ? (long)cursor[0][2] : 0;
            if (sameOwner && write.Order <= (long)cursor[0][1])
            {
                if (write.Order != (long)cursor[0][1] || !digest.SequenceEqual((byte[])cursor[0][3]))
                    throw new InvalidOperationException("Stale or conflicting incremental command.");
                // A prior COMMIT may have returned an I/O failure after making
                // its receipt readable. Recommit a changed page under FULL
                // synchronization before acknowledging that retry.
                db.Exec("UPDATE receipt SET durability_nonce=1-durability_nonce WHERE id=1");
                db.Exec("COMMIT");
                owner = write.Owner;
                return;
            }
            if (write.CoveredAfter > committed)
                throw new InvalidOperationException("Incremental command omits an uncommitted predecessor.");
            if (write.Revision < db.ScalarLong("SELECT revision FROM profile_state WHERE id=1"))
                throw new InvalidOperationException("Incremental command would roll back the profile revision.");
            var craftingScopeChanged = SqliteCraftingRecords.ScopeChanged(db, write);
            if (write.ReplaceRunStatistics)
            {
                // Explicit prepared correction only. Its command owns every
                // rebuilt run-derived row; routine updates never enter here.
                db.Exec("DELETE FROM records WHERE kind IN (15,16,28,29,30,31,32)");
                db.Exec("DELETE FROM run_metric_counts");
            }
            foreach (var record in write.Records.OrderBy(record => record.Address.Kind)) Put(db, record);
            ValidateTransaction(db, write, craftingScopeChanged);
            var sessionChange = write.Records.FirstOrDefault(record => record.Address.Kind == ProfileRecordKind.Session);
            db.Exec("UPDATE profile_state SET revision=?,session_present=COALESCE(?,session_present) WHERE id=1", write.Revision,
                sessionChange == null ? null : sessionChange.Bytes == null ? 0 : 1);
            db.Exec("INSERT INTO receipt VALUES(1,?,?,?,?,0) ON CONFLICT(id) DO UPDATE SET owner=excluded.owner,sequence=excluded.sequence,through_version=excluded.through_version,digest=excluded.digest,durability_nonce=1-receipt.durability_nonce",
                write.Owner, write.Order, write.ThroughVersion, digest);
            db.Exec("COMMIT");
            owner = write.Owner;
        }
        catch
        {
            TryRollback(db);
            throw;
        }
    }

    private SqliteStore Open()
    {
        if (readFailure) throw new InvalidDataException("A retained SQLite record failed validation; this generation is blocked from further reads and writes.");
        if (connection != null) return connection;
        if (!File.Exists(Path)) throw new FileNotFoundException("SQLite profile has not been imported.", Path);
        var db = new SqliteStore(Path);
        try { PrepareFormat(db); db.Configure(); db.DisableAutomaticCheckpoint(); connection = db; return db; }
        catch { db.Dispose(); throw; }
    }

    private static void CreateSchema(SqliteStore db)
    {
        db.Exec("PRAGMA user_version=" + StorageVersion);
        db.Exec("CREATE TABLE profile_state(id INTEGER PRIMARY KEY CHECK(id=1),generation TEXT NOT NULL,revision INTEGER NOT NULL CHECK(revision>=0),complete INTEGER NOT NULL CHECK(complete=1),session_present INTEGER NOT NULL CHECK(session_present IN (0,1)))");
        db.Exec("CREATE TABLE receipt(id INTEGER PRIMARY KEY CHECK(id=1),owner TEXT NOT NULL,sequence INTEGER NOT NULL,through_version INTEGER NOT NULL,digest BLOB NOT NULL,durability_nonce INTEGER NOT NULL CHECK(durability_nonce IN (0,1)))");
        db.Exec("CREATE TABLE records(ordinal INTEGER PRIMARY KEY,kind INTEGER NOT NULL,k1 TEXT NOT NULL,k2 TEXT NOT NULL,k3 TEXT NOT NULL,payload_sha BLOB NOT NULL,payload BLOB NOT NULL,UNIQUE(kind,k1,k2,k3))");
        SqliteCraftingRecords.Create(db);
        CreateHistoryIndex(db);
        CreateCheckpointCounts(db);
        CreateRunMetricCounts(db);
    }

    private void Put(SqliteStore db, ProfileRecordChange record)
    {
        var key = record.Address;
        if (SqliteCraftingRecords.Owns(key.Kind)) { SqliteCraftingRecords.Put(db, record); return; }
        PrepareCheckpointRecord(db, record);
        PrepareRunMetricRecord(db, record);
        PrepareEncounterRecord(db, record);
        if (key.Kind == ProfileRecordKind.DeferredHeader)
        {
            var old = ReadRootPayload(db, (int)key.Kind);
            var previousId = old == null ? null : ProfileRecordCodec.Decode<DeferredMetadataRecord>(old).RunId;
            var nextId = record.Bytes == null ? null : ProfileRecordCodec.Decode<DeferredMetadataRecord>(record.Bytes).RunId;
            if (record.Bytes == null || previousId != nextId)
                db.Exec("DELETE FROM records WHERE kind IN (19,20,21)");
        }
        if (record.Bytes == null)
            db.Exec("DELETE FROM records WHERE kind=? AND k1=? AND k2=? AND k3=?", (int)key.Kind, key.First, key.Second, key.Third);
        else
            db.Exec("INSERT INTO records(kind,k1,k2,k3,payload,payload_sha) VALUES(?,?,?,?,?,?) ON CONFLICT(kind,k1,k2,k3) DO UPDATE SET payload=excluded.payload,payload_sha=excluded.payload_sha",
                (int)key.Kind, key.First, key.Second, key.Third, SqliteRecordPayload.Encode(record.Bytes), HashBytes(record.Bytes));
        if (key.Kind == ProfileRecordKind.CompletedRun) PutHistoryIndex(db, record);
    }

    private void ValidateTransaction(SqliteStore db, IncrementalProfileWrite write, bool craftingScopeChanged)
    {
        var metadata = ProfileRecordCodec.Decode<ProfileMetadataRecord>(ReadRootPayload(db, 1)
            ?? throw new InvalidDataException("Profile metadata was removed."));
        if (metadata.GenerationId != write.GenerationId || metadata.Revision != write.Revision)
            throw new InvalidDataException("Profile metadata and receipt disagree.");
        SqliteCraftingRecords.ValidateAffected(db, write, craftingScopeChanged);
        ValidateCheckpointTransaction(db, write);
        ValidateRunMetricTransaction(db, write);
        ValidateEncounterTransaction(db, write);
        // Checkpoint and terminal run cannot both own the same run identity.
        if (!write.Records.Any(record => record.Address.Kind is ProfileRecordKind.ActiveCheckpoint or ProfileRecordKind.CompletedRun)) return;
        var checkpointBytes = ReadRootPayload(db, 23);
        if (checkpointBytes != null)
        {
            var checkpoint = ProfileRecordCodec.Decode<ActiveRunCheckpoint>(checkpointBytes);
            if (checkpoint.SaveGenerationId != write.GenerationId
                || db.ScalarLong("SELECT count(*) FROM records WHERE kind=17 AND k1=?", checkpoint.RunId) != 0)
                throw new InvalidDataException("Active checkpoint ownership conflicts with completed history.");
        }
    }

    private static IncrementalProfileState Read(SqliteStore db) => ProfileRecordReconstruction.Read(ReadRecords(db));

    private static IEnumerable<ProfileRecordChange> ReadRecords(SqliteStore db, bool includeHistory = true, bool includeCheckpoint = true)
    {
        // Stream individual retained payloads. Recovery/export may construct a
        // complete document, but never also retains a second all-run byte array.
        for (var number = 1; number <= (int)ProfileRecordKind.Encounter; number++)
        {
            var kind = (ProfileRecordKind)number;
            if (!includeHistory && kind is ProfileRecordKind.CompletedRun or ProfileRecordKind.Encounter) continue;
            if (!includeCheckpoint && number >= 23 && number <= 27) continue;
            if (SqliteCraftingRecords.Owns(kind))
            {
                foreach (var record in SqliteCraftingRecords.Read(db, kind)) yield return record;
                continue;
            }
            foreach (var row in db.EnumerateRows("SELECT k1,k2,k3,payload,payload_sha FROM records WHERE kind=? ORDER BY ordinal", number))
            {
                var bytes = DecodePayload((byte[])row[3], (byte[])row[4]);
                yield return new ProfileRecordChange(new ProfileRecordAddress(kind, (string)row[0], (string)row[1], (string)row[2]), 0, bytes);
            }
        }
    }

    private static byte[] HashBytes(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }
    private static void VerifyHash(byte[] bytes, byte[] expected)
    { if (!HashBytes(bytes).SequenceEqual(expected)) throw new InvalidDataException("A stored record checksum failed."); }

    private static byte[] Canonical(ProfileDocument profile)
    { using var stream = new MemoryStream(); new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ProfileDocument), new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(stream, profile); return stream.ToArray(); }

    private static byte[] Digest(IncrementalProfileWrite write)
    {
        using var sha = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        using (var binary = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            binary.Write(write.GenerationId); binary.Write(write.Owner); binary.Write(write.Order); binary.Write(write.CoveredAfter); binary.Write(write.ThroughVersion); binary.Write(write.Revision); binary.Write(write.ReplaceRunStatistics);
            foreach (var row in write.Records)
            { binary.Write((int)row.Address.Kind); binary.Write(row.Address.First); binary.Write(row.Address.Second); binary.Write(row.Address.Third); binary.Write(row.Version); binary.Write(row.Bytes?.Length ?? -1); if (row.Bytes != null) binary.Write(row.Bytes); }
        }
        stream.FlushFinalBlock();
        return sha.Hash!;
    }

    private static void TryRollback(SqliteStore db) { try { db.Exec("ROLLBACK"); } catch (SqliteFailure) { /* Preserve the original failure. */ } }

    public void Dispose()
    {
        Task pending;
        lock (gate) { if (disposed) return; disposed = true; pending = tail; }
        try { pending.GetAwaiter().GetResult(); }
        finally
        {
            StopExportCopy();
            StopMaintenance();
            connection?.Dispose(); ownership.Dispose();
        }
    }
}
