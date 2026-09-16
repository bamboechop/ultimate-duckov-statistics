using System.Collections.ObjectModel;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

// The address vocabulary is internal. Callers cannot submit an arbitrary field
// path or a validation delegate to bypass the repository's typed mutation APIs.
internal enum ProfileRecordKind
{
    Metadata = 1, Statistics = 2, BaseMovement = 3, WorldTime = 4, Holdings = 5,
    Economy = 6, Item = 7, Groups = 8, Crafting = 9, CraftingOutput = 10,
    CraftingRecipe = 11, CraftingBatch = 12, CraftingResource = 13,
    CraftingAssociation = 14, RunTotals = 15, RunRecords = 16, CompletedRun = 17,
    DeferredHeader = 18, DeferredItem = 19, DeferredGroups = 20, DeferredEconomy = 21,
    Session = 22, ActiveCheckpoint = 23, CheckpointRoot = 24, CheckpointSegment = 25,
    CheckpointCollection = 26, CheckpointEntry = 27, RunMap = 28, RouteMap = 29,
    RunMetricCollection = 30, RunMetricEntry = 31, MapRunRecords = 32, Encounter = 33
}

internal readonly struct ProfileRecordAddress : IEquatable<ProfileRecordAddress>
{
    internal ProfileRecordAddress(ProfileRecordKind kind, string first = "", string second = "", string third = "")
    { Kind = kind; First = first; Second = second; Third = third; }
    internal ProfileRecordKind Kind { get; }
    internal string First { get; }
    internal string Second { get; }
    internal string Third { get; }
    public bool Equals(ProfileRecordAddress other) => Kind == other.Kind && First == other.First && Second == other.Second && Third == other.Third;
    public override bool Equals(object? obj) => obj is ProfileRecordAddress other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, First, Second, Third);
}

internal sealed class ProfileRecordChange
{
    internal ProfileRecordChange(ProfileRecordAddress address, long version, byte[]? bytes)
    { Address = address; Version = version; Bytes = bytes; }
    internal ProfileRecordAddress Address { get; }
    internal long Version { get; }
    // Only the owned storage backend sees this independently encoded buffer.
    internal byte[]? Bytes { get; }
}

/// <summary>An immutable write boundary produced by the repository.</summary>
public sealed class IncrementalProfileWrite
{
    internal IncrementalProfileWrite(string generation, string owner, long order, long coveredAfter,
        long throughVersion, long revision, IReadOnlyList<ProfileRecordChange> records, bool replaceRunStatistics = false)
    {
        GenerationId = generation; Owner = owner; Order = order; CoveredAfter = coveredAfter;
        ThroughVersion = throughVersion; Revision = revision;
        Records = new ReadOnlyCollection<ProfileRecordChange>(records.ToArray());
        ReplaceRunStatistics = replaceRunStatistics;
    }
    public string GenerationId { get; }
    public long Revision { get; }
    public int ChangedRecordCount => Records.Count;
    internal string Owner { get; }
    internal long Order { get; }
    internal long CoveredAfter { get; }
    internal long ThroughVersion { get; }
    internal IReadOnlyList<ProfileRecordChange> Records { get; }
    internal bool ReplaceRunStatistics { get; }
}

// Mutations and captures are owned by the repository caller. Only acknowledgement
// may arrive from the writer. A newer value keeps its dirty mark when an older
// immutable snapshot commits; failures never remove a mark.
internal sealed class ProfileChangeJournal
{
    private readonly object gate = new();
    private readonly Dictionary<ProfileRecordAddress, long> dirty = new();
    private readonly string generation;
    private readonly string owner = Guid.NewGuid().ToString("N");
    private readonly ProfileRecordCodec codec;
    private long version;
    private long order;
    private long acknowledged;
    private byte[]? sessionPayload;
    private byte[]? checkpointPayload;
    private readonly Dictionary<ProfileRecordAddress, byte[]?> checkpointRecords = new();
    private (long Version, IncrementalCheckpointCapture Capture)? checkpointReceipt;
    private readonly Dictionary<ProfileRecordAddress, byte[]?> preparedCorrection = new();
    private readonly Dictionary<ProfileRecordAddress, byte[]> encounterRecords = new();
    private long runStatisticsReplacementVersion;

    internal ProfileChangeJournal(string generation, ProfileRecordCodec? codec = null)
    { this.generation = generation; this.codec = codec ?? new ProfileRecordCodec(); }

    internal void Metadata() => Mark(new ProfileRecordAddress(ProfileRecordKind.Metadata));
    internal void Encounter(Encounters.EncounterRecord record, byte[] bytes)
    {
        var address = EncounterAddress(record);
        lock (gate) { dirty[address] = checked(++version); encounterRecords[address] = bytes; }
    }

