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
        GenerationId = generation, Slot = 2, Statistics = new ProfileStatistics { SaveGenerationId = generation },
        Capabilities = DiagnosticsCapabilityCatalog.All.Select(d => new CapabilityRecord
        {
            AdapterId = d.Id, Version = "observed-contract-v1", Detail = "Observed " + d.Id,
            State = d.BaselineLimitation ? AdapterCapabilityState.DisabledIncompatible : AdapterCapabilityState.Supported
        }).ToList()
    };
    private static DiagnosticsRuntimeSnapshot Runtime(string generation = "g") => new()
    {
        GenerationId = generation, MainMenu = NativeMenuIntegrationState.Available,
        BaseMenu = NativeMenuIntegrationState.Available, DataRoot = "C:\\UDS data", GameVersion = "native-version"
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
            .Concat(new[] { "native-item-use", "native-healing-attribution", ThrowableUseObservation.CapabilityId,
                "native-run-lifecycle", "native-main-duck-movement", "native-map-identity", "native-multi-map-route", "native-save-lifecycle" })
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, DiagnosticsCapabilityCatalog.All.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(expected.Length, expected.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("items", Assert.Single(DiagnosticsCapabilityCatalog.All, d => d.Id == ThrowableUseObservation.CapabilityId).Group);
    }

    [Fact]
    public void KnownBaselineLimitationsDoNotBecomeTrackingErrorsOrRecentIssues()
    {
        var p = Present(Profile());
        Assert.Equal(DiagnosticsHealth.Working, p.Health); Assert.Empty(p.Issues);
        Assert.All(p.Systems.Where(s => s.Id != "storage"), s => Assert.Equal(DiagnosticsHealth.Working, s.Health));
        var limitations = p.Systems.SelectMany(s => s.Capabilities).Where(c => c.BaselineLimitation).ToArray();
        Assert.NotEmpty(limitations); Assert.Equal(limitations.Length, p.Limitations.Count);
        Assert.All(limitations, cap => { Assert.Equal(DiagnosticsHealth.Limited, cap.Health); Assert.Equal("Unavailable", cap.Status); });
        Assert.Contains(limitations, cap => cap.Id == EquipmentCapabilityIds.ToteActivation);
        Assert.Contains(limitations, cap => cap.Id == CraftingCapabilityIds.CurrencyMoneyCashSplit);
    }

    [Fact]
    public void RealNativeInitializationAndSupportedFactoriesHaveNoFalseTrackingError()
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
            records.AddRange(new[] { "native-item-use", "native-healing-attribution", ThrowableUseObservation.CapabilityId,
                "native-run-lifecycle", "native-main-duck-movement", "native-map-identity", "native-multi-map-route", "native-save-lifecycle" }
                .Select(id => new CapabilityRecord { AdapterId = id, State = AdapterCapabilityState.Supported }));
            var profile = Profile(); profile.Capabilities = records;
            var p = Present(profile);
            Assert.Equal(DiagnosticsHealth.Limited, p.Health); Assert.Empty(p.Issues);
            Assert.DoesNotContain(p.Systems, s => s.Health == DiagnosticsHealth.Error);
            var terminal = Cap(p, EconomyCapabilityIds.CashTerminalOutcomes);
            Assert.True(terminal.BaselineLimitation); Assert.Equal("Unavailable", terminal.Status);
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible.ToString(), terminal.State);
            Assert.Equal("Limited", Cap(p, EconomyCapabilityIds.MoneySourceAttribution).Status);
            Assert.Equal("Limited", Cap(p, EconomyCapabilityIds.CashExternalAcquisition).Status);
            Assert.Equal("Working", Cap(p, EconomyCapabilityIds.MoneyAmountDirection).Status);
            Assert.Equal("Working", Cap(p, EconomyCapabilityIds.CashAmountDirection).Status);
            Assert.Contains(p.Limitations, row => row.Label == terminal.Name);
            Assert.Equal(records.Count, p.Systems.Sum(s => s.Capabilities.Count));
        }
        finally { UnityEngine.Application.version = oldVersion; }
    }

    [Theory]
    [InlineData(EconomyCapabilityIds.MoneyAmountDirection, EconomyCapabilityIds.CashAmountDirection)]
    [InlineData(EconomyCapabilityIds.CashAmountDirection, EconomyCapabilityIds.MoneyAmountDirection)]
    [InlineData(EconomyHoldingsCapabilityIds.CurrentMoney, EconomyHoldingsCapabilityIds.CurrentCash)]
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
    [InlineData((int)NativeMenuIntegrationState.NotObserved, false)]
    [InlineData((int)NativeMenuIntegrationState.AttachedUnverified, false)]
    [InlineData((int)NativeMenuIntegrationState.Unavailable, true)]
    public void MenuFallbackDoesNotDowngradeWorkingStatistics(int state, bool issueExpected)
    {
        var runtime = Runtime(); runtime.MainMenu = (NativeMenuIntegrationState)state;
        var p = Present(Profile(), runtime); var menu = Assert.Single(p.Systems, s => s.Id == "menu");
        Assert.Equal(DiagnosticsHealth.Working, p.Health); Assert.Equal(DiagnosticsHealth.Limited, menu.Health);
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
        Assert.Equal(DiagnosticsHealth.Limited, Assert.Single(Present(profile, runtime).Systems, s => s.Id == "storage").Health);
        runtime.Entries = new[] { Entry(1, "Error", "Failed to persist snapshot") };
        Assert.Equal(DiagnosticsHealth.Error, Assert.Single(Present(profile, runtime).Systems, s => s.Id == "storage").Health);
        var another = Profile("another"); runtime.GenerationId = "another";
        Assert.Equal("Unavailable", Present(another, runtime).LastSaved);
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
        Assert.True(selection.Expanded("technical")); Assert.True(selection.Expanded("issue:" + p.Issues[0].Id));
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
