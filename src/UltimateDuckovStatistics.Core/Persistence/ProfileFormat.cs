using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

public static class ProfileFormat
{
    public static string? ValidateRecoveryCandidate(ProfileDocument profile)
    {
        if (profile == null)
        {
            return "Profile document is missing.";
        }

        // Incompatible documents are selected intact so Open preserves them as unsupported.
        // Never normalize them or fall back to an older generation over their primary.
        if (!string.Equals(profile.FormatId, ProductInfo.ProfileFormatId, StringComparison.Ordinal)
            || profile.SchemaVersion != ProductInfo.SchemaVersion
            || profile.Statistics?.SchemaVersion > ProductInfo.SchemaVersion)
        {
            return null;
        }

        if (profile.Statistics?.SchemaVersion != ProductInfo.SchemaVersion)
            return "Current-schema profile roots are incomplete.";

        var missingPath = FindMissingRequiredDataMember(profile, "Profile");
        if (missingPath != null)
        {
            return $"Current-schema profile roots are incomplete. Missing required data member: {missingPath}.";
        }

        if (profile.Statistics.Runs.Any(run => run.SchemaVersion != ProductInfo.SchemaVersion))
            return "Current-format profile contains an incompatible run schema.";

        try { RunDataSchema.Validate(profile.Statistics); }
        catch (ArgumentException exception)
        {
            return $"Current-schema Runs data foundation is invalid: {exception.Message}";
        }

        foreach (var scope in EconomyRecoveryScopes(profile))
        {
            try
            {
                EconomyStatisticsReducer.ValidateRecoveryCandidate(scope.Economy);
            }
            catch (ArgumentException exception)
            {
                return $"Current-schema {scope.Path} contains invalid economy state: {exception.Message}";
            }
        }

        foreach (var scope in M14RecoveryScopes(profile))
        {
            try
            {
                WeaponStatisticsReducer.ValidateAggregate(scope.WeaponStatistics);
                EquipmentStatisticsReducer.ValidateRecoveryCandidate(scope.EquipmentStatistics);
            }
            catch (ArgumentException exception)
            {
                return $"Current-schema {scope.Path} contains invalid M14 association state: {exception.Message}";
            }
            catch (OverflowException exception)
            {
                return $"Current-schema {scope.Path} contains invalid M14 association state: {exception.Message}";
            }
        }

        foreach (var run in profile.Statistics.Runs)
        {
            try
            {
                RouteStatisticsReducer.ValidateCapabilities(run.RouteCapabilities);
                RouteStatisticsReducer.ValidateAssociations(run.Segments, run.SegmentEventAssociations);
                ValidateHistoricalEventAttribution(run);
            }
            catch (ArgumentException exception)
            {
                return $"Current-schema run '{run.RunId}' contains invalid route-association state: {exception.Message}";
            }
        }

        try
        {
            RunReducer.ValidateProfileEconomyComposition(profile.Statistics);
        }
        catch (ArgumentException exception)
        {
            return $"Current-schema economy fan-out is inconsistent: {exception.Message}";
        }

        try
        {
            WorldTimeStatisticsReducer.Validate(profile.Statistics.WorldTime);
        }
        catch (ArgumentException exception)
        {
            return $"Current-schema world-time state is invalid: {exception.Message}";
        }

        try
        {
            CraftingStatisticsReducer.Validate(profile.Statistics.Crafting);
        }
        catch (ArgumentException exception)
        {
            return $"Current-schema crafting state is invalid: {exception.Message}";
        }
        catch (OverflowException exception)
        {
            return $"Current-schema crafting state is invalid: {exception.Message}";
        }

        try
        {
            EconomyHoldingsReducer.ValidateRecoveryCandidate(
                profile.Statistics.Holdings,
                profile.GenerationId);
        }
        catch (ArgumentException exception)
        {
            return $"Current-schema economy holdings state is invalid: {exception.Message}";
        }

        if (profile.DeferredItemPersistence != null)
        {
            var deferred = profile.DeferredItemPersistence;
            if (deferred.RunId != null && string.IsNullOrWhiteSpace(deferred.RunId))
            {
                return "Deferred lifetime item persistence watermark has an invalid run identity.";
            }
            try
            {
                ItemStatisticsAggregateReducer.Validate(
                    deferred.AppliedLifetimeStatistics);
                if (!ItemStatisticsAggregateReducer.IsCompositionConsistent(
                        deferred.AppliedLifetimeStatistics))
                {
                    return "Deferred lifetime item persistence watermark is compositionally inconsistent.";
                }
                if (deferred.RunId == null
                    && (deferred.AppliedLifetimeStatistics.Overall.ActivationCount != 0
                        || deferred.AppliedLifetimeStatistics.Overall.ActualHealthRestored != 0
                        || deferred.AppliedLifetimeStatistics.Overall.AmountsByUnit.Count != 0
                        || deferred.AppliedLifetimeStatistics.Items.Count != 0
                        || deferred.AppliedLifetimeStatistics.Groups.Count != 0
                        || deferred.AppliedLifetimeStatistics.RecentEventIds.Count != 0))
                {
                    return "Deferred lifetime item persistence watermark has values without an active run identity.";
                }
                var lifetime = new Domain.ItemStatisticsAggregate
                {
                    Overall = profile.Statistics.Overall,
                    Items = profile.Statistics.Items,
                    Groups = profile.Statistics.Groups,
                    RecentEventIds = profile.Statistics.RecentEventIds
                };
                if (!ItemStatisticsAggregateReducer.TrySubtract(
                        lifetime,
                        deferred.AppliedLifetimeStatistics,
                        out _))
                {
                    return "Deferred lifetime item persistence watermark is not a valid subset of lifetime statistics.";
                }
                EconomyStatisticsReducer.ValidateRecoveryCandidate(deferred.AppliedLifetimeEconomy);
                if (deferred.RunId == null && !EconomyStatisticsReducer.IsEmpty(deferred.AppliedLifetimeEconomy))
                    return "Deferred lifetime economy watermark has values without an active run identity.";
                if (!EconomyStatisticsReducer.TrySubtract(
                        profile.Statistics.Economy,
                        deferred.AppliedLifetimeEconomy,
                        out _))
                    return "Deferred lifetime economy watermark is not a valid subset of lifetime statistics.";
            }
            catch (ArgumentException exception)
            {
                return $"Deferred lifetime item persistence watermark is invalid: {exception.Message}";
            }
        }

        return null;
    }

