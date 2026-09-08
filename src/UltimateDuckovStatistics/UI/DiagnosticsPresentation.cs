using System.Globalization;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.UI;

internal enum DiagnosticsHealth { Working, Limited, Error }
internal enum DiagnosticsLogFilter { All, Warnings, Errors }
internal enum NativeMenuIntegrationState { NotObserved, AttachedUnverified, Available, Unavailable }

internal sealed class DiagnosticsRuntimeSnapshot
{
    public string GenerationId { get; set; } = "";
    public string DataRoot { get; set; } = "";
    public string Hotkey { get; set; } = "F8";
    public string GameVersion { get; set; } = "";
    public string OpenDetail { get; set; } = "";
    public ProfileSaveReceipt? SaveReceipt { get; set; }
    public ProfileOpenResult? OpenResult { get; set; }
    public NativeMenuIntegrationState MainMenu { get; set; }
    public NativeMenuIntegrationState BaseMenu { get; set; }
    public IReadOnlyList<DiagnosticEntry> Entries { get; set; } = Array.Empty<DiagnosticEntry>();
    public bool TransitionPending { get; set; }
}

internal sealed class DiagnosticsValue
{
    public string Label { get; }
    public string Value { get; }
    public DiagnosticsHealth? Health { get; }
    public DiagnosticsValue(string label, string value, DiagnosticsHealth? health = null)
    { Label = label; Value = value; Health = health; }
}
internal sealed class DiagnosticsCapability
{
    public string Id { get; }
    public string Name { get; }
    public string Status { get; }
    public DiagnosticsHealth Health { get; }
    public string Version { get; }
    public string State { get; }
    public string Detail { get; }
    public DiagnosticsCapability(string id, string name, string status, DiagnosticsHealth health,
        string version, string state, string detail)
    { Id = id; Name = name; Status = status; Health = health; Version = version; State = state; Detail = detail; }
}
internal sealed class DiagnosticsSystem
{
    public string Id { get; }
    public string Name { get; }
    public DiagnosticsHealth Health { get; }
    public IReadOnlyList<DiagnosticsCapability> Capabilities { get; }
    public IReadOnlyList<DiagnosticsValue> ExtraRows { get; }
    public DiagnosticsSystem(string id, string name, DiagnosticsHealth health, IEnumerable<DiagnosticsCapability> capabilities,
        IEnumerable<DiagnosticsValue>? extra = null)
    { Id = id; Name = name; Health = health; Capabilities = Array.AsReadOnly(capabilities.ToArray()); ExtraRows = Array.AsReadOnly(extra?.ToArray() ?? Array.Empty<DiagnosticsValue>()); }
}
internal sealed class DiagnosticsIssue
{
    public string Id { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Severity { get; }
    public string Timestamp { get; }
    public int ReportCount { get; }
    public DiagnosticsIssue(string id, string title, string detail, string severity, string timestamp = "", int reportCount = 1)
    { Id = id; Title = title; Detail = detail; Severity = severity; Timestamp = timestamp; ReportCount = reportCount; }
}
internal sealed class DiagnosticsLogEntry
{
    public string Id { get; }
    public string Message { get; }
    public string Severity { get; }
    public string Timestamp { get; }
    public DiagnosticsLogEntry(string id, string message, string severity, string timestamp)
    { Id = id; Message = message; Severity = severity; Timestamp = timestamp; }
}
internal sealed class DiagnosticsPresentation
{
    public string GenerationId { get; }
    public string ProfileLabel { get; }
    public string LastSaved { get; }
    public string DataRoot { get; }
    public string Hotkey { get; }
    public DiagnosticsHealth Health { get; }
    public string BannerTitle { get; }
    public string BannerDetail { get; }
    public IReadOnlyList<DiagnosticsSystem> Systems { get; }
    public IReadOnlyList<DiagnosticsIssue> Issues { get; }
    public IReadOnlyList<DiagnosticsLogEntry> Log { get; }
    public IReadOnlyList<DiagnosticsValue> Versions { get; }
    public IReadOnlyList<DiagnosticsValue> Recovery { get; }
    public string OpenDetail { get; }
    public DiagnosticsPresentation(string generation, string profile, string lastSaved, string dataRoot, string hotkey,
        DiagnosticsHealth health, string bannerTitle, string bannerDetail, IEnumerable<DiagnosticsSystem> systems,
        IEnumerable<DiagnosticsIssue> issues, IEnumerable<DiagnosticsLogEntry> log, IEnumerable<DiagnosticsValue> versions,
        IEnumerable<DiagnosticsValue> recovery, string openDetail)
    {
        GenerationId = generation; ProfileLabel = profile; LastSaved = lastSaved; DataRoot = dataRoot; Hotkey = hotkey;
        Health = health; BannerTitle = bannerTitle; BannerDetail = bannerDetail; OpenDetail = openDetail;
        Systems = Array.AsReadOnly(systems.ToArray()); Issues = Array.AsReadOnly(issues.ToArray()); Log = Array.AsReadOnly(log.ToArray());
        Versions = Array.AsReadOnly(versions.ToArray()); Recovery = Array.AsReadOnly(recovery.ToArray());
    }
}

internal static class DiagnosticsPresentationFactory
{
    private const string ThrowableInitializationDetail = "Successful main-player Skill_Grenade releases in raids; recorded totals begin with this adapter. Earlier throws are unavailable.";
    public static DiagnosticsPresentation? Create(StatisticsPanelProjection projection, string generation, DiagnosticsRuntimeSnapshot runtime,
        Func<string, string>? text = null)
    {
        if (projection == null || runtime == null || runtime.TransitionPending || runtime.GenerationId != generation
            || !StatisticsPanelProjectionFactory.HasProvableGeneration(projection.Profile, generation)) return null;
        var t = text ?? UiText.Get;
        var profile = projection.Profile;
        var unavailable = t("ui.unavailable");
        var entries = runtime.Entries.OrderByDescending(e => e.TimestampUtc).Take(50)
            .Select(e => new DiagnosticEntry { TimestampUtc = e.TimestampUtc, Severity = e.Severity, Message = e.Message }).ToArray();
        var grouped = profile.Capabilities.GroupBy(c => c.AdapterId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var systems = new List<DiagnosticsSystem>();
        foreach (var group in DiagnosticsCapabilityCatalog.GroupOrder.Where(g => g != "menu"))
        {
            var descriptors = DiagnosticsCapabilityCatalog.All.Where(d => d.Group == group).ToList();
            if (group == "other")
                descriptors.AddRange(grouped.Keys.Where(id => !DiagnosticsCapabilityCatalog.All.Any(d => d.Id == id))
                    .OrderBy(id => id, StringComparer.Ordinal).Select(id => new DiagnosticsCapabilityDescriptor(id, group, id)));
            if (descriptors.Count == 0) continue;
            var capabilities = descriptors.Select(d =>
            {
                grouped.TryGetValue(d.Id, out var records);
                var record = records?.Length == 1 ? records[0] : null;
                var health = record?.State == AdapterCapabilityState.Supported
                    || (record?.State == AdapterCapabilityState.Experimental && d.PartialCoverageIsExpected) ? DiagnosticsHealth.Working
                    : record?.State == AdapterCapabilityState.Experimental ? DiagnosticsHealth.Limited : DiagnosticsHealth.Error;
                var status = record == null ? unavailable : t("ui." + health.ToString().ToLowerInvariant());
                return new DiagnosticsCapability(d.Id, group == "other" ? d.EnglishName : t(d.TextKey), status, health,
                    record?.Version ?? unavailable, record?.State.ToString() ?? unavailable,
                    record == null ? t(records?.Length > 1 ? "ui.diag_conflicting_contract" : "ui.diag_missing_contract") : DetailForDisplay(record.Detail ?? unavailable, t));
            }).ToArray();
            var state = capabilities.Any(c => c.Health == DiagnosticsHealth.Error) ? DiagnosticsHealth.Error
                : capabilities.Any(c => c.Health == DiagnosticsHealth.Limited) ? DiagnosticsHealth.Limited : DiagnosticsHealth.Working;
            var extra = new List<DiagnosticsValue>();
            if (group == "storage")
            {
                var receipt = runtime.SaveReceipt;
                var saved = receipt?.GenerationId == generation;
                var pending = !saved || receipt!.Revision < profile.Revision;
                var failedWrite = entries.Any(e => e.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    && IsPersistenceFailure(e.Message) && (!saved || e.TimestampUtc > receipt!.SavedUtc));
                // Deferred updates (including the normal world-time save cadence) are
                // pending data, not degraded storage. A matching receipt still proves
                // storage is working without claiming the newest revision is durable.
                var disk = failedWrite ? DiagnosticsHealth.Error : !saved ? DiagnosticsHealth.Limited : DiagnosticsHealth.Working;
                if (disk > state) state = disk;
                extra.Add(new DiagnosticsValue(t("ui.diag_profile_writes"), t(failedWrite ? "ui.error" : pending ? "ui.diag_pending_write" : "ui.working"),
                    pending && saved && !failedWrite ? null : disk));
            }
            systems.Add(new DiagnosticsSystem(group, t("ui.diag_group_" + group), state, capabilities, extra));
        }
        var menusVerified = runtime.MainMenu == NativeMenuIntegrationState.Available && runtime.BaseMenu == NativeMenuIntegrationState.Available;
        var menuUnavailable = runtime.MainMenu == NativeMenuIntegrationState.Unavailable || runtime.BaseMenu == NativeMenuIntegrationState.Unavailable;
        string MenuState(NativeMenuIntegrationState state) => t(state == NativeMenuIntegrationState.Available ? "ui.working"
            : state == NativeMenuIntegrationState.NotObserved ? "ui.not_observed"
            : state == NativeMenuIntegrationState.AttachedUnverified ? "ui.attached_unverified" : "ui.unavailable");
        DiagnosticsHealth? MenuHealth(NativeMenuIntegrationState state) => state switch
        {
            NativeMenuIntegrationState.Available => DiagnosticsHealth.Working,
            NativeMenuIntegrationState.Unavailable => DiagnosticsHealth.Limited,
            _ => null
        };
        systems.Add(new DiagnosticsSystem("menu", t("ui.menu_access"), menuUnavailable ? DiagnosticsHealth.Limited : DiagnosticsHealth.Working,
            Array.Empty<DiagnosticsCapability>(), new[] {
                new DiagnosticsValue(t("ui.main_menu_entry"), MenuState(runtime.MainMenu), MenuHealth(runtime.MainMenu)),
                new DiagnosticsValue(t("ui.base_pause_entry"), MenuState(runtime.BaseMenu), MenuHealth(runtime.BaseMenu)),
                new DiagnosticsValue(string.Format(CultureInfo.CurrentCulture, t("ui.diag_hotkey_fallback"), runtime.Hotkey), t("ui.working"), DiagnosticsHealth.Working),
                new DiagnosticsValue(t("ui.diag_outside_raids"), t("ui.working"), DiagnosticsHealth.Working)
            }));
        systems = systems.OrderBy(s => Array.IndexOf(DiagnosticsCapabilityCatalog.GroupOrder.ToArray(), s.Id)).ToList();
        var tracking = systems.Where(s => s.Id != "menu" && s.Id != "storage").ToArray();
        var health = tracking.Any(s => s.Health == DiagnosticsHealth.Error) ? DiagnosticsHealth.Error
            : tracking.Any(s => s.Health == DiagnosticsHealth.Limited) ? DiagnosticsHealth.Limited : DiagnosticsHealth.Working;
        var bannerTitle = t(health == DiagnosticsHealth.Error ? "ui.diag_tracking_error" : health == DiagnosticsHealth.Limited ? "ui.diag_tracking_limited"
            : menusVerified ? "ui.diag_all_working" : "ui.diag_tracking_working");
        var bannerDetail = health == DiagnosticsHealth.Error ? t("ui.diag_tracking_error_detail")
            : health == DiagnosticsHealth.Limited ? t("ui.diag_tracking_limited_detail")
            : menuUnavailable ? string.Format(CultureInfo.CurrentCulture, t("ui.diag_menu_fallback"), runtime.Hotkey) : t("ui.diag_supported_recording");

        var issues = new List<DiagnosticsIssue>();
        foreach (var system in systems.Where(s => s.Id != "menu" && s.Capabilities.Any(c => c.Health == DiagnosticsHealth.Error)))
        {
            var affected = string.Join(", ", system.Capabilities.Where(c => c.Health == DiagnosticsHealth.Error).Select(c => c.Name));
            issues.Add(new DiagnosticsIssue("capability:" + system.Id, system.Name + " · " + t("ui.diag_tracking_unavailable"),
                string.Format(CultureInfo.CurrentCulture, t("ui.diag_affected_metrics"), affected) + "\n" + t("ui.diag_tracking_recovery"), "Error"));
        }
        var issueGroups = entries.Where(e => IsIssue(e.Severity)
            && !e.Message.StartsWith("Duplicate ", StringComparison.OrdinalIgnoreCase)
            && !IsDeferredResetMessage(e.Message))
            .Select(e => (Entry: e, Display: IssueText(e.Message, runtime.Hotkey, t)))
            .GroupBy(e => (Severity: e.Entry.Severity.ToUpperInvariant(), e.Display.Title, e.Display.Detail));
        foreach (var group in issueGroups)
        {
            var latest = group.First().Entry;
            issues.Add(new DiagnosticsIssue("log:" + group.Key.Severity + ":" + group.Key.Title + ":" + group.Key.Detail,
                group.Key.Title, group.Key.Detail, latest.Severity, Timestamp(latest.TimestampUtc), group.Count()));
        }
        if (menuUnavailable && !entries.Any(e => IsIssue(e.Severity) && e.Message.StartsWith("M17 native ", StringComparison.Ordinal)))
            issues.Add(new DiagnosticsIssue("menu", t("ui.diag_menu_issue"), string.Format(CultureInfo.CurrentCulture, t("ui.diag_menu_issue_detail"), runtime.Hotkey), "Warning"));
        var log = entries.Select((e, i) => new DiagnosticsLogEntry(e.TimestampUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + i.ToString(CultureInfo.InvariantCulture),
            DetailForDisplay(e.Message, t), e.Severity, Timestamp(e.TimestampUtc)));
        var stats = profile.Statistics; var run = stats.RunTotals; var craft = stats.Crafting; var world = stats.WorldTime;
        var repaired = stats.Economy.WasRepairedFromInvalidState || craft.WasRepairedFromInvalidState || world.WasRepairedFromInvalidState
            || run.ItemStatistics.WasRepairedFromInvalidState || run.CombatStatistics.WasRepairedFromInvalidState
            || run.EquipmentStatistics.WasRepairedFromInvalidState || run.WeaponStatistics.WasRepairedFromInvalidState;
        var arithmetic = stats.Economy.MoneyArithmeticSaturated || stats.Economy.CashArithmeticSaturated
            || craft.CompletionArithmeticUnavailable || craft.QuantityArithmeticUnavailable || craft.ResourceActionArithmeticUnavailable
            || craft.ResourceQuantityArithmeticUnavailable || craft.CurrencyActionArithmeticUnavailable || craft.CurrencyAmountArithmeticUnavailable
            || world.CalendarArithmeticUnavailable || world.ObservedElapsedArithmeticUnavailable || world.SleepSessionArithmeticUnavailable || world.SleepElapsedArithmeticUnavailable;
        var opened = runtime.OpenResult;
        var recovery = new[] {
            new DiagnosticsValue(t("ui.diag_profile_load"), opened == null ? unavailable : t(opened.CreatedNew ? "ui.diag_new_profile" : opened.RecoveredSnapshot ? "ui.diag_recovered" : "ui.diag_normal")),
            new DiagnosticsValue(t("ui.diag_backup_recovery"), opened == null ? unavailable : t(opened.LoadSource == AtomicJsonLoadSource.Backup ? "ui.diag_used" : "ui.diag_not_required")),
            new DiagnosticsValue(t("ui.diag_temp_recovery"), opened == null ? unavailable : t(opened.LoadSource == AtomicJsonLoadSource.Temporary ? "ui.diag_used" : "ui.diag_not_required")),
            new DiagnosticsValue(t("ui.diag_repairs"), t(repaired ? "ui.diag_present" : "ui.diag_none")),
            new DiagnosticsValue(t("ui.diag_arithmetic"), t(arithmetic ? "ui.limited" : "ui.diag_exact")),
            new DiagnosticsValue(t("ui.diag_run_recovery"), opened == null ? unavailable : t(opened.InterruptedRunRecovered ? "ui.diag_recovered" : "ui.diag_not_required")),
            new DiagnosticsValue(t("ui.diag_session_recovery"), opened == null ? unavailable : t(opened.InterruptedSessionRecovered ? "ui.diag_recovered" : "ui.diag_not_required"))
        };
        var versions = new[] {
            new DiagnosticsValue(t("ui.diag_game_version"), string.IsNullOrWhiteSpace(runtime.GameVersion) ? unavailable : runtime.GameVersion),
            new DiagnosticsValue(t("ui.diag_mod_version"), ProductInfo.Version),
            new DiagnosticsValue(t("ui.diag_profile_format"), profile.SchemaVersion.ToString(CultureInfo.InvariantCulture)),
            new DiagnosticsValue(t("ui.diag_statistics_format"), stats.SchemaVersion.ToString(CultureInfo.InvariantCulture))
        };
        var lastSaved = runtime.SaveReceipt?.GenerationId == generation ? Timestamp(runtime.SaveReceipt.SavedUtc) : unavailable;
        return new DiagnosticsPresentation(generation, string.Format(CultureInfo.CurrentCulture, t("ui.diag_save_slot"), profile.Slot), lastSaved,
            runtime.DataRoot, runtime.Hotkey, health, bannerTitle, bannerDetail, systems, issues.Take(12), log, versions, recovery, runtime.OpenDetail);
    }

    public static string Timestamp(DateTime utc) => utc == default ? UiText.Get("ui.unavailable")
        : utc.ToLocalTime().ToString("yyyy-MM-dd - HH:mm:ss", CultureInfo.InvariantCulture);
    public static bool IsIssue(string severity) => severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) || severity.Equals("Error", StringComparison.OrdinalIgnoreCase);
    private static bool IsPersistenceFailure(string message) => message.StartsWith("Failed to persist", StringComparison.Ordinal)
        || message.StartsWith("Profile flush failed", StringComparison.Ordinal);
    // A pending transition invalidates this presentation. Once a usable generation
    // is published again, its old deferral messages belong only in the technical log.
    private static bool IsDeferredResetMessage(string message) => message.StartsWith("M17 UI reset awaiting completion", StringComparison.Ordinal)
        || message.StartsWith("User profile reset", StringComparison.Ordinal);
    private static string DetailForDisplay(string detail, Func<string, string> text) => detail == ThrowableInitializationDetail
        ? text("ui.diag_throwable_contract") : detail;
    private static (string Title, string Detail) IssueText(string message, string hotkey, Func<string, string> t)
    {
        if (message.StartsWith("M17 UI export failed", StringComparison.Ordinal)) return (t("ui.diag_export_failed"), t("ui.diag_export_failed_detail"));
        if (message.StartsWith("M17 UI reset failed; previous profile remains active", StringComparison.Ordinal)
            || message.StartsWith("User reset failed with the original generation preserved", StringComparison.Ordinal))
            return (t("ui.diag_reset_failed"), t("ui.diag_reset_failed_detail"));
        if (message.StartsWith("M17 UI reset could not be completed", StringComparison.Ordinal))
            return (t("ui.diag_reset_unconfirmed"), t("ui.diag_reset_unconfirmed_detail"));
        if (message.StartsWith("M17 UI clipboard", StringComparison.Ordinal)) return (t("ui.diag_clipboard_failed"), t("ui.diag_clipboard_failed_detail"));
        if (message.StartsWith("M17 native ", StringComparison.Ordinal)) return (t("ui.diag_menu_issue"), string.Format(CultureInfo.CurrentCulture, t("ui.diag_menu_issue_detail"), hotkey));
        if (IsPersistenceFailure(message)) return (t("ui.diag_storage_issue"), t("ui.diag_storage_issue_detail"));
        return (t("ui.diag_recent_warning"), message + "\n" + t("ui.issue_guidance"));
    }
}