    internal static ProfileRecordAddress EncounterAddress(Encounters.EncounterRecord record) =>
        new(ProfileRecordKind.Encounter, record.RunId, ((int)record.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture), record.Id);
    internal void BaseMovement() => Mark(new ProfileRecordAddress(ProfileRecordKind.BaseMovement));
    internal void WorldTime() => Mark(new ProfileRecordAddress(ProfileRecordKind.WorldTime));
    internal void Holdings() => Mark(new ProfileRecordAddress(ProfileRecordKind.Holdings));
    internal void Economy() => Mark(new ProfileRecordAddress(ProfileRecordKind.Economy), new ProfileRecordAddress(ProfileRecordKind.DeferredEconomy), new ProfileRecordAddress(ProfileRecordKind.DeferredHeader));
    internal void Statistics() => Mark(new ProfileRecordAddress(ProfileRecordKind.Statistics));
    internal void CompletedReplayCompacted(string runId) => Mark(new ProfileRecordAddress(ProfileRecordKind.CompletedRun, runId));
    internal void DeferredState() => Mark(new ProfileRecordAddress(ProfileRecordKind.DeferredHeader), new ProfileRecordAddress(ProfileRecordKind.DeferredGroups), new ProfileRecordAddress(ProfileRecordKind.DeferredEconomy));

    internal void Item(string itemId)
    {
        Mark(new ProfileRecordAddress(ProfileRecordKind.Item, itemId), new ProfileRecordAddress(ProfileRecordKind.Groups), new ProfileRecordAddress(ProfileRecordKind.Statistics),
            new ProfileRecordAddress(ProfileRecordKind.DeferredItem, itemId), new ProfileRecordAddress(ProfileRecordKind.DeferredGroups), new ProfileRecordAddress(ProfileRecordKind.DeferredHeader));
    }