    private static IEnumerable<(string Path, EconomyStatisticsAggregate Economy)> EconomyRecoveryScopes(ProfileDocument profile)
    {
        yield return ("profile lifetime", profile.Statistics.Economy);
        yield return ("completed-run totals", profile.Statistics.RunTotals.Economy);
        foreach (var map in profile.Statistics.RunTotals.Maps)
            yield return ($"starting-map totals '{map.Key}'", map.Value.Economy);
        foreach (var map in profile.Statistics.RunTotals.RouteMaps)
            yield return ($"route-map totals '{map.Key}'", map.Value.Economy);
        foreach (var run in profile.Statistics.Runs)
        {
            yield return ($"run '{run.RunId}'", run.Economy);
            foreach (var segment in run.Segments)
                yield return ($"run '{run.RunId}' segment '{segment.SegmentId}'", segment.Economy);
        }
    }

    private static IEnumerable<(string Path, WeaponStatisticsAggregate WeaponStatistics, EquipmentStatisticsAggregate EquipmentStatistics)> M14RecoveryScopes(ProfileDocument profile)
    {
        yield return ("completed-run totals", profile.Statistics.RunTotals.WeaponStatistics, profile.Statistics.RunTotals.EquipmentStatistics);
        foreach (var map in profile.Statistics.RunTotals.Maps)
            yield return ($"starting-map totals '{map.Key}'", map.Value.WeaponStatistics, map.Value.EquipmentStatistics);
        foreach (var map in profile.Statistics.RunTotals.RouteMaps)
            yield return ($"route-map totals '{map.Key}'", map.Value.WeaponStatistics, map.Value.EquipmentStatistics);
        foreach (var run in profile.Statistics.Runs)
        {
            yield return ($"run '{run.RunId}'", run.WeaponStatistics, run.EquipmentStatistics);
            foreach (var segment in run.Segments)
                yield return ($"run '{run.RunId}' segment '{segment.SegmentId}'", segment.WeaponStatistics, segment.EquipmentStatistics);
        }
    }

