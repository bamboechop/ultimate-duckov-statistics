using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

public static class ProfileMigrator
{
    public static string? ValidateRecoveryCandidate(ProfileDocument profile)
    {
        if (profile == null)
        {
            return "Profile document is missing.";
        }

        // Older schemas are intentionally incomplete and must remain eligible for migration.
        // Future schemas are selected intact so Open can archive them as unsupported.
        if (profile.SchemaVersion != ProductInfo.SchemaVersion)
        {
            return null;
        }

        if (profile.Statistics?.SchemaVersion > ProductInfo.SchemaVersion)
        {
            return null;
        }

        if (profile.Statistics?.SchemaVersion != ProductInfo.SchemaVersion)
        {
            return "Current-schema profile roots are incomplete.";
        }

        var missingPath = FindMissingRequiredDataMember(profile, "Profile");
        if (missingPath != null)
        {
            return $"Current-schema profile roots are incomplete. Missing required data member: {missingPath}.";
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
                EquipmentStatisticsReducer.ValidateRecoveryCandidate(scope.EquipmentStatistics, ProductInfo.SchemaVersion);
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
        var lifetime = profile.Statistics.Economy;
        changed |= EconomyStatisticsReducer.CompactLegacyReplayEvidence(
            lifetime,
            clearReplayCursor: false);
        changed |= EconomyStatisticsReducer.CompactLegacyReplayEvidence(
            profile.Statistics.RunTotals.Economy,
            clearReplayCursor: true);
        foreach (var map in profile.Statistics.RunTotals.Maps.Values)
            changed |= EconomyStatisticsReducer.CompactLegacyReplayEvidence(map.Economy, clearReplayCursor: true);
        foreach (var map in profile.Statistics.RunTotals.RouteMaps.Values)
            changed |= EconomyStatisticsReducer.CompactLegacyReplayEvidence(map.Economy, clearReplayCursor: true);
        foreach (var run in profile.Statistics.Runs)
        {
            changed |= EconomyStatisticsReducer.CompactLegacyReplayEvidence(run.Economy, clearReplayCursor: true);
            foreach (var segment in run.Segments)
                changed |= EconomyStatisticsReducer.CompactLegacyReplayEvidence(segment.Economy, clearReplayCursor: true);
        }
        if (profile.DeferredItemPersistence?.AppliedLifetimeEconomy != null)
            changed |= EconomyStatisticsReducer.CompactLegacyReplayEvidence(
                profile.DeferredItemPersistence.AppliedLifetimeEconomy,
                clearReplayCursor: true);
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

    public static bool Migrate(ProfileDocument profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (profile.SchemaVersion > ProductInfo.SchemaVersion)
        {
            throw new NotSupportedException(
                $"Profile schema {profile.SchemaVersion} is newer than supported schema {ProductInfo.SchemaVersion}.");
        }

        if (profile.Statistics?.SchemaVersion > ProductInfo.SchemaVersion)
        {
            throw new NotSupportedException(
                $"Statistics schema {profile.Statistics.SchemaVersion} is newer than supported schema {ProductInfo.SchemaVersion}.");
        }

        var changed = false;
        var migratingCombat = profile.SchemaVersion < 5
                              || (profile.Statistics != null && profile.Statistics.SchemaVersion < 5);
        var migratingEquipment = profile.SchemaVersion < 6
                                 || (profile.Statistics != null && profile.Statistics.SchemaVersion < 6);
        var migratingContainers = profile.SchemaVersion < 7
                                  || (profile.Statistics != null && profile.Statistics.SchemaVersion < 7);
        var migratingRoutes = profile.SchemaVersion < 8
                              || (profile.Statistics != null && profile.Statistics.SchemaVersion < 8);
        var migratingEconomy = profile.SchemaVersion < 9
                               || (profile.Statistics != null && profile.Statistics.SchemaVersion < 9);
        var migratingLosslessRouteAssociation = profile.SchemaVersion < 10
                                                || (profile.Statistics != null && profile.Statistics.SchemaVersion < 10);
        var migratingCombatOwnership = profile.SchemaVersion < 11
                                       || (profile.Statistics != null && profile.Statistics.SchemaVersion < 11);
        var migratingWorldTime = profile.SchemaVersion < 12
                                 || (profile.Statistics != null && profile.Statistics.SchemaVersion < 12);
        var migratingCrafting = profile.SchemaVersion < 13
                                || (profile.Statistics != null && profile.Statistics.SchemaVersion < 13);
        var migratingM14Associations = profile.SchemaVersion < 14
                                       || (profile.Statistics != null && profile.Statistics.SchemaVersion < 14);
        var missingCurrentCombatRoot = !migratingCombat
                                       && (profile.Statistics == null || profile.Statistics.RunTotals == null);
        var missingCurrentEquipmentRoot = !migratingEquipment
                                          && (profile.Statistics == null || profile.Statistics.RunTotals == null);
        var missingCurrentContainerRoot = !migratingContainers
                                          && (profile.Statistics == null || profile.Statistics.RunTotals == null);
        if (profile.SchemaVersion < 1)
        {
            profile.SchemaVersion = 1;
            changed = true;
        }

        if (profile.Identity == null)
        {
            profile.Identity = new SaveIdentitySnapshot { Slot = profile.Slot };
            changed = true;
        }

        if (profile.Statistics == null)
        {
            profile.Statistics = new ProfileStatistics();
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 1)
        {
            profile.Statistics.SchemaVersion = 1;
            changed = true;
        }

        if (profile.Statistics.CreatedUtc == default)
        {
            profile.Statistics.CreatedUtc = profile.CreatedUtc;
            changed = true;
        }

        if (profile.Statistics.UpdatedUtc == default)
        {
            profile.Statistics.UpdatedUtc = profile.UpdatedUtc;
            changed = true;
        }

        if (profile.Statistics.Items == null)
        {
            profile.Statistics.Items = new Dictionary<string, ItemAggregate>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.Groups == null)
        {
            profile.Statistics.Groups = new Dictionary<string, AggregateTotals>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.RecentEventIds == null)
        {
            profile.Statistics.RecentEventIds = new List<string>();
            changed = true;
        }

        if (profile.Statistics.Overall == null)
        {
            profile.Statistics.Overall = new AggregateTotals();
            changed = true;
        }

        if (profile.Capabilities == null)
        {
            profile.Capabilities = new List<CapabilityRecord>();
            changed = true;
        }

        foreach (var item in profile.Statistics.Items.Values)
        {
            if (item.EffectTags == null)
            {
                item.EffectTags = new List<Domain.ItemEffectTag>();
                changed = true;
            }

            if (item.Totals == null)
            {
                item.Totals = new AggregateTotals();
                changed = true;
            }

            if (item.Totals.AmountsByUnit == null)
            {
                item.Totals.AmountsByUnit = new Dictionary<string, double>(StringComparer.Ordinal);
                changed = true;
            }
        }

        foreach (var group in profile.Statistics.Groups.Values)
        {
            if (group.AmountsByUnit == null)
            {
                group.AmountsByUnit = new Dictionary<string, double>(StringComparer.Ordinal);
                changed = true;
            }
        }

        if (profile.Statistics.Overall.AmountsByUnit == null)
        {
            profile.Statistics.Overall.AmountsByUnit = new Dictionary<string, double>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.Runs == null)
        {
            profile.Statistics.Runs = new List<Domain.RunSummary>();
            changed = true;
        }

        if (profile.Statistics.RunTotals == null)
        {
            profile.Statistics.RunTotals = new RunAggregateTotals();
            changed = true;
        }

        if (profile.Statistics.RunTotals.Outcomes == null)
        {
            profile.Statistics.RunTotals.Outcomes = new Dictionary<string, long>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.RunTotals.Maps == null)
        {
            profile.Statistics.RunTotals.Maps = new Dictionary<string, MapRunAggregate>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.RunTotals.RouteMaps == null)
        {
            profile.Statistics.RunTotals.RouteMaps = new Dictionary<string, Domain.RouteAwareMapAggregate>(StringComparer.Ordinal);
            profile.Statistics.RunTotals.RouteAwareHistoryUnavailable = true;
            changed = true;
        }
        if (profile.Statistics.RunTotals.ItemStatistics == null)
        {
            profile.Statistics.RunTotals.ItemStatistics = new Domain.ItemStatisticsAggregate
            {
                HistoricalUnavailable = true,
                WasRepairedFromInvalidState = !migratingRoutes
            };
            changed = true;
        }
        changed |= ItemStatisticsAggregateReducer.NormalizePersisted(profile.Statistics.RunTotals.ItemStatistics);
        if (profile.Statistics.Economy == null)
        {
            profile.Statistics.Economy = new EconomyStatisticsAggregate();
            changed = true;
        }

        if (profile.DeferredItemPersistence != null)
        {
            profile.DeferredItemPersistence.AppliedLifetimeStatistics ??= new Domain.ItemStatisticsAggregate();
            if (profile.DeferredItemPersistence.AppliedLifetimeEconomy == null)
            {
                profile.DeferredItemPersistence.AppliedLifetimeEconomy = new EconomyStatisticsAggregate();
                changed = true;
            }
            changed |= EconomyStatisticsReducer.NormalizePersisted(profile.DeferredItemPersistence.AppliedLifetimeEconomy);
        }
        changed |= EconomyStatisticsReducer.NormalizePersisted(profile.Statistics.Economy);
        if (migratingEconomy) changed |= MarkHistoricalEconomyUnavailable(profile.Statistics.Economy);
        if (profile.Statistics.WorldTime == null)
        {
            profile.Statistics.WorldTime = new WorldTimeStatisticsAggregate();
            changed = true;
        }
        changed |= WorldTimeStatisticsReducer.NormalizePersisted(profile.Statistics.WorldTime);
        if (migratingWorldTime)
        {
            profile.Statistics.WorldTime.HistoricalUnavailable = true;
            profile.Statistics.WorldTime.HistoricalProvenance =
                "Historical schema predates M12; prior calendar advancement, observed game time, completed sleep sessions, and sleep-advanced time were not recorded and were not reconstructed.";
            changed = true;
        }
        if (profile.Statistics.Crafting == null)
        {
            profile.Statistics.Crafting = new CraftingStatisticsAggregate();
            changed = true;
        }
        changed |= CraftingStatisticsReducer.NormalizePersisted(profile.Statistics.Crafting);
        if (migratingCrafting)
        {
            profile.Statistics.Crafting.HistoricalUnavailable = true;
            profile.Statistics.Crafting.HistoricalProvenance =
                "Historical schema predates M13; crafted-item completion actions, produced quantities, recipe identity, and batch metadata were not recorded.";
            changed = true;
        }
        if (profile.Statistics.RunTotals.Economy == null)
        {
            profile.Statistics.RunTotals.Economy = new EconomyStatisticsAggregate();
            changed = true;
        }
        changed |= EconomyStatisticsReducer.NormalizePersisted(profile.Statistics.RunTotals.Economy);
        if (migratingEconomy) changed |= MarkHistoricalEconomyUnavailable(profile.Statistics.RunTotals.Economy);
        if (migratingRoutes)
        {
            profile.Statistics.RunTotals.RouteAwareHistoryUnavailable = true;
            profile.Statistics.RunTotals.ItemStatistics.HistoricalUnavailable = true;
            changed = true;
        }
        foreach (var routeMap in profile.Statistics.RunTotals.RouteMaps.Values)
        {
            changed |= NormalizeRouteMap(routeMap);
            if (migratingCombatOwnership)
            {
                changed |= MigrateCombatOwnership(routeMap.CombatStatistics, routeMap.EquipmentStatistics);
            }
            if (migratingEconomy) changed |= MarkHistoricalEconomyUnavailable(routeMap.Economy);
        }

        foreach (var map in profile.Statistics.RunTotals.Maps.Values)
        {
            if (map.Outcomes == null)
            {
                map.Outcomes = new Dictionary<string, long>(StringComparer.Ordinal);
                changed = true;
            }

            if (map.WeaponStatistics == null)
            {
                map.WeaponStatistics = new WeaponStatisticsAggregate();
                changed = true;
            }

            changed |= NormalizeWeaponStatistics(map.WeaponStatistics);

            if (map.CombatStatistics == null)
            {
                map.CombatStatistics = new CombatStatisticsAggregate
                {
                    WasRepairedFromInvalidState = !migratingCombat
                };
                changed = true;
            }

            changed |= NormalizeCombatStatistics(map.CombatStatistics);
            if (migratingCombat) changed |= MarkHistoricalCombatUnavailable(map.CombatStatistics);

            if (map.EquipmentStatistics == null)
            {
                map.EquipmentStatistics = new EquipmentStatisticsAggregate { WasRepairedFromInvalidState = !migratingEquipment };
                changed = true;
            }
            changed |= EquipmentStatisticsReducer.NormalizePersisted(map.EquipmentStatistics);
            if (migratingEquipment) changed |= MarkHistoricalEquipmentUnavailable(map.EquipmentStatistics);
            if (migratingCombatOwnership)
            {
                changed |= MigrateCombatOwnership(map.CombatStatistics, map.EquipmentStatistics);
            }

            if (map.ContainerStatistics == null)
            {
                map.ContainerStatistics = new ContainerStatisticsAggregate
                {
                    WasRepairedFromInvalidState = !migratingContainers
                };
                changed = true;
            }
            changed |= ContainerStatisticsReducer.NormalizePersisted(map.ContainerStatistics);
            if (migratingContainers) changed |= MarkHistoricalContainersUnavailable(map.ContainerStatistics);

            if (map.ItemStatistics == null)
            {
                map.ItemStatistics = new Domain.ItemStatisticsAggregate
                {
                    HistoricalUnavailable = true,
                    WasRepairedFromInvalidState = !migratingRoutes
                };
                changed = true;
            }
            changed |= ItemStatisticsAggregateReducer.NormalizePersisted(map.ItemStatistics);
            if (migratingRoutes)
            {
                map.ItemStatistics.HistoricalUnavailable = true;
                changed = true;
            }
            if (map.Economy == null)
            {
                map.Economy = new EconomyStatisticsAggregate();
                changed = true;
            }
            changed |= EconomyStatisticsReducer.NormalizePersisted(map.Economy);
            if (migratingEconomy) changed |= MarkHistoricalEconomyUnavailable(map.Economy);
        }

        if (profile.Statistics.RunTotals.WeaponStatistics == null)
        {
            profile.Statistics.RunTotals.WeaponStatistics = new WeaponStatisticsAggregate();
            changed = true;
        }

        changed |= NormalizeWeaponStatistics(profile.Statistics.RunTotals.WeaponStatistics);

        if (profile.Statistics.RunTotals.CombatStatistics == null)
        {
            profile.Statistics.RunTotals.CombatStatistics = new CombatStatisticsAggregate
            {
                WasRepairedFromInvalidState = !migratingCombat
            };
            changed = true;
        }

        changed |= NormalizeCombatStatistics(profile.Statistics.RunTotals.CombatStatistics);
        if (missingCurrentCombatRoot)
        {
            profile.Statistics.RunTotals.CombatStatistics.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (migratingCombat) changed |= MarkHistoricalCombatUnavailable(profile.Statistics.RunTotals.CombatStatistics);

        if (profile.Statistics.RunTotals.EquipmentStatistics == null)
        {
            profile.Statistics.RunTotals.EquipmentStatistics = new EquipmentStatisticsAggregate { WasRepairedFromInvalidState = !migratingEquipment };
            changed = true;
        }
        changed |= EquipmentStatisticsReducer.NormalizePersisted(profile.Statistics.RunTotals.EquipmentStatistics);
        if (missingCurrentEquipmentRoot)
        {
            profile.Statistics.RunTotals.EquipmentStatistics.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (migratingEquipment) changed |= MarkHistoricalEquipmentUnavailable(profile.Statistics.RunTotals.EquipmentStatistics);
        if (migratingCombatOwnership)
        {
            changed |= MigrateCombatOwnership(
                profile.Statistics.RunTotals.CombatStatistics,
                profile.Statistics.RunTotals.EquipmentStatistics);
        }

        if (profile.Statistics.RunTotals.ContainerStatistics == null)
        {
            profile.Statistics.RunTotals.ContainerStatistics = new ContainerStatisticsAggregate
            {
                WasRepairedFromInvalidState = !migratingContainers
            };
            changed = true;
        }
        changed |= ContainerStatisticsReducer.NormalizePersisted(profile.Statistics.RunTotals.ContainerStatistics);
        if (missingCurrentContainerRoot)
        {
            profile.Statistics.RunTotals.ContainerStatistics.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (migratingContainers) changed |= MarkHistoricalContainersUnavailable(profile.Statistics.RunTotals.ContainerStatistics);

        foreach (var run in profile.Statistics.Runs)
        {
            if (run.SchemaVersion < 11)
            {
                run.SchemaVersion = 11;
                changed = true;
            }
            if (run.WeaponStatistics == null)
            {
                run.WeaponStatistics = new WeaponStatisticsAggregate();
                changed = true;
            }

            changed |= NormalizeWeaponStatistics(run.WeaponStatistics);

            if (run.CombatStatistics == null)
            {
                run.CombatStatistics = new CombatStatisticsAggregate
                {
                    WasRepairedFromInvalidState = !migratingCombat
                };
                changed = true;
            }

            changed |= NormalizeCombatStatistics(run.CombatStatistics);
            if (migratingCombat) changed |= MarkHistoricalCombatUnavailable(run.CombatStatistics);

            if (run.EquipmentStatistics == null)
            {
                run.EquipmentStatistics = new EquipmentStatisticsAggregate { WasRepairedFromInvalidState = !migratingEquipment };
                changed = true;
            }
            changed |= EquipmentStatisticsReducer.NormalizePersisted(run.EquipmentStatistics);
            if (migratingEquipment) changed |= MarkHistoricalEquipmentUnavailable(run.EquipmentStatistics);
            if (migratingCombatOwnership)
            {
                changed |= MigrateCombatOwnership(run.CombatStatistics, run.EquipmentStatistics);
            }

            if (run.ContainerStatistics == null)
            {
                run.ContainerStatistics = new ContainerStatisticsAggregate
                {
                    WasRepairedFromInvalidState = !migratingContainers
                };
                changed = true;
            }
            changed |= ContainerStatisticsReducer.NormalizePersisted(run.ContainerStatistics);
            if (migratingContainers) changed |= MarkHistoricalContainersUnavailable(run.ContainerStatistics);

            if (string.IsNullOrWhiteSpace(run.StartingMapId)
                || (string.Equals(run.StartingMapId, Domain.MapIdentity.UnknownId, StringComparison.Ordinal)
                    && !string.Equals(run.MapId, Domain.MapIdentity.UnknownId, StringComparison.Ordinal)))
            {
                run.StartingMapId = run.MapId;
                run.StartingMapDisplayName = run.MapDisplayName;
                run.StartingMapKnown = run.MapKnown;
                changed = true;
            }
            run.Segments ??= new List<Domain.MapSegmentSummary>();
            run.SegmentEventAssociations ??= new List<Domain.SegmentEventAssociation>();
            run.RouteCapabilities ??= RouteStatisticsReducer.Unavailable("Route capability record was missing from persisted data.");
            if (migratingLosslessRouteAssociation)
            {
                var legacySaturationIncomplete = run.SegmentEventAssociations.Count
                                                 == RouteStatisticsReducer.LegacyMaximumRawEventAssociationsPerRun
                                                 && run.RouteCapabilities.EventAttribution?.State
                                                 != Domain.AdapterCapabilityState.Supported;
                changed |= RouteStatisticsReducer.MigrateLegacyAssociations(run.SegmentEventAssociations);
                RouteStatisticsReducer.MigrateLegacyCaptureCapability(run.RouteCapabilities, legacySaturationIncomplete);
                if (legacySaturationIncomplete)
                {
                    run.HistoricalEventAttributionIncomplete = true;
                    run.HistoricalEventAttributionProvenance =
                        "Schema-9 reached the 2,048-row association ceiling; retained rows are exact, later historical associations may be missing, and schema-10 current capture is available.";
                }
                else
                {
                    run.HistoricalEventAttributionProvenance ??= string.Empty;
                }
                changed = true;
            }
            var routeCapabilityRepaired = RouteStatisticsReducer.NormalizeCapabilities(run.RouteCapabilities);
            run.RouteWasRepairedFromInvalidState |= routeCapabilityRepaired;
            changed |= routeCapabilityRepaired;
            run.ItemStatistics ??= new Domain.ItemStatisticsAggregate();
            changed |= ItemStatisticsAggregateReducer.NormalizePersisted(run.ItemStatistics);
            if (migratingRoutes)
            {
                run.EndingMapId = Domain.MapIdentity.UnknownId;
                run.EndingMapDisplayName = Domain.MapIdentity.UnknownDisplayName;
                run.EndingMapKnown = false;
                run.RouteSignature = string.Empty;
                run.Segments.Clear();
                run.SegmentEventAssociations.Clear();
                run.TransitionExcludedDistance = 0;
                run.RouteCapabilities = RouteStatisticsReducer.Unavailable(
                    "Historical schema predates M8; ending map, ordered route, segments, transition displacement, and event attribution were not recorded.");
                run.HistoricalRouteUnavailable = true;
                run.ItemStatistics.HistoricalUnavailable = true;
                changed = true;
            }
            else
            {
                changed |= NormalizeCurrentRunRoute(run);
            }
            if (run.Economy == null)
            {
                run.Economy = new EconomyStatisticsAggregate();
                changed = true;
            }
            changed |= EconomyStatisticsReducer.NormalizePersisted(run.Economy);
            if (migratingEconomy) changed |= MarkHistoricalEconomyUnavailable(run.Economy);
            foreach (var segment in run.Segments)
            {
                if (migratingCombatOwnership)
                {
                    changed |= MigrateCombatOwnership(segment.CombatStatistics, segment.EquipmentStatistics);
                }
                segment.Economy ??= new EconomyStatisticsAggregate();
                changed |= EconomyStatisticsReducer.NormalizePersisted(segment.Economy);
                if (migratingEconomy) changed |= MarkHistoricalEconomyUnavailable(segment.Economy);
            }
        }

        if (migratingM14Associations)
        {
            foreach (var scope in M14RecoveryScopes(profile))
            {
                changed |= MarkHistoricalM14Unavailable(scope.WeaponStatistics, scope.EquipmentStatistics);
            }
            foreach (var run in profile.Statistics.Runs)
            {
                if (run.SchemaVersion < 14)
                {
                    run.SchemaVersion = 14;
                    changed = true;
                }
            }
        }

        if (profile.Statistics.RunRecords == null)
        {
            profile.Statistics.RunRecords = new RunDurationRecords();
            changed = true;
        }

        if (profile.Statistics.RunRecords.Extraction == null)
        {
            profile.Statistics.RunRecords.Extraction = new DurationRecordPair();
            changed = true;
        }

        if (profile.Statistics.RunRecords.Death == null)
        {
            profile.Statistics.RunRecords.Death = new DurationRecordPair();
            changed = true;
        }

        if (profile.Statistics.RunRecords.Maps == null)
        {
            profile.Statistics.RunRecords.Maps = new Dictionary<string, MapRunDurationRecords>(StringComparer.Ordinal);
            changed = true;
        }

        foreach (var map in profile.Statistics.RunRecords.Maps.Values)
        {
            if (map.Extraction == null)
            {
                map.Extraction = new DurationRecordPair();
                changed = true;
            }

            if (map.Death == null)
            {
                map.Death = new DurationRecordPair();
                changed = true;
            }
        }

        if (profile.SchemaVersion < 2)
        {
            profile.SchemaVersion = 2;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 2)
        {
            profile.Statistics.SchemaVersion = 2;
            changed = true;
        }

        if (profile.SchemaVersion < 3)
        {
            profile.SchemaVersion = 3;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 3)
        {
            profile.Statistics.SchemaVersion = 3;
            changed = true;
        }

        if (profile.SchemaVersion < 4)
        {
            profile.SchemaVersion = 4;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 4)
        {
            profile.Statistics.SchemaVersion = 4;
            changed = true;
        }

        if (profile.SchemaVersion < 5)
        {
            profile.SchemaVersion = 5;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 5)
        {
            profile.Statistics.SchemaVersion = 5;
            changed = true;
        }

        if (profile.SchemaVersion < 6)
        {
            profile.SchemaVersion = 6;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 6)
        {
            profile.Statistics.SchemaVersion = 6;
            changed = true;
        }

        if (profile.SchemaVersion < 7)
        {
            profile.SchemaVersion = 7;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 7)
        {
            profile.Statistics.SchemaVersion = 7;
            changed = true;
        }

        if (profile.SchemaVersion < 8)
        {
            profile.SchemaVersion = 8;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 8)
        {
            profile.Statistics.SchemaVersion = 8;
            changed = true;
        }

        if (profile.SchemaVersion < 9)
        {
            profile.SchemaVersion = 9;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 9)
        {
            profile.Statistics.SchemaVersion = 9;
            changed = true;
        }

        if (profile.SchemaVersion < 10)
        {
            profile.SchemaVersion = 10;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 10)
        {
            profile.Statistics.SchemaVersion = 10;
            changed = true;
        }

        if (profile.SchemaVersion < 11)
        {
            profile.SchemaVersion = 11;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 11)
        {
            profile.Statistics.SchemaVersion = 11;
            changed = true;
        }

        if (profile.SchemaVersion < 12)
        {
            profile.SchemaVersion = 12;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 12)
        {
            profile.Statistics.SchemaVersion = 12;
            changed = true;
        }

        if (profile.SchemaVersion < 13)
        {
            profile.SchemaVersion = 13;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 13)
        {
            profile.Statistics.SchemaVersion = 13;
            changed = true;
        }

        if (profile.SchemaVersion < 14)
        {
            profile.SchemaVersion = 14;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 14)
        {
            profile.Statistics.SchemaVersion = 14;
            changed = true;
        }

        if (!string.Equals(profile.Statistics.SaveGenerationId, profile.GenerationId, StringComparison.Ordinal))
        {
            profile.Statistics.SaveGenerationId = profile.GenerationId;
            changed = true;
        }

        // Repair schema-2 pre-release profiles written before delayed healing
        // buffs were promoted to the canonical Healing group.
        changed |= ProfileGroupReconciler.PromoteProvenHealingItems(profile.Statistics);

        return changed;
    }

    private static bool NormalizeWeaponStatistics(WeaponStatisticsAggregate statistics)
        => WeaponStatisticsReducer.NormalizePersisted(statistics).Changed;

    private static bool NormalizeCombatStatistics(CombatStatisticsAggregate statistics)
        => CombatStatisticsReducer.NormalizePersisted(statistics).Changed;

    internal static bool MigrateCombatOwnership(
        CombatStatisticsAggregate combat,
        EquipmentStatisticsAggregate equipment)
    {
        const string provenance = "Historical schema predates M11; proven player final blows were retained where ownership evidence permits, while ambiguous death classification and equipment kill credit remain explicitly legacy.";
        var changed = CombatStatisticsReducer.MigrateLegacyOwnershipSemantics(combat, provenance);
        changed |= EquipmentStatisticsReducer.MigrateLegacyCombatOwnership(equipment, provenance);
        return changed;
    }

    internal static bool MarkHistoricalM14Unavailable(
        WeaponStatisticsAggregate weapon,
        EquipmentStatisticsAggregate equipment)
    {
        const string pairingProvenance = "Historical schema predates M14; event-time weapon-ammunition pairs were not recorded and cannot be reconstructed from marginal totals.";
        const string characterSlotProvenance = "Historical schema predates M14; the character-slot member catalog and proven-empty root-slot durations were not recorded.";
        const string nestedSlotProvenance = "Historical schema predates M14; named occupied-child and proven-empty nested-slot durations were not recorded; existing exact item-tree signatures remain intact.";
        var supportedWeapon = WeaponNativeContractPolicy.CreateMetricCapabilities();
        var supportedEquipment = EquipmentNativeContractPolicy.CreateSupportedCapabilities();
        weapon.HistoricalPairingUnavailable = true;
        weapon.HistoricalPairingProvenance = pairingProvenance;
        weapon.Capabilities.WeaponAmmunitionPairing = supportedWeapon.WeaponAmmunitionPairing;
        equipment.HistoricalCharacterSlotStateUnavailable = true;
        equipment.HistoricalCharacterSlotStateProvenance = characterSlotProvenance;
        equipment.HistoricalNestedSlotStateUnavailable = true;
        equipment.HistoricalNestedSlotStateProvenance = nestedSlotProvenance;
        equipment.Capabilities.CharacterSlotState = supportedEquipment.CharacterSlotState;
        equipment.Capabilities.NestedSlotState = supportedEquipment.NestedSlotState;
        return true;
    }

    private static bool MarkHistoricalCombatUnavailable(CombatStatisticsAggregate statistics)
    {
        const string provenance = "Historical schema predates M5; combat attribution was not recorded.";
        statistics.Capabilities = CombatNativeContractPolicy.CreateUnavailableCapabilities(provenance);
        return true;
    }

    private static bool MarkHistoricalEquipmentUnavailable(EquipmentStatisticsAggregate statistics)
    {
        const string provenance = "Historical schema predates M6; equipment and totem state was not recorded.";
        statistics.Capabilities = EquipmentNativeContractPolicy.CreateUnavailableCapabilities(provenance);
        statistics.HistoricalUnavailable = true;
        return true;
    }

    private static bool MarkHistoricalContainersUnavailable(ContainerStatisticsAggregate statistics)
    {
        const string provenance = "Historical schema predates M7; successful unique-container access was not recorded.";
        statistics.Capabilities = ContainerNativeContractPolicy.Unavailable(provenance);
        statistics.HistoricalUnavailable = true;
        return true;
    }

    private static bool MarkHistoricalEconomyUnavailable(EconomyStatisticsAggregate statistics)
    {
        const string provenance = "Historical schema predates M9; economy flows and physical-Cash raid outcomes were not recorded.";
        statistics.HistoricalUnavailable = true;
        statistics.Capabilities = new Domain.EconomyMetricCapabilities
        {
            MoneyAmountDirection = Unavailable(provenance),
            MoneySourceAttribution = Unavailable(provenance),
            MoneyContextAttribution = Unavailable(provenance),
            CashAmountDirection = Unavailable(provenance),
            CashExternalAcquisition = Unavailable(provenance),
            CashContextAttribution = Unavailable(provenance),
            CashTerminalOutcomes = Unavailable(provenance),
            RouteAttribution = Unavailable(provenance)
        };
        return true;
    }

    private static Domain.MetricAvailability Unavailable(string provenance) => new()
    {
        State = Domain.AdapterCapabilityState.DisabledIncompatible,
        Provenance = provenance
    };

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
                if (!run.HistoricalRouteUnavailable
                    && !string.Equals(run.StartingMapId, run.Segments[0].MapId, StringComparison.Ordinal))
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

        if (run.SegmentEventAssociations.Count > RouteStatisticsReducer.MaximumPersistedEventAssociationsPerRun)
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