    internal void Crafting(CraftingMutation mutation)
    {
        Mark(new ProfileRecordAddress(ProfileRecordKind.Crafting));
        foreach (var row in mutation.Rows)
        {
            Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingOutput, row.OutputItemId));
            foreach (var resource in row.Resources) Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingResource, resource.ResourceItemId));
            if (!row.RecipeIdentityProven) continue;
            Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingRecipe, row.OutputItemId, row.RecipeId));
            foreach (var batch in row.BatchActions.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingBatch, row.OutputItemId, row.RecipeId, batch));
            foreach (var resource in row.Resources)
                Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingAssociation, row.OutputItemId, row.RecipeId, resource.ResourceItemId));
        }
    }

    internal void Capabilities()
    {
        Mark(new ProfileRecordAddress(ProfileRecordKind.Metadata), new ProfileRecordAddress(ProfileRecordKind.WorldTime), new ProfileRecordAddress(ProfileRecordKind.Holdings),
            new ProfileRecordAddress(ProfileRecordKind.Economy), new ProfileRecordAddress(ProfileRecordKind.Crafting));
    }

    internal void CompletedRun(ProfileDocument profile, RunSummary summary)
    {
        ClearIncrementalCheckpoint();
        checkpointPayload = null;
        Mark(new ProfileRecordAddress(ProfileRecordKind.CompletedRun, summary.RunId), new ProfileRecordAddress(ProfileRecordKind.RunTotals), new ProfileRecordAddress(ProfileRecordKind.RunRecords),
            new ProfileRecordAddress(ProfileRecordKind.Statistics), new ProfileRecordAddress(ProfileRecordKind.DeferredHeader), new ProfileRecordAddress(ProfileRecordKind.DeferredGroups),
            new ProfileRecordAddress(ProfileRecordKind.DeferredEconomy), new ProfileRecordAddress(ProfileRecordKind.ActiveCheckpoint));
        var source = new RunMetricScope(summary.WeaponStatistics, summary.CombatStatistics, summary.ItemStatistics, summary.EquipmentStatistics);
        RunMetrics(RunMetricRecords.ScopeId(ProfileRecordKind.RunTotals), source);
        RunMetrics(RunMetricRecords.ScopeId(ProfileRecordKind.RunMap, summary.StartingMapId), source);
        Mark(new ProfileRecordAddress(ProfileRecordKind.MapRunRecords, summary.StartingMapId));
        var routeMetrics = summary.RouteCapabilities.RouteAwareMapTotals.State == AdapterCapabilityState.Supported
            || summary.HistoricalEventAttributionIncomplete && summary.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported;
        if (routeMetrics || summary.Economy.Capabilities.RouteAttribution.State == AdapterCapabilityState.Supported)
            foreach (var segment in summary.Segments)
            {
                if (!profile.Statistics.RunTotals.RouteMaps.ContainsKey(segment.MapId)) throw new InvalidOperationException("Completed route map aggregation is missing.");
                RunMetrics(RunMetricRecords.ScopeId(ProfileRecordKind.RouteMap, segment.MapId), routeMetrics
                    ? new RunMetricScope(segment.WeaponStatistics, segment.CombatStatistics, segment.ItemStatistics, segment.EquipmentStatistics) : null);
            }
    }

    private void RunMetrics(string scope, RunMetricScope? changed)
    {
        var owner = RunMetricRecords.ParseScope(scope);
        Mark(new ProfileRecordAddress(owner.Kind, owner.Key));
        foreach (var kind in RunMetricRecords.Kinds)
        {
            var family = ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Mark(new ProfileRecordAddress(ProfileRecordKind.RunMetricCollection, scope, family));
            if (changed == null) continue;
            foreach (string key in changed.Dictionary(kind).Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.RunMetricEntry, scope, family, key));
        }
    }

    internal ProfileRecordChange[] PrepareCorrectedRunRecords(ProfileDocument profile, RunSummary replacement)
    {
        Mark(new ProfileRecordAddress(ProfileRecordKind.Statistics), new ProfileRecordAddress(ProfileRecordKind.RunTotals),
            new ProfileRecordAddress(ProfileRecordKind.RunRecords));
        foreach (var scope in RunMetricRecords.AllScopes(profile)) RunMetrics(scope, RunMetricRecords.Scope(profile, scope));
        foreach (var key in profile.Statistics.RunRecords.Maps.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.MapRunRecords, key));
        return Capture(profile).Records.Concat(new[] { new ProfileRecordChange(new ProfileRecordAddress(ProfileRecordKind.CompletedRun, replacement.RunId), 0, codec.Encode(replacement)) }).ToArray();
    }

    internal void ApplyPreparedRunCorrection(IReadOnlyList<ProfileRecordChange> records)
    {
        lock (gate)
        {
            foreach (var record in records)
            {
                dirty[record.Address] = checked(++version);
                preparedCorrection[record.Address] = record.Bytes;
            }
            runStatisticsReplacementVersion = version;
        }
    }

    internal void Checkpoint(IncrementalCheckpointCapture capture)
    {
        if (capture.Generation != generation) throw new InvalidOperationException("Checkpoint capture belongs to another generation.");
        // The tracker retains every unacknowledged changed entry. Its newer
        // immutable packet therefore supersedes the previous pending packet,
        // including collection replacements and entries removed since capture.
        var records = CheckpointRecordChanges.From(capture, codec).ToArray();
        lock (gate)
        {
            ClearIncrementalCheckpoint();
            dirty.Remove(new ProfileRecordAddress(ProfileRecordKind.ActiveCheckpoint));
            foreach (var record in records)
            {
                checkpointRecords.Add(record.Address, record.Bytes);
                dirty[record.Address] = checked(++version);
            }
            // Only the latest cumulative capture needs acknowledgement. An
            // older successful command may leave redundant dirty entries until
            // this one commits; retries cannot accumulate an unbounded ledger.
            checkpointReceipt = (version, capture);
        }
    }

    private void ClearIncrementalCheckpoint()
    {
        lock (gate)
        {
            foreach (var address in checkpointRecords.Keys) dirty.Remove(address);
            checkpointRecords.Clear();
        }
    }

    internal void Import(ProfileDocument profile, bool includeHistory = true)
    {
        Mark(new ProfileRecordAddress(ProfileRecordKind.Metadata), new ProfileRecordAddress(ProfileRecordKind.Statistics), new ProfileRecordAddress(ProfileRecordKind.BaseMovement),
            new ProfileRecordAddress(ProfileRecordKind.WorldTime), new ProfileRecordAddress(ProfileRecordKind.Holdings), new ProfileRecordAddress(ProfileRecordKind.Economy),
            new ProfileRecordAddress(ProfileRecordKind.Groups), new ProfileRecordAddress(ProfileRecordKind.Crafting), new ProfileRecordAddress(ProfileRecordKind.RunTotals),
            new ProfileRecordAddress(ProfileRecordKind.RunRecords), new ProfileRecordAddress(ProfileRecordKind.DeferredHeader), new ProfileRecordAddress(ProfileRecordKind.DeferredGroups),
            new ProfileRecordAddress(ProfileRecordKind.DeferredEconomy));
        foreach (var key in profile.Statistics.Items.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.Item, key));
        if (profile.DeferredItemPersistence != null)
            foreach (var key in profile.DeferredItemPersistence.AppliedLifetimeStatistics.Items.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.DeferredItem, key));
        foreach (var output in profile.Statistics.Crafting.Outputs)
        {
            Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingOutput, output.Key));
            foreach (var recipe in output.Value.Recipes)
            {
                Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingRecipe, output.Key, recipe.Key));
                foreach (var key in recipe.Value.BatchActions.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingBatch, output.Key, recipe.Key, key));
                foreach (var key in recipe.Value.Resources.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingAssociation, output.Key, recipe.Key, key));
            }
        }
        foreach (var key in profile.Statistics.Crafting.Resources.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.CraftingResource, key));
        foreach (var scope in RunMetricRecords.AllScopes(profile)) RunMetrics(scope, RunMetricRecords.Scope(profile, scope));
        foreach (var key in profile.Statistics.RunRecords.Maps.Keys) Mark(new ProfileRecordAddress(ProfileRecordKind.MapRunRecords, key));
        if (includeHistory) foreach (var run in profile.Statistics.Runs) Mark(new ProfileRecordAddress(ProfileRecordKind.CompletedRun, run.RunId));
        if (includeHistory && profile.EncounterHistory != null)
            foreach (var record in profile.EncounterHistory) { Encounters.EncounterRecordValidation.Validate(record); Encounter(record, codec.Encode(record)); }
    }

    internal IncrementalProfileWrite Capture(ProfileDocument profile, SessionCheckpoint? session = null,
        bool sessionChanged = false, ActiveRunCheckpoint? checkpoint = null, bool checkpointChanged = false)
    {
        if (profile.GenerationId != generation) throw new InvalidOperationException("An incremental capture cannot cross profile generations.");
        Metadata();
        if (sessionChanged) { sessionPayload = session == null ? null : codec.Encode(session); Mark(new ProfileRecordAddress(ProfileRecordKind.Session)); }
        if (checkpointChanged) { ClearIncrementalCheckpoint(); checkpointPayload = checkpoint == null ? null : codec.Encode(checkpoint); Mark(new ProfileRecordAddress(ProfileRecordKind.ActiveCheckpoint)); }
        KeyValuePair<ProfileRecordAddress, long>[] marks; long through; long from; long captureOrder;
        Dictionary<ProfileRecordAddress, byte[]> capturedEncounters;
        lock (gate) { marks = dirty.ToArray(); through = version; from = acknowledged; captureOrder = checked(++order); capturedEncounters = new(encounterRecords); }
        // Encode before handing ownership to a worker. Each address resolves only
        // its changed entry; unrelated lifetime entries and history are untouched.
        var records = marks.Select(mark => (checkpointRecords.TryGetValue(mark.Key, out var bytes) || preparedCorrection.TryGetValue(mark.Key, out bytes) || capturedEncounters.TryGetValue(mark.Key, out bytes))
            ? new ProfileRecordChange(mark.Key, mark.Value, bytes)
            : ProfileRecordCapture.Capture(profile, mark.Key, mark.Value, sessionPayload, checkpointPayload, codec)).ToArray();
        return new IncrementalProfileWrite(generation, owner, captureOrder, from, through, profile.Revision, records, runStatisticsReplacementVersion != 0);
    }

    internal void Acknowledge(IncrementalProfileWrite write)
    {
        if (write.GenerationId != generation || write.Owner != owner) throw new InvalidOperationException("Incremental receipt belongs to another owner.");
        IncrementalCheckpointCapture? receipt = null;
        lock (gate)
        {
            foreach (var record in write.Records)
                if (dirty.TryGetValue(record.Address, out var current) && current <= record.Version)
                { dirty.Remove(record.Address); preparedCorrection.Remove(record.Address); encounterRecords.Remove(record.Address); }
            if (runStatisticsReplacementVersion != 0 && runStatisticsReplacementVersion <= write.ThroughVersion) runStatisticsReplacementVersion = 0;
            acknowledged = Math.Max(acknowledged, write.ThroughVersion);
            if (checkpointReceipt.HasValue && checkpointReceipt.Value.Version <= write.ThroughVersion)
            { receipt = checkpointReceipt.Value.Capture; checkpointReceipt = null; }
        }
        receipt?.Acknowledge();
    }

    private void Mark(params ProfileRecordAddress[] addresses)
    {
        lock (gate)
            foreach (var address in addresses)
            { dirty[address] = checked(++version); preparedCorrection.Remove(address); }
    }
}