    public static bool CompactEconomyReplayEvidenceAfterRecovery(ProfileDocument profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var changed = false;
        changed |= EconomyStatisticsReducer.ClearReplayCursor(
            profile.Statistics.RunTotals.Economy);
        foreach (var map in profile.Statistics.RunTotals.Maps.Values)
            changed |= EconomyStatisticsReducer.ClearReplayCursor(map.Economy);
        foreach (var map in profile.Statistics.RunTotals.RouteMaps.Values)
            changed |= EconomyStatisticsReducer.ClearReplayCursor(map.Economy);
        foreach (var run in profile.Statistics.Runs)
        {
            changed |= EconomyStatisticsReducer.ClearReplayCursor(run.Economy);
            foreach (var segment in run.Segments)
                changed |= EconomyStatisticsReducer.ClearReplayCursor(segment.Economy);
        }
        if (profile.DeferredItemPersistence?.AppliedLifetimeEconomy != null)
            changed |= EconomyStatisticsReducer.ClearReplayCursor(
                profile.DeferredItemPersistence.AppliedLifetimeEconomy);
        return changed;
    }

    private static string? FindMissingRequiredDataMember(
        object? value,
        string path,
        bool nullDictionaryValuesAreRepairable = false)
    {
        if (value == null)
        {
            return path;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or decimal or DateTime or Guid)
        {
            return null;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key == null)
                {
                    return path + "[missing key]";
                }

                if (entry.Value == null)
                {
                    if (nullDictionaryValuesAreRepairable)
                    {
                        continue;
                    }

                    return $"{path}[{entry.Key}]";
                }

                var missing = FindMissingRequiredDataMember(entry.Value, $"{path}[{entry.Key}]");
                if (missing != null)
                {
                    return missing;
                }
            }

