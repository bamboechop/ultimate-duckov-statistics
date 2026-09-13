using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SQLitePCL;

namespace UltimateDuckovStatistics.Sqlite;

public static class SqliteLibrary
{
    private static readonly object gate = new();
    private static IntPtr handle;
    public const string ExpectedHash = "ab57d0437795ecc757cb693f32ea224173fa9856594d95cfa6b5033e645cd1ec";
    public static string Initialize(string path)
    {
        lock (gate)
        {
            path = Path.GetFullPath(path);
            if (HashFile(path) != ExpectedHash) throw new InvalidOperationException("SQLite library hash mismatch.");
            if (handle == IntPtr.Zero)
            {
                handle = LoadLibraryExW(path, IntPtr.Zero, 0x900); // DLL directory plus System32, no process search-path mutation.
                if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "SQLite load failed.");
                SQLite3Provider_dynamic_cdecl.Setup("uds-sqlite-3.53.4", new Functions(handle));
                raw.SetProvider(new SQLite3Provider_dynamic_cdecl());
                // Provider state is confined to the uniquely named owned provider assemblies.
                raw.FreezeProvider();
            }
            if (raw.sqlite3_libversion_number() != 3053004) throw new InvalidOperationException("SQLite version mismatch.");
            return raw.sqlite3_sourceid().utf8_to_string();
        }
    }
    public static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    public static string HashFile(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    private sealed class Functions(IntPtr library) : IGetFunctionPointer
    { public IntPtr GetFunctionPointer(string name) => GetProcAddress(library, name); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2101", Justification = "GetProcAddress requires ANSI; the upstream provider supplies fixed ASCII SQLite export names.")]
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}

public sealed class SqliteFailure(int code, string message) : IOException(message)
{ public int Code { get; } = code; }

// Small command wrapper over the maintained upstream raw provider.
public sealed class SqliteStore : IDisposable
{
    private readonly sqlite3 db;
    public SqliteStore(string path, bool readOnly = false)
    {
        var rc = raw.sqlite3_open_v2(path, out db, readOnly ? raw.SQLITE_OPEN_READONLY : raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE, null);
        if (rc != raw.SQLITE_OK) { var error = Error(rc); db.Dispose(); throw error; }
        Check(raw.sqlite3_busy_timeout(db, 25));
        Exec("PRAGMA foreign_keys=ON");
    }
    public void Configure(string journal = "WAL")
    {
        if (journal is not ("WAL" or "DELETE")) throw new ArgumentException("Unsupported journal.");
        var actual = ScalarText("PRAGMA journal_mode=" + journal);
        if (!string.Equals(journal, actual, StringComparison.OrdinalIgnoreCase)) throw new IOException("Journal setting was not applied.");
        Exec("PRAGMA synchronous=" + (journal == "WAL" ? "FULL" : "EXTRA"));
        if (ScalarLong("PRAGMA synchronous") != (journal == "WAL" ? 2 : 3)) throw new IOException("Durability setting was not applied.");
    }
    public void DisableAutomaticCheckpoint()
    {
        Check(raw.sqlite3_wal_autocheckpoint(db, 0));
        Check(raw.sqlite3_db_config(db, raw.SQLITE_DBCONFIG_NO_CKPT_ON_CLOSE, 1, out var enabled));
        if (enabled != 1) throw new IOException("Checkpoint ownership setting was not applied.");
    }
    public void Exec(string sql, params object?[] values)
    { using var statement = Prepare(sql, values); var rc = raw.sqlite3_step(statement); if (rc != raw.SQLITE_DONE) throw Error(rc); }
    public long ScalarLong(string sql, params object?[] values)
    { using var statement = Prepare(sql, values); CheckRow(raw.sqlite3_step(statement)); return raw.sqlite3_column_int64(statement, 0); }
    public string ScalarText(string sql, params object?[] values)
    { using var statement = Prepare(sql, values); CheckRow(raw.sqlite3_step(statement)); return raw.sqlite3_column_text(statement, 0).utf8_to_string(); }
    public byte[]? Blob(string sql, params object?[] values)
    { using var statement = Prepare(sql, values); var rc = raw.sqlite3_step(statement); if (rc == raw.SQLITE_DONE) return null; CheckRow(rc); return raw.sqlite3_column_blob(statement, 0).ToArray(); }
    public List<object[]> Rows(string sql, params object?[] values) => EnumerateRows(sql, values).ToList();
    public IEnumerable<object[]> EnumerateRows(string sql, params object?[] values)
    {
        using var statement = Prepare(sql, values); int rc;
        while ((rc = raw.sqlite3_step(statement)) == raw.SQLITE_ROW)
        {
            var row = new object[raw.sqlite3_column_count(statement)];
            for (var col = 0; col < row.Length; col++) row[col] = raw.sqlite3_column_type(statement, col) switch
            {
                raw.SQLITE_INTEGER => raw.sqlite3_column_int64(statement, col),
                raw.SQLITE_BLOB => raw.sqlite3_column_blob(statement, col).ToArray(),
                raw.SQLITE_NULL => null!,
                _ => raw.sqlite3_column_text(statement, col).utf8_to_string()
            };
            yield return row;
        }
        if (rc != raw.SQLITE_DONE) throw Error(rc);
    }
    public void BackupTo(string newPath, CancellationToken cancellation = default)
    {
        if (File.Exists(newPath)) throw new IOException("Backup output already exists.");
        using var target = new SqliteStore(newPath);
        using var backup = raw.sqlite3_backup_init(target.db, "main", db, "main");
        if (backup == null) throw target.Error(raw.sqlite3_errcode(target.db));
        int rc;
        do
        {
            cancellation.ThrowIfCancellationRequested();
            // A close/profile switch can cancel between bounded page batches.
            rc = raw.sqlite3_backup_step(backup, 16);
            if (rc != raw.SQLITE_OK && rc != raw.SQLITE_DONE) throw target.Error(rc);
        } while (rc != raw.SQLITE_DONE);
    }
    private sqlite3_stmt Prepare(string sql, object?[] values)
    {
        Check(raw.sqlite3_prepare_v2(db, sql, out var statement));
        try
        {
            for (var index = 0; index < values.Length; index++) Check(values[index] switch
            {
                null => raw.sqlite3_bind_null(statement, index + 1),
                byte[] bytes => raw.sqlite3_bind_blob(statement, index + 1, bytes),
                string text => raw.sqlite3_bind_text(statement, index + 1, text),
                long number => raw.sqlite3_bind_int64(statement, index + 1, number),
                int number => raw.sqlite3_bind_int(statement, index + 1, number),
                _ => throw new ArgumentException("Unsupported bind type.")
            });
            return statement;
        }
        catch { statement.Dispose(); throw; }
    }
    private void Check(int rc) { if (rc != raw.SQLITE_OK) throw Error(rc); }
    private void CheckRow(int rc) { if (rc != raw.SQLITE_ROW) throw Error(rc); }
    private SqliteFailure Error(int rc) => new(rc, raw.sqlite3_errmsg(db).utf8_to_string());
    public void Dispose() => db.Dispose();
}
