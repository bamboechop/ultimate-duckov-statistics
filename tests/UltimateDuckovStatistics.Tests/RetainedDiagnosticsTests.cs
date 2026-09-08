using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

#pragma warning disable CA1861
[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class RetainedDiagnosticsTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 1, 0, 0, DateTimeKind.Utc);
    private static ProfileDocument Profile(string generation = "g") => new()
    {
        GenerationId = generation,
        Slot = 2,
        Statistics = new ProfileStatistics { SaveGenerationId = generation },
        Capabilities = DiagnosticsCapabilityCatalog.All.Select(d => new CapabilityRecord
        {
            AdapterId = d.Id,
            Version = "observed-contract-v1",
            Detail = "Observed " + d.Id,
            State = AdapterCapabilityState.Supported
        }).ToList()
    };
    private static DiagnosticsRuntimeSnapshot Runtime(string generation = "g") => new()
    {
        GenerationId = generation,
        MainMenu = NativeMenuIntegrationState.Available,
        BaseMenu = NativeMenuIntegrationState.Available,
        DataRoot = "C:\\UDS data",
        GameVersion = "native-version"
    };
    private static StatisticsPanelProjection Project(ProfileDocument profile) => StatisticsPanelProjectionFactory.Create(profile,
        new EconomyMetricCapabilities(), new CraftingMetricCapabilities(), new WorldTimeMetricCapabilities());
    private static DiagnosticsPresentation Present(ProfileDocument profile, DiagnosticsRuntimeSnapshot? runtime = null) =>
        DiagnosticsPresentationFactory.Create(Project(profile), profile.GenerationId, runtime ?? Runtime(profile.GenerationId))!;
    private static DiagnosticsCapability Cap(DiagnosticsPresentation p, string id) => p.Systems.SelectMany(s => s.Capabilities).Single(c => c.Id == id);
    private static DiagnosticEntry Entry(int minute, string severity, string message) => new()
    { TimestampUtc = Now.AddMinutes(minute), Severity = severity, Message = message };

    [Fact]
    public void CatalogCoversEveryPublishedMetricFamilyAndTheNativeThrowableContractExactlyOnce()
    {
        var families = new[] { typeof(CombatCapabilityIds), typeof(WeaponCapabilityIds), typeof(EquipmentCapabilityIds),
            typeof(ContainerCapabilityIds), typeof(EconomyCapabilityIds), typeof(EconomyHoldingsCapabilityIds),
            typeof(CraftingCapabilityIds), typeof(WorldTimeCapabilityIds) };
        var expected = families.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!))
            .Concat(new[] { "native-item-use", "native-healing-attribution", ThrowableUseObservation.CapabilityId, "native-grenade-hazard-attribution",
                "native-run-lifecycle", "native-main-duck-movement", "native-map-identity", "native-multi-map-route", "native-save-lifecycle" })
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, DiagnosticsCapabilityCatalog.All.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(expected.Length, expected.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("items", Assert.Single(DiagnosticsCapabilityCatalog.All, d => d.Id == ThrowableUseObservation.CapabilityId).Group);
    }

    [Fact]
    public void MissingHarmonyExplainsFailedCombatHooksAndPreservesPublicCallbacks()
    {
        var profile = Profile();
        var records = CombatNativeContractPolicy.ToRecords(CombatNativeContractPolicy.CreateCapabilities(new CombatHookSupport
        { PublicMeleeSwing = true, PublicPlayerDeath = true }), "native");
        profile.Capabilities.RemoveAll(c => records.Any(r => r.AdapterId == c.AdapterId));
        profile.Capabilities.AddRange(records);
        var runtime = Runtime(); runtime.HarmonyLoaded = false;
        var p = Present(profile, runtime);
        Assert.Equal(DiagnosticsHealth.Error, p.Health);
        Assert.Equal(UiText.Get("ui.diag_harmony_banner"), p.BannerTitle);
        Assert.Contains(UiText.Get("ui.diag_harmony_recovery"), p.BannerDetail, StringComparison.Ordinal);
        Assert.Contains("Harmony", p.Systems.Single(s => s.Id == "combat").Status, StringComparison.Ordinal);
        foreach (var record in records)
        {
            var cap = Cap(p, record.AdapterId);
            Assert.Equal(record.State == AdapterCapabilityState.DisabledIncompatible, cap.HarmonyUnavailable);
            Assert.Equal(record.Detail, cap.Detail);
            if (record.State == AdapterCapabilityState.Supported) Assert.Equal("Working", cap.Status);
        }
        Assert.All(p.Systems.Where(s => s.Id != "combat" && s.Id != "storage"), s => Assert.Equal("Working", s.Status));
        var issue = Assert.Single(p.Issues);
        Assert.Contains("Harmony", issue.Title, StringComparison.Ordinal);
        Assert.Contains(UiText.Get("ui.diag_harmony_recovery"), issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingHarmonyIdentifiesSleepAndPreservesIndependentClock()
    {
        var profile = Profile();
        var records = WorldTimeNativeContractPolicy.ToRecords(
            WorldTimeNativeContractPolicy.ClockSupportedSleepUnavailable("clock", "HarmonyLib is not loaded."), "native");
        profile.Capabilities.RemoveAll(c => records.Any(r => r.AdapterId == c.AdapterId));
        profile.Capabilities.AddRange(records);
        var runtime = Runtime(); runtime.HarmonyLoaded = false;
        var p = Present(profile, runtime);
        foreach (var record in records)
            Assert.Equal(record.State == AdapterCapabilityState.DisabledIncompatible, Cap(p, record.AdapterId).HarmonyUnavailable);
        Assert.Equal("Working", Cap(p, WorldTimeCapabilityIds.CalendarDays).Status);
        Assert.Contains("Harmony", p.Systems.Single(s => s.Id == "world").Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void OldMissingHarmonyLogDoesNotDiagnoseCurrentContractFailure(bool? loaded)
    {
        var profile = Profile();
        profile.Capabilities.Single(c => c.AdapterId == "native-healing-attribution").State = AdapterCapabilityState.DisabledIncompatible;
        var runtime = Runtime(); runtime.HarmonyLoaded = loaded;
        runtime.Entries = new[] { Entry(1, "Warning", "HarmonyLib is not loaded. Install and activate Workshop item 3589088839 before UDS.") };
        var p = Present(profile, runtime);
        Assert.Equal(UiText.Get("ui.diag_tracking_error"), p.BannerTitle);
        Assert.Equal("Error", p.Systems.Single(s => s.Id == "items").Status);
        Assert.False(Cap(p, "native-healing-attribution").HarmonyUnavailable);
        Assert.Equal("Error", Cap(p, "native-healing-attribution").Status);
        Assert.Single(p.Log); // Historical evidence remains in the log, not current cause detection.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsentHarmonyDoesNotExplainMissingOrConflictingCapabilityRecords(bool conflicting)
    {
        var profile = Profile();
        profile.Capabilities.Single(c => c.AdapterId == ThrowableUseObservation.CapabilityId).State = AdapterCapabilityState.DisabledIncompatible;
        if (conflicting) profile.Capabilities.Add(new CapabilityRecord { AdapterId = ThrowableUseObservation.CapabilityId });
        else profile.Capabilities.RemoveAll(c => c.AdapterId == ThrowableUseObservation.CapabilityId);
        var runtime = Runtime(); runtime.HarmonyLoaded = false;
        var p = Present(profile, runtime);
        Assert.Equal("Unavailable", Cap(p, ThrowableUseObservation.CapabilityId).Status);
        Assert.False(Cap(p, ThrowableUseObservation.CapabilityId).HarmonyUnavailable);
        Assert.Equal(UiText.Get("ui.diag_tracking_error"), p.BannerTitle);
    }

    [Fact]
    public void MixedFailureKeepsOtherCauseAndStorageFailureVisible()
    {
        var profile = Profile();
        foreach (var id in new[] { "native-healing-attribution", "native-item-use", EconomyCapabilityIds.MoneyAmountDirection })
            profile.Capabilities.Single(c => c.AdapterId == id).State = AdapterCapabilityState.DisabledIncompatible;
        var runtime = Runtime(); runtime.HarmonyLoaded = false; runtime.ProfilePersistenceFailed = true;
        var p = Present(profile, runtime);
        Assert.Equal("Error", Cap(p, "native-item-use").Status);
        Assert.Equal("Error", p.Systems.Single(s => s.Id == "economy").Status);
        Assert.Equal("Error", p.Systems.Single(s => s.Id == "storage").Status);
        var issue = p.Issues.Single(i => i.Id == "capability:items");
        Assert.Contains(UiText.Get("ui.diag_harmony_recovery"), issue.Detail, StringComparison.Ordinal);
        Assert.Contains(UiText.Get("ui.diag_tracking_recovery"), issue.Detail, StringComparison.Ordinal);
        Assert.Contains(UiText.Get("ui.diag_tracking_recovery"), p.Issues.Single(i => i.Id == "capability:economy").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoringHarmonyAndCapabilitiesClearsCurrentCause()
    {
        var profile = Profile();
        profile.Capabilities.Single(c => c.AdapterId == "native-healing-attribution").State = AdapterCapabilityState.DisabledIncompatible;
        var runtime = Runtime(); runtime.HarmonyLoaded = false;
        var before = Present(profile, runtime);
        runtime.HarmonyLoaded = true;
        profile.Capabilities.Single(c => c.AdapterId == "native-healing-attribution").State = AdapterCapabilityState.Supported;
        var after = Present(profile, runtime);
        Assert.Equal(UiText.Get("ui.diag_harmony_banner"), before.BannerTitle);
        Assert.Equal(DiagnosticsHealth.Working, after.Health);
        Assert.Equal(UiText.Get("ui.diag_all_working"), after.BannerTitle);
        Assert.Empty(after.Issues);
        Assert.All(after.Systems.SelectMany(s => s.Capabilities), c => Assert.False(c.HarmonyUnavailable));
    }

    [Fact]
    public void MissingHarmonyExplainsShortcutIsolationWithoutBlamingMenuEntries()
    {
        var runtime = Runtime(); runtime.HarmonyLoaded = false;
        runtime.ShortcutIsolation = NativeMenuIntegrationState.Unavailable;
        var p = Present(Profile(), runtime);
        Assert.Equal(DiagnosticsHealth.Limited, p.Health);
        Assert.Equal(UiText.Get("ui.diag_harmony_banner"), p.BannerTitle);
        var menu = p.Systems.Single(s => s.Id == "menu");
        Assert.Contains("Harmony", menu.Status, StringComparison.Ordinal);
        Assert.Equal("Working", menu.ExtraRows[0].Value);
        Assert.Equal("Working", menu.ExtraRows[1].Value);
        Assert.Equal(UiText.Get("ui.diag_harmony_not_loaded"), menu.ExtraRows[3].Value);
    }

    [Fact]
    public void AvailableMenuEntryPublishesWorkingColorEvenWhenSiblingIsUnavailable()
    {
        var runtime = Runtime(); runtime.BaseMenu = NativeMenuIntegrationState.Unavailable;
        var menu = Present(Profile(), runtime).Systems.Single(s => s.Id == "menu");
        Assert.Equal(DiagnosticsHealth.Working, menu.ExtraRows[0].Health);
        Assert.Equal(DiagnosticsHealth.Limited, menu.ExtraRows[1].Health);
    }

    [Fact]
    public void RealNativeInitializationReportsWorkingWithinExpectedAttributionCoverage()
    {
        var oldVersion = UnityEngine.Application.version;
        UnityEngine.Application.version = "2.3.30";
        try
        {
            var records = new List<CapabilityRecord>();
            using var economy = new NativeEconomyAdapter(() => "g", () => null, () => null, () => null, () => false,
                _ => true, records.AddRange, _ => { });
            using var weapon = new NativeWeaponFireAdapter(() => "g", () => null, () => null, _ => true, records.AddRange, _ => { });
            economy.Initialize(); weapon.Initialize();
            records.AddRange(CombatNativeContractPolicy.ToRecords(CombatNativeContractPolicy.CreateSupportedCapabilities(), "native"));
            records.AddRange(EquipmentNativeContractPolicy.ToRecords(EquipmentNativeContractPolicy.CreateSupportedCapabilities(), "native"));
            records.AddRange(CraftingNativeContractPolicy.ToRecords(CraftingNativeContractPolicy.Supported("completion", "formula", "cost-items", "currency"), "native"));
            records.AddRange(EconomyHoldingsNativeContractPolicy.ToRecords(EconomyHoldingsNativeContractPolicy.Supported("money", "cash", "liquid"), "native"));
            records.AddRange(WorldTimeNativeContractPolicy.ToRecords(WorldTimeNativeContractPolicy.Supported("clock", "sleep"), "native"));
            records.Add(ContainerNativeContractPolicy.ToRecord(ContainerNativeContractPolicy.Supported(), "native"));
            // Public lifecycle/item callbacks and the trusted throwable release boundary publish these exact IDs.
            records.AddRange(new[] { "native-item-use", "native-healing-attribution", ThrowableUseObservation.CapabilityId, "native-grenade-hazard-attribution",
                "native-run-lifecycle", "native-main-duck-movement", "native-map-identity", "native-multi-map-route", "native-save-lifecycle" }
                .Select(id => new CapabilityRecord { AdapterId = id, State = AdapterCapabilityState.Supported }));
            var profile = Profile(); profile.Capabilities = records;
            var p = Present(profile);
            Assert.Equal(DiagnosticsHealth.Working, p.Health); Assert.Empty(p.Issues);
            Assert.All(p.Systems.Where(s => s.Id != "storage"), s => Assert.Equal(DiagnosticsHealth.Working, s.Health));
            foreach (var id in new[] { EconomyCapabilityIds.MoneySourceAttribution, EconomyCapabilityIds.MoneyContextAttribution,
                EconomyCapabilityIds.CashExternalAcquisition })
            {
                Assert.Equal("Working", Cap(p, id).Status);
                Assert.Equal(nameof(AdapterCapabilityState.Experimental), Cap(p, id).State);
                Assert.Equal(records.Single(r => r.AdapterId == id).Detail, Cap(p, id).Detail);
            }
            Assert.Equal(AdapterCapabilityState.Experimental, economy.MetricCapabilities.MoneySourceAttribution.State);
            Assert.Equal(AdapterCapabilityState.Experimental, economy.MetricCapabilities.MoneyContextAttribution.State);
            Assert.Equal(AdapterCapabilityState.Experimental, economy.MetricCapabilities.CashExternalAcquisition.State);
            Assert.Equal("Working", Cap(p, EconomyCapabilityIds.MoneyAmountDirection).Status);
            Assert.Equal("Working", Cap(p, EconomyCapabilityIds.CashAmountDirection).Status);
            Assert.Equal(records.Count, p.Systems.Sum(s => s.Capabilities.Count));
        }
        finally { UnityEngine.Application.version = oldVersion; }
    }

    [Theory]
    [InlineData(EconomyCapabilityIds.MoneyAmountDirection, EconomyCapabilityIds.CashAmountDirection)]
    [InlineData(EconomyCapabilityIds.CashAmountDirection, EconomyCapabilityIds.MoneyAmountDirection)]
    [InlineData(EconomyHoldingsCapabilityIds.CurrentMoney, EconomyHoldingsCapabilityIds.CurrentCash)]
    [InlineData(EconomyCapabilityIds.MoneySourceAttribution, EconomyCapabilityIds.MoneyAmountDirection)]
    [InlineData(EconomyCapabilityIds.MoneyContextAttribution, EconomyCapabilityIds.MoneyAmountDirection)]
    [InlineData(EconomyCapabilityIds.CashExternalAcquisition, EconomyCapabilityIds.CashAmountDirection)]
    public void AFailedEconomyMetricKeepsItsIndependentSiblingWorking(string failing, string working)
    {
        var profile = Profile(); profile.Capabilities.Single(c => c.AdapterId == failing).State = AdapterCapabilityState.DisabledIncompatible;
        var p = Present(profile);
        Assert.Equal(DiagnosticsHealth.Error, Cap(p, failing).Health); Assert.Equal(DiagnosticsHealth.Working, Cap(p, working).Health);
        Assert.Equal(DiagnosticsHealth.Error, p.Health);
        var issue = Assert.Single(p.Issues);
        Assert.Contains(Cap(p, failing).Name, issue.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(Cap(p, working).Name, issue.Detail, StringComparison.Ordinal);
        Assert.Contains("Player.log", issue.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrConflictingCurrentContractFailsClosedWithExactIdentity(bool conflicting)
    {
        var profile = Profile();
        if (conflicting) profile.Capabilities.Add(new CapabilityRecord { AdapterId = ThrowableUseObservation.CapabilityId, State = AdapterCapabilityState.Supported });
        else profile.Capabilities.RemoveAll(c => c.AdapterId == ThrowableUseObservation.CapabilityId);
        var p = Present(profile); var cap = Cap(p, ThrowableUseObservation.CapabilityId);
        Assert.Equal(DiagnosticsHealth.Error, cap.Health); Assert.Equal("Unavailable", cap.Status);
        Assert.Contains(conflicting ? "Conflicting" : "No current", cap.Detail, StringComparison.Ordinal);
        Assert.Equal(DiagnosticsHealth.Working, Cap(p, "native-item-use").Health);
        Assert.Single(p.Issues);
    }

    [Fact]
    public void ExperimentalMetricIsLimitedAndDoesNotClaimTrackingFailure()
    {
        var profile = Profile(); profile.Capabilities.Single(c => c.AdapterId == "native-item-use").State = AdapterCapabilityState.Experimental;
        var p = Present(profile);
        Assert.Equal(DiagnosticsHealth.Limited, p.Health); Assert.Equal("Limited", Cap(p, "native-item-use").Status);
        Assert.Empty(p.Issues); Assert.Equal(DiagnosticsHealth.Working, Cap(p, ThrowableUseObservation.CapabilityId).Health);
    }

    [Fact]
    public void ForeignCapabilityIdsRemainVisibleAndAreNeverJoinedByCaseOrName()
    {
        var profile = Profile(); profile.Capabilities.AddRange(new[] {
            new CapabilityRecord { AdapterId = "mod:Metric", State = AdapterCapabilityState.Supported, Detail = "upper" },
            new CapabilityRecord { AdapterId = "mod:metric", State = AdapterCapabilityState.Experimental, Detail = "lower" } });
        var other = Assert.Single(Present(profile).Systems, s => s.Id == "other");
        Assert.Equal(new[] { "mod:Metric", "mod:metric" }, other.Capabilities.Select(c => c.Id));
        Assert.Equal(new[] { "upper", "lower" }, other.Capabilities.Select(c => c.Detail));
    }

    [Theory]
    [InlineData((int)NativeMenuIntegrationState.NotObserved, true, false)]
    [InlineData((int)NativeMenuIntegrationState.NotObserved, false, false)]
    [InlineData((int)NativeMenuIntegrationState.AttachedUnverified, true, false)]
    [InlineData((int)NativeMenuIntegrationState.AttachedUnverified, false, false)]
    [InlineData((int)NativeMenuIntegrationState.Unavailable, true, true)]
    [InlineData((int)NativeMenuIntegrationState.Unavailable, false, true)]
    public void OnlyAnObservedMenuFailureDowngradesMenuHealth(int state, bool mainMenu, bool issueExpected)
    {
        var runtime = Runtime();
        if (mainMenu) runtime.MainMenu = (NativeMenuIntegrationState)state;
        else runtime.BaseMenu = (NativeMenuIntegrationState)state;
        var p = Present(Profile(), runtime); var menu = Assert.Single(p.Systems, s => s.Id == "menu");
        Assert.Equal(DiagnosticsHealth.Working, p.Health);
        Assert.Equal(issueExpected ? DiagnosticsHealth.Limited : DiagnosticsHealth.Working, menu.Health);
        Assert.Equal<DiagnosticsHealth?>(issueExpected ? DiagnosticsHealth.Limited : null, menu.ExtraRows[mainMenu ? 0 : 1].Health);
        Assert.Equal(UiText.Get(state == (int)NativeMenuIntegrationState.NotObserved ? "ui.not_observed"
            : state == (int)NativeMenuIntegrationState.AttachedUnverified ? "ui.attached_unverified" : "ui.unavailable"),
            menu.ExtraRows[mainMenu ? 0 : 1].Value);
        Assert.Equal(UiText.Get("ui.diag_tracking_working"), p.BannerTitle);
        Assert.Equal(DiagnosticsHealth.Working, Assert.Single(menu.ExtraRows, r => r.Label.Contains("F8", StringComparison.Ordinal)).Health);
        Assert.Equal(issueExpected ? 1 : 0, p.Issues.Count);
        if (issueExpected) { Assert.Contains("F8", p.BannerDetail, StringComparison.Ordinal); Assert.Equal("Warning", p.Issues[0].Severity); }
    }

    [Fact]
    public void LatestFiftyLogsAreDetachedAndRoutineDuplicateNotesAreNotIssues()
    {
        var entries = Enumerable.Range(0, 70).Select(i => Entry(i, "Info", "ordinary " + i)).ToList();
        entries.Add(Entry(71, "Warning", "Duplicate callback ignored"));
        entries.Add(Entry(72, "Error", "M17 UI export failed: denied"));
        var runtime = Runtime(); runtime.Entries = entries; var p = Present(Profile(), runtime);
        Assert.Equal(50, p.Log.Count); Assert.Equal("M17 UI export failed: denied", p.Log[0].Message);
        Assert.Equal("ordinary 22", p.Log[^1].Message);
        Assert.Single(p.Issues); Assert.Contains("Try exporting again", p.Issues[0].Detail, StringComparison.Ordinal);
        entries[^1].Message = "mutated"; entries.Clear();
        Assert.Equal(50, p.Log.Count); Assert.Equal("M17 UI export failed: denied", p.Log[0].Message);
    }

    [Fact]
    public void IssuesAreBoundedAndRetainNewestActionableEvents()
    {
        var runtime = Runtime(); runtime.Entries = Enumerable.Range(0, 20).Select(i => Entry(i, "Warning", "warning " + i)).ToArray();
        var p = Present(Profile(), runtime);
        Assert.Equal(12, p.Issues.Count); Assert.Contains("warning 19", p.Issues[0].Detail, StringComparison.Ordinal);
        Assert.Contains("warning 8", p.Issues[^1].Detail, StringComparison.Ordinal);
        Assert.All(p.Issues, issue => Assert.NotEmpty(issue.Timestamp));
    }

    [Fact]
    public void RepeatedStorageReportsGroupAcrossSecondsWithoutRemovingTechnicalEvidence()
    {
        var runtime = Runtime();
        runtime.Entries = Enumerable.Range(0, 7).Select(i => new DiagnosticEntry
        {
            TimestampUtc = Now.AddSeconds(i / 4),
            Severity = "Error",
            Message = i % 2 == 0 ? "Failed to persist profile: disk full" : "Profile flush failed: disk full"
        }).ToArray();
        var p = Present(Profile(), runtime);
        var issue = Assert.Single(p.Issues);
        Assert.Equal(7, issue.ReportCount);
        Assert.Equal(DiagnosticsPresentationFactory.Timestamp(Now.AddSeconds(1)), issue.Timestamp);
        Assert.Equal(7, p.Log.Count);
        var selection = new DiagnosticsSelection(); selection.Refresh(p); selection.Toggle("g", "issue:" + issue.Id);
        runtime.Entries = runtime.Entries.Append(Entry(2, "Error", "Failed to persist profile: disk full")).ToArray();
        selection.Refresh(Present(Profile(), runtime));
        Assert.True(selection.Expanded("issue:" + issue.Id));
        Assert.Equal(8, Assert.Single(selection.Snapshot!.Issues).ReportCount);
    }

    [Fact]
    public void GroupingKeepsDifferentGuidanceAndSeveritySeparateAndCapsAfterGrouping()
    {
        var runtime = Runtime();
        runtime.Entries = Enumerable.Range(0, 20).Select(i => Entry(i, "Error", "Failed to persist profile: retry"))
            .Concat(new[] { Entry(21, "Warning", "Failed to persist profile: retry"),
                Entry(22, "Error", "M17 UI export failed: denied"), Entry(23, "Warning", "different warning") }).ToArray();
        var p = Present(Profile(), runtime);
        Assert.Equal(4, p.Issues.Count); Assert.Equal(23, p.Log.Count);
        Assert.Contains(p.Issues, issue => issue.ReportCount == 20 && issue.Severity == "Error");
        Assert.Contains(p.Issues, issue => issue.ReportCount == 1 && issue.Detail.Contains("different warning", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("M17 UI reset failed; previous profile remains active: denied", "existing UDS profile remains active")]
    [InlineData("User reset failed with the original generation preserved: archive denied", "existing UDS profile remains active")]
    [InlineData("M17 UI clipboard unavailable", "export completed")]
    [InlineData("Profile flush failed: disk full", "Pending data is retained")]
    [InlineData("M17 native main-menu integration unavailable", "F8")]
    public void OperationFailuresGiveBoundarySpecificRecoveryInsteadOfFalseSuccess(string message, string guidance)
    {
        var runtime = Runtime(); runtime.Entries = new[] { Entry(1, "Error", message) };
        Assert.Contains(guidance, Assert.Single(Present(Profile(), runtime).Issues).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SettledResetDeferralsRemainInTechnicalLogWithoutClaimingTheOperationIsStillPending()
    {
        var profile = Profile(); var runtime = Runtime(); runtime.TransitionPending = true;
        runtime.Entries = new[] {
            Entry(1, "Warning", "M17 UI reset awaiting completion. writer blocked"),
            Entry(2, "Warning", "User profile reset transition deferred at native publication"),
            Entry(3, "Error", "M17 UI reset failed; previous profile remains active. archive denied") };
        Assert.Null(DiagnosticsPresentationFactory.Create(Project(profile), "g", runtime));
        runtime.TransitionPending = false;
        var settled = Present(profile, runtime);
        Assert.Equal(3, settled.Log.Count); Assert.Equal("Reset failed", Assert.Single(settled.Issues).Title);
        Assert.Contains(settled.Log, entry => entry.Message.StartsWith("M17 UI reset awaiting completion", StringComparison.Ordinal));
        Assert.Contains(settled.Log, entry => entry.Message.StartsWith("User profile reset", StringComparison.Ordinal));
        runtime.Entries = runtime.Entries.Take(2).ToArray();
        Assert.Empty(Present(profile, runtime).Issues);
    }

    [Fact]
    public void UnconfirmedResetNeverClaimsThePreviousProfileWasPreserved()
    {
        var runtime = Runtime(); runtime.Entries = new[] { Entry(1, "Error", "M17 UI reset could not be completed for the requested generation. slot changed") };
        var issue = Assert.Single(Present(Profile(), runtime).Issues);
        Assert.Equal("Reset not completed", issue.Title);
        Assert.Contains("current profile", issue.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("No statistics were removed", issue.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("remains active", issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulThrowableContractOmitsAutomaticHistoryNoticesWithoutMutatingStoredEvidence()
    {
        const string nativeDetail = "Successful main-player Skill_Grenade releases in raids; recorded totals begin with this adapter. Earlier throws are unavailable.";
        var profile = Profile(); var record = profile.Capabilities.Single(c => c.AdapterId == ThrowableUseObservation.CapabilityId);
        record.Detail = nativeDetail;
        var entry = Entry(1, "Info", nativeDetail); var runtime = Runtime(); runtime.Entries = new[] { entry };
        var p = Present(profile, runtime); var capability = Cap(p, ThrowableUseObservation.CapabilityId);
        Assert.Equal("Successful main-player Skill_Grenade releases in raids.", capability.Detail);
        Assert.Equal(capability.Detail, Assert.Single(p.Log).Message);
        Assert.Equal(DiagnosticsHealth.Working, capability.Health); Assert.Empty(p.Issues);
        Assert.Equal(nativeDetail, record.Detail); Assert.Equal(nativeDetail, entry.Message);
        const string failure = "Throwable tracking stopped: release patches are not trusted.";
        record.State = AdapterCapabilityState.DisabledIncompatible; record.Detail = failure; entry.Severity = "Error"; entry.Message = failure;
        var failed = Present(profile, runtime);
        Assert.Equal(failure, Cap(failed, ThrowableUseObservation.CapabilityId).Detail);
        Assert.Equal(failure, Assert.Single(failed.Log).Message);
        Assert.Contains(failed.Issues, issue => issue.Detail.Contains(failure, StringComparison.Ordinal));
    }

    [Fact]
    public void MenuLogAndMenuStateDoNotDuplicateTheSameAccessIssue()
    {
        var runtime = Runtime(); runtime.MainMenu = NativeMenuIntegrationState.Unavailable;
        runtime.Entries = new[] { Entry(1, "Warning", "M17 native main-menu integration unavailable") };
        Assert.Single(Present(Profile(), runtime).Issues);
    }

    [Fact]
    public void GenerationOrTransitionMismatchHidesTheSnapshotInsteadOfShowingAnotherProfile()
    {
        var profile = Profile(); var projection = Project(profile); var runtime = Runtime("other");
        Assert.Null(DiagnosticsPresentationFactory.Create(projection, "g", runtime));
        runtime.GenerationId = "g"; runtime.TransitionPending = true;
        Assert.Null(DiagnosticsPresentationFactory.Create(projection, "g", runtime));
        runtime.TransitionPending = false; profile.Statistics.SaveGenerationId = "other";
        Assert.Null(DiagnosticsPresentationFactory.Create(projection, "g", runtime));
        profile.Statistics.SaveGenerationId = "g"; profile.GenerationId = "other";
        Assert.Null(DiagnosticsPresentationFactory.Create(projection, "g", runtime));
    }

    [Fact]
    public void SaveReceiptProvesDiskStateOnlyForItsOwnGenerationAndRevision()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProfileRepository(directory.Path, () => Now, () => "saved-generation");
        repository.Open(new SaveIdentitySnapshot { Slot = 2, SaveFilePresent = true, SaveFileCreationUtcTicks = Now.Ticks });
        var profile = Profile(repository.CurrentGenerationId); profile.Revision = repository.Current.Revision;
        var runtime = Runtime(profile.GenerationId); runtime.SaveReceipt = repository.LastSaveReceipt;
        runtime.OpenResult = repository.LastOpenResult;
        var saved = Present(profile, runtime);
        Assert.Equal(DiagnosticsPresentationFactory.Timestamp(Now), saved.LastSaved);
        Assert.Equal(DiagnosticsHealth.Working, Assert.Single(saved.Systems, s => s.Id == "storage").Health);
        Assert.Contains(saved.Recovery, r => r.Value == "New profile");
        profile.Revision++;
        var pending = Assert.Single(Present(profile, runtime).Systems, s => s.Id == "storage");
        Assert.Equal(DiagnosticsHealth.Working, pending.Health);
        Assert.Equal("Pending", Assert.Single(pending.ExtraRows).Value);
        Assert.Null(Assert.Single(pending.ExtraRows).Health);
        runtime.Entries = new[] { Entry(1, "Error", "Failed to persist snapshot") };
        Assert.Equal(DiagnosticsHealth.Error, Assert.Single(Present(profile, runtime).Systems, s => s.Id == "storage").Health);
        var another = Profile("another"); runtime.GenerationId = "another";
        Assert.Equal("Unavailable", Present(another, runtime).LastSaved);
        runtime.Entries = Array.Empty<DiagnosticEntry>();
        Assert.Equal(DiagnosticsHealth.Limited, Assert.Single(Present(another, runtime).Systems, s => s.Id == "storage").Health);
        runtime.SaveReceipt = null;
        Assert.Equal(DiagnosticsHealth.Limited, Assert.Single(Present(another, runtime).Systems, s => s.Id == "storage").Health);
    }

    [Fact]
    public void WorldTimeBetweenScheduledWritesRemainsPendingWithoutDegradingWorkingStorage()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProfileRepository(directory.Path, () => Now, () => "world-time-generation");
        repository.Open(new SaveIdentitySnapshot { Slot = 3, SaveFilePresent = false });
        repository.SetCapabilitySnapshot(Profile().Capabilities, new EconomyMetricCapabilities(),
            WorldTimeNativeContractPolicy.Supported("clock", "sleep"), new CraftingMetricCapabilities());
        var receipt = repository.LastSaveReceipt;
        var runtime = Runtime(repository.CurrentGenerationId);
        runtime.SaveReceipt = receipt;
        var cadence = new NativeWorldTimePersistenceCadence();
        cadence.Start(0);
        for (var second = 1; second < NativeWorldTimePersistenceCadence.DurablePersistenceIntervalSeconds; second++)
        {
            Assert.True(cadence.ShouldPublish(second));
            Assert.True(repository.RecordWorldTimeDeferred(new WorldTimeMutation(0, TimeSpan.TicksPerSecond, 0, 0)));
            cadence.RecordPublicationAttempt(succeeded: true, changed: true, second);
            Assert.False(cadence.ShouldSchedulePersistence(second));
            Assert.Same(receipt, repository.LastSaveReceipt);
            var storage = Assert.Single(Present(repository.Current, runtime).Systems, s => s.Id == "storage");
            Assert.Equal(DiagnosticsHealth.Working, storage.Health);
            Assert.Equal("Pending", Assert.Single(storage.ExtraRows).Value);
            Assert.Null(Assert.Single(storage.ExtraRows).Health);
        }
        Assert.True(cadence.ShouldSchedulePersistence(NativeWorldTimePersistenceCadence.DurablePersistenceIntervalSeconds));
        var writer = new DeferredSnapshotWriter<ProfilePersistenceSnapshot>(repository.CapturePersistenceSnapshot, repository.SaveSnapshot);
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        runtime.SaveReceipt = repository.LastSaveReceipt;
        Assert.Equal(repository.Current.Revision, runtime.SaveReceipt!.Revision);
        var completed = Assert.Single(Present(repository.Current, runtime).Systems, s => s.Id == "storage");
        Assert.Equal(DiagnosticsHealth.Working, completed.Health);
        Assert.Equal("Working", Assert.Single(completed.ExtraRows).Value);
        Assert.Equal(DiagnosticsHealth.Working, Assert.Single(completed.ExtraRows).Health);
    }

    [Fact]
    public void RepeatedWriteFailureAfterRecoveryRemainsErrorWhenItsDiagnosticIsThrottled()
    {
        using var directory = new TemporaryDirectory();
        var priorDataPath = UnityEngine.Application.persistentDataPath;
        UnityEngine.Application.persistentDataPath = directory.Path;
        Saves.SavesSystem.ResetNativeState();
        try
        {
            var seconds = 0d;
            using var coordinator = new NativeProfileCoordinator(() => seconds);
            coordinator.Initialize();
            void ChangeWorldTime()
            {
                Assert.True(coordinator.HandleWorldTime(new WorldTimeMutation(0, TimeSpan.TicksPerSecond, 0, 0)));
                Assert.True(coordinator.RequestWorldTimePersistence());
            }
            DeferredWriteState CompleteWrite()
            {
                var state = coordinator.TickProfilePersistence();
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    if (state != DeferredWriteState.Pending) return true;
                    state = coordinator.TickProfilePersistence();
                    return state != DeferredWriteState.Pending;
                }, TimeSpan.FromSeconds(5)));
                return state;
            }
            int FailureEntries() => coordinator.DiagnosticEntries.Count(e => e.Message.StartsWith("Failed to persist deferred profile snapshot", StringComparison.Ordinal));
            ChangeWorldTime();
            using (var held = new FileStream(coordinator.CurrentProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(DeferredWriteState.Failed, CompleteWrite());
            Assert.Equal(1, FailureEntries());
            seconds = 1;
            Assert.Equal(DeferredWriteState.Succeeded, CompleteWrite());
            Assert.Equal(coordinator.Current!.Revision, coordinator.LastSaveReceipt!.Revision);
            var saved = coordinator.LastSaveReceipt;
            seconds = 2;
            ChangeWorldTime();
            try
            {
                using var held = new FileStream(coordinator.CurrentProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Assert.Equal(DeferredWriteState.Failed, CompleteWrite());
                Assert.Equal(1, FailureEntries());
                Assert.Same(saved, coordinator.LastSaveReceipt);
                var runtime = Runtime(coordinator.CurrentGenerationId);
                runtime.SaveReceipt = coordinator.LastSaveReceipt;
                runtime.ProfilePersistenceFailed = coordinator.HasProfilePersistenceFailure;
                runtime.Entries = coordinator.DiagnosticEntries;
                var storage = Assert.Single(Present(coordinator.Current, runtime).Systems, s => s.Id == "storage");
                Assert.Equal(DiagnosticsHealth.Error, storage.Health);
                Assert.Equal("Error", Assert.Single(storage.ExtraRows).Value);
            }
            finally
            {
                seconds = 3;
                Assert.Equal(DeferredWriteState.Succeeded, CompleteWrite());
                Assert.False(coordinator.HasProfilePersistenceFailure);
            }
        }
        finally
        {
            Saves.SavesSystem.ResetNativeState();
            UnityEngine.Application.persistentDataPath = priorDataPath;
        }
    }

    [Fact]
    public void RepairAndArithmeticEvidenceAppearWithoutChangingIndependentWorkingContracts()
    {
        var profile = Profile(); profile.Statistics.Crafting.WasRepairedFromInvalidState = true;
        profile.Statistics.Economy.MoneyArithmeticSaturated = true;
        var p = Present(profile);
        Assert.Contains(p.Recovery, r => r.Label == "Automatic repairs" && r.Value == "Present");
        Assert.Contains(p.Recovery, r => r.Label == "Arithmetic status" && r.Value == "Limited");
        Assert.Equal(DiagnosticsHealth.Working, p.Health);
    }

    [Fact]
    public void RefreshPreservesOnlySurvivingExpansionsAndClampsIndependentColumns()
    {
        var runtime = Runtime(); runtime.Entries = new[] { Entry(1, "Warning", "first") };
        var p = Present(Profile(), runtime); var selection = new DiagnosticsSelection(); selection.Refresh(p);
        Assert.True(selection.Expanded("technical")); Assert.False(selection.Expanded("issue:" + p.Issues[0].Id));
        Assert.False(selection.Expanded("recovery")); Assert.False(selection.Expanded("log"));
        Assert.True(selection.Toggle("g", "issue:" + p.Issues[0].Id));
        Assert.True(selection.Toggle("g", "system:economy")); Assert.True(selection.Toggle("g", "contracts:economy"));
        Assert.True(selection.Filter("g", DiagnosticsLogFilter.Errors));
        selection.Capture("left", 900); selection.Capture("right", 200);
        runtime.Entries = Array.Empty<DiagnosticEntry>(); selection.Refresh(Present(Profile(), runtime));
        Assert.False(selection.Expanded("issue:" + p.Issues[0].Id)); Assert.True(selection.Expanded("system:economy"));
        Assert.True(selection.Expanded("contracts:economy")); Assert.Equal(DiagnosticsLogFilter.Errors, selection.LogFilter);
        Assert.Equal(600, selection.Offset("left", 400, 1000)); Assert.Equal(200, selection.Offset("right", 400, 1000));
        Assert.Equal(0, selection.Offset("left", 400, 100));
        selection.Capture("right", float.NaN); selection.Capture("right", float.PositiveInfinity);
        Assert.Equal(200, selection.Offset("right", 400, 1000));
    }

    [Fact]
    public void ChangedOrUnavailableGenerationClearsInteractiveStateAndRejectsStaleCallbacks()
    {
        var selection = new DiagnosticsSelection(); selection.Refresh(Present(Profile()));
        selection.Toggle("g", "system:items"); selection.Filter("g", DiagnosticsLogFilter.Errors); selection.Capture("left", 100);
        selection.Refresh(Present(Profile("next")));
        Assert.False(selection.Expanded("system:items")); Assert.Equal(DiagnosticsLogFilter.All, selection.LogFilter);
        Assert.Equal(0, selection.Offset("left", 100, 500));
        Assert.False(selection.Toggle("g", "system:items")); Assert.False(selection.Filter("g", DiagnosticsLogFilter.Errors));
        Assert.False(selection.Filter("next", (DiagnosticsLogFilter)999));
        selection.Refresh(null); Assert.Null(selection.Snapshot); Assert.Empty(selection.VisibleLog);
        Assert.False(selection.Toggle("next", "technical"));
    }

    [Fact]
    public void LogFiltersSelectExactSeverityAndKeepOrder()
    {
        var runtime = Runtime(); runtime.Entries = new[] { Entry(1, "Info", "info"), Entry(2, "wArNiNg", "warning"), Entry(3, "ERROR", "error") };
        var selection = new DiagnosticsSelection(); selection.Refresh(Present(Profile(), runtime));
        Assert.Equal(new[] { "error", "warning", "info" }, selection.VisibleLog.Select(e => e.Message));
        selection.Filter("g", DiagnosticsLogFilter.Warnings); Assert.Equal("warning", Assert.Single(selection.VisibleLog).Message);
        selection.Filter("g", DiagnosticsLogFilter.Errors); Assert.Equal("error", Assert.Single(selection.VisibleLog).Message);
        selection.Filter("g", DiagnosticsLogFilter.All); Assert.Equal(3, selection.VisibleLog.Count());
    }

    [Fact]
    public void NarrowAndLongLocalizedValuesUseBoundedStackedLayout()
    {
        Assert.True(DiagnosticsLayoutPolicy.Stack(1179)); Assert.False(DiagnosticsLayoutPolicy.Stack(1180));
        Assert.Equal(680, DiagnosticsLayoutPolicy.ColumnWidth(1400, false)); Assert.Equal(760, DiagnosticsLayoutPolicy.ColumnWidth(760, true));
        var wide = DiagnosticsLayoutPolicy.Columns(680, 260, 280);
        Assert.False(wide.Stacked); Assert.Equal(680, wide.Label + 20 + wide.Value);
        var narrow = DiagnosticsLayoutPolicy.Columns(400, 600, 900);
        Assert.True(narrow.Stacked); Assert.Equal(400, narrow.Label); Assert.Equal(400, narrow.Value);
        Assert.Equal(720, DiagnosticsLayoutPolicy.ColumnViewport(true, 800, 2000));
        Assert.Equal(200, DiagnosticsLayoutPolicy.ColumnViewport(true, 800, 200));
        Assert.Equal(800, DiagnosticsLayoutPolicy.ColumnViewport(false, 800, 2000));
        Assert.Equal((660f, 740f), DiagnosticsLayoutPolicy.Modal(1400, 800));
        Assert.Equal((340f, 140f), DiagnosticsLayoutPolicy.Modal(400, 200));
    }

    [Theory]
    [InlineData("F8", true)]
    [InlineData("F10", true)]
    [InlineData("Escape", false)]
    [InlineData("Return", false)]
    [InlineData("Tab", false)]
    [InlineData("LeftArrow", false)]
    [InlineData("RightArrow", false)]
    [InlineData("UpArrow", false)]
    [InlineData("DownArrow", false)]
    [InlineData("Space", false)]
    [InlineData("LeftControl", false)]
    [InlineData("Mouse0", false)]
    [InlineData("JoystickButton0", false)]
    [InlineData("", false)]
    public void HotkeyPolicyPreservesCancelAndReservedControlKeys(string key, bool allowed)
        => Assert.Equal(allowed, PanelHotkeyPolicy.IsAllowed(key));
}
#pragma warning restore CA1861