            return null;
        }

        if (value is IEnumerable sequence)
        {
            var index = 0;
            foreach (var item in sequence)
            {
                var missing = FindMissingRequiredDataMember(item, $"{path}[{index}]");
                if (missing != null)
                {
                    return missing;
                }

                index++;
            }

            return null;
        }

        if (type.GetCustomAttribute<DataContractAttribute>() == null)
        {
            return null;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var dataMember = property.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember == null)
            {
                continue;
            }

            var memberPath = path + "." + property.Name;
            var memberValue = property.GetValue(value);
            if (memberValue == null && !dataMember.EmitDefaultValue)
            {
                continue;
            }

            var missing = FindMissingRequiredDataMember(
                memberValue,
                memberPath,
                NullDictionaryValuesAreRepairable(value, property));
            if (missing != null)
            {
                return missing;
            }
        }

        return null;
    }

    private static bool NullDictionaryValuesAreRepairable(object owner, PropertyInfo property)
    {
        if (owner is WeaponStatisticsAggregate)
        {
            return property.Name is nameof(WeaponStatisticsAggregate.Weapons)
                or nameof(WeaponStatisticsAggregate.AmmunitionTypes);
        }

        if (owner is CombatStatisticsAggregate)
        {
            return property.Name is nameof(CombatStatisticsAggregate.Enemies)
                or nameof(CombatStatisticsAggregate.Killers)
                or nameof(CombatStatisticsAggregate.Families)
                or nameof(CombatStatisticsAggregate.Causes)
                or nameof(CombatStatisticsAggregate.Weapons)
                or nameof(CombatStatisticsAggregate.Ammunition)
                or nameof(CombatStatisticsAggregate.Ownership);
        }

        if (owner is EquipmentStatisticsAggregate)
        {
            return property.Name is nameof(EquipmentStatisticsAggregate.Items)
                or nameof(EquipmentStatisticsAggregate.SelectedWeapons)
                or nameof(EquipmentStatisticsAggregate.Loadouts)
                or nameof(EquipmentStatisticsAggregate.TotemSets)
                or nameof(EquipmentStatisticsAggregate.CombatAssociations)
                or nameof(EquipmentStatisticsAggregate.TotemStates)
                or nameof(EquipmentStatisticsAggregate.Slots)
                or nameof(EquipmentStatisticsAggregate.SlottedWeapons);
        }

        return owner is Domain.ItemStatisticsAggregate
               && property.Name == nameof(Domain.ItemStatisticsAggregate.Groups);
    }

    public static bool IsCurrent(ProfileDocument profile) =>
        string.Equals(profile.FormatId, ProductInfo.ProfileFormatId, StringComparison.Ordinal)
        && profile.SchemaVersion == ProductInfo.SchemaVersion
        && profile.Statistics?.SchemaVersion == ProductInfo.SchemaVersion;

    public static bool Normalize(ProfileDocument profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (!IsCurrent(profile))
            throw new NotSupportedException("The UDS profile format is incompatible and will be preserved without conversion.");
        var statistics = profile.Statistics;
        var changed = false;
        if (statistics.CreatedUtc == default) { statistics.CreatedUtc = profile.CreatedUtc; changed = true; }
        if (statistics.UpdatedUtc == default) { statistics.UpdatedUtc = profile.UpdatedUtc; changed = true; }
        changed |= WorldTimeStatisticsReducer.NormalizePersisted(statistics.WorldTime);
        changed |= CraftingStatisticsReducer.NormalizePersisted(statistics.Crafting);
        changed |= EconomyHoldingsReducer.NormalizePersisted(statistics.Holdings, profile.GenerationId, downgradeCurrent: true);
        foreach (var scope in EconomyRecoveryScopes(profile))
            changed |= EconomyStatisticsReducer.NormalizePersisted(scope.Economy);
        foreach (var scope in M14RecoveryScopes(profile))
        {
            changed |= WeaponStatisticsReducer.NormalizePersisted(scope.WeaponStatistics).Changed;
            changed |= EquipmentStatisticsReducer.NormalizePersisted(scope.EquipmentStatistics);
        }
        foreach (var scope in RunDataSchema.Scopes(statistics))
            changed |= CombatStatisticsReducer.NormalizePersisted(scope.Combat).Changed;
        changed |= ItemStatisticsAggregateReducer.NormalizePersisted(statistics.RunTotals.ItemStatistics);
        changed |= ContainerStatisticsReducer.NormalizePersisted(statistics.RunTotals.ContainerStatistics);
        foreach (var map in statistics.RunTotals.Maps.Values)
        {
            changed |= ItemStatisticsAggregateReducer.NormalizePersisted(map.ItemStatistics);
            changed |= ContainerStatisticsReducer.NormalizePersisted(map.ContainerStatistics);
        }
        foreach (var map in statistics.RunTotals.RouteMaps.Values) changed |= NormalizeRouteMap(map);
        foreach (var run in statistics.Runs)
        {
            var repaired = RouteStatisticsReducer.NormalizeCapabilities(run.RouteCapabilities);
            run.RouteWasRepairedFromInvalidState |= repaired;
            changed |= repaired;
            changed |= ItemStatisticsAggregateReducer.NormalizePersisted(run.ItemStatistics);
            changed |= ContainerStatisticsReducer.NormalizePersisted(run.ContainerStatistics);
            changed |= NormalizeCurrentRunRoute(run);
        }
        if (profile.DeferredItemPersistence != null)
        {
            changed |= ItemStatisticsAggregateReducer.NormalizePersisted(profile.DeferredItemPersistence.AppliedLifetimeStatistics);
            changed |= EconomyStatisticsReducer.NormalizePersisted(profile.DeferredItemPersistence.AppliedLifetimeEconomy);
        }
        return changed;
    }

    private static bool NormalizeRouteMap(Domain.RouteAwareMapAggregate map)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(map.MapId)) { map.MapId = Domain.MapIdentity.UnknownId; changed = true; }
        if (string.IsNullOrWhiteSpace(map.DisplayName)) { map.DisplayName = Domain.MapIdentity.UnknownDisplayName; changed = true; }
        map.ItemStatistics ??= Repair(new Domain.ItemStatisticsAggregate(), ref changed);
        map.WeaponStatistics ??= Repair(new WeaponStatisticsAggregate(), ref changed);
        map.CombatStatistics ??= Repair(new CombatStatisticsAggregate(), ref changed);
        map.EquipmentStatistics ??= Repair(new EquipmentStatisticsAggregate(), ref changed);
        map.ContainerStatistics ??= Repair(new ContainerStatisticsAggregate(), ref changed);
        map.Economy ??= Repair(new EconomyStatisticsAggregate(), ref changed);
        changed |= ItemStatisticsAggregateReducer.NormalizePersisted(map.ItemStatistics);
        changed |= WeaponStatisticsReducer.NormalizePersisted(map.WeaponStatistics).Changed;
        changed |= CombatStatisticsReducer.NormalizePersisted(map.CombatStatistics).Changed;
        changed |= EquipmentStatisticsReducer.NormalizePersisted(map.EquipmentStatistics);
        changed |= ContainerStatisticsReducer.NormalizePersisted(map.ContainerStatistics);
        changed |= EconomyStatisticsReducer.NormalizePersisted(map.Economy);
        if (map.RunsVisited < 0) { map.RunsVisited = 0; changed = true; }
        if (map.SegmentVisits < 0) { map.SegmentVisits = 0; changed = true; }
        changed |= NormalizeDistance(map.ActiveDurationSeconds, value => map.ActiveDurationSeconds = value);
        changed |= NormalizeDistance(map.PhysicalDistance, value => map.PhysicalDistance = value);
        changed |= NormalizeDistance(map.TeleportDistance, value => map.TeleportDistance = value);
        changed |= NormalizeDistance(map.TransitionExcludedDistance, value => map.TransitionExcludedDistance = value);
        if (changed)
        {
            map.HistoricalUnavailable = true;
            map.WasRepairedFromInvalidState = true;
        }
        return changed;
    }

    private static bool NormalizeCurrentRunRoute(Domain.RunSummary run)
    {
        var changed = false;
        if (run.Segments.Count > RouteStatisticsReducer.MaximumSegmentsPerRun)
        {
            ClearInvalidRoute(run, "Persisted route exceeded the defensive segment bound.");
            return true;
        }

        try
        {
            var routeRepaired = RouteStatisticsReducer.NormalizePersisted(run.Segments);
            if (routeRepaired)
            {
                run.RouteWasRepairedFromInvalidState = true;
                RouteStatisticsReducer.DisableRoute(
                    run.RouteCapabilities,
                    "Persisted route data required repair and is no longer treated as supported evidence.");
                run.RouteSignature = string.Empty;
                run.EndingMapId = Domain.MapIdentity.UnknownId;
                run.EndingMapDisplayName = Domain.MapIdentity.UnknownDisplayName;
                run.EndingMapKnown = false;
                changed = true;
            }
            if (run.Segments.Count > 0)
            {
                RouteStatisticsReducer.Validate(run.Segments, allowOpenLast: false);
                if (!string.Equals(run.StartingMapId, run.Segments[0].MapId, StringComparison.Ordinal))
                {
                    ClearInvalidRoute(run, "Persisted starting map did not match the first retained segment.");
                    return true;
                }
            }
        }
        catch (ArgumentException)
        {
            ClearInvalidRoute(run, "Persisted route structure was invalid and has been disabled.");
            return true;
        }

        if (run.SegmentEventAssociations.Count > RouteStatisticsReducer.MaximumAggregateEventAssociationsPerRun)
        {
            ClearInvalidAttribution(run, "Persisted event attribution exceeded its route-cardinality bound.");
            changed = true;
        }
        else try
            {
                RouteStatisticsReducer.ValidateAssociations(run.Segments, run.SegmentEventAssociations);
            }
            catch (ArgumentException)
            {
                ClearInvalidAttribution(run, "Persisted event attribution contained an invalid segment join.");
                changed = true;
            }

        if (run.RouteCapabilities.OrderedRoute.State == Domain.AdapterCapabilityState.Supported)
        {
            var expectedSignature = RouteStatisticsReducer.BuildSignature(run.Segments);
            var first = run.Segments.FirstOrDefault();
            var last = run.Segments.LastOrDefault();
            if (first == null
                || last == null
                || !string.Equals(run.RouteSignature, expectedSignature, StringComparison.Ordinal)
                || !string.Equals(run.StartingMapId, first.MapId, StringComparison.Ordinal)
                || !string.Equals(run.EndingMapId, last.MapId, StringComparison.Ordinal)
                || !NearlyEqual(run.ActiveDurationSeconds, RouteStatisticsReducer.SaturatingSum(run.Segments.Select(segment => segment.ActiveDurationSeconds)))
                || !NearlyEqual(run.PhysicalDistance, RouteStatisticsReducer.SaturatingSum(run.Segments.Select(segment => segment.PhysicalDistance)))
                || !NearlyEqual(run.TeleportDistance, RouteStatisticsReducer.SaturatingSum(run.Segments.Select(segment => segment.TeleportDistance)))
                || !NearlyEqual(run.TransitionExcludedDistance, RouteStatisticsReducer.SaturatingSum(run.Segments.Select(segment => segment.TransitionExcludedDistance))))
            {
                ClearInvalidRoute(run, "Persisted route identity or totals were inconsistent with its segments.");
                changed = true;
            }
        }
        return changed;
    }

    private static void ClearInvalidRoute(Domain.RunSummary run, string provenance)
    {
        run.Segments.Clear();
        run.SegmentEventAssociations.Clear();
        run.RouteSignature = string.Empty;
        run.EndingMapId = Domain.MapIdentity.UnknownId;
        run.EndingMapDisplayName = Domain.MapIdentity.UnknownDisplayName;
        run.EndingMapKnown = false;
        run.RouteWasRepairedFromInvalidState = true;
        RouteStatisticsReducer.DisableRoute(run.RouteCapabilities, provenance);
    }

    private static void ClearInvalidAttribution(Domain.RunSummary run, string provenance)
    {
        run.SegmentEventAssociations.Clear();
        run.RouteWasRepairedFromInvalidState = true;
        run.HistoricalEventAttributionIncomplete = true;
        run.HistoricalEventAttributionProvenance = provenance;
        RouteStatisticsReducer.DisableAttribution(run.RouteCapabilities, provenance);
    }

    private static void ValidateHistoricalEventAttribution(Domain.RunSummary run)
    {
        if (run.HistoricalEventAttributionProvenance == null)
            throw new ArgumentException("Historical event-attribution provenance is missing.", nameof(run));
        if (run.HistoricalEventAttributionIncomplete
            && string.IsNullOrWhiteSpace(run.HistoricalEventAttributionProvenance))
            throw new ArgumentException("Incomplete historical event attribution has no provenance.", nameof(run));
        if (run.HistoricalEventAttributionIncomplete
            && run.RouteCapabilities.EventAttribution.State == Domain.AdapterCapabilityState.Supported)
            throw new ArgumentException("Incomplete historical event attribution cannot be marked exact.", nameof(run));
    }

    private static bool NormalizeDistance(double value, Action<double> replace)
    {
        if (value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value)) return false;
        replace(0);
        return true;
    }

    private static bool NearlyEqual(double left, double right) =>
        !double.IsNaN(left)
        && !double.IsInfinity(left)
        && Math.Abs(left - right) <= 0.000001;

    private static T Repair<T>(T value, ref bool changed)
    {
        changed = true;
        return value;
    }
}
