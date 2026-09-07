using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

internal static class RunDataSchema
{
    internal static IEnumerable<(CombatStatisticsAggregate Combat, EquipmentStatisticsAggregate Equipment)> Scopes(ProfileStatistics profile)
    {
        yield return (profile.RunTotals.CombatStatistics, profile.RunTotals.EquipmentStatistics);
        foreach (var map in profile.RunTotals.Maps.Values) yield return (map.CombatStatistics, map.EquipmentStatistics);
        foreach (var map in profile.RunTotals.RouteMaps.Values) yield return (map.CombatStatistics, map.EquipmentStatistics);
        foreach (var run in profile.Runs)
        {
            yield return (run.CombatStatistics, run.EquipmentStatistics);
            foreach (var segment in run.Segments) yield return (segment.CombatStatistics, segment.EquipmentStatistics);
        }
    }

    internal static void Migrate(CombatStatisticsAggregate combat, EquipmentStatisticsAggregate equipment)
    {
        foreach (var totals in CombatStatisticsReducer.PlayerKillScopes(combat))
            totals.PlayerKills = PlayerKillPartition.Historical(totals.KillsByYou);
        foreach (var row in equipment.CombatAssociations.Values)
            row.PlayerKills = PlayerKillPartition.Historical(row.KillsByYou);
    }

    internal static void Validate(CombatStatisticsAggregate combat, EquipmentStatisticsAggregate equipment)
    {
        if (combat == null || equipment?.CombatAssociations == null)
            throw new ArgumentException("Combat or equipment root is missing.");
        CombatStatisticsReducer.ValidateAggregate(combat);
        CombatStatisticsReducer.ValidatePlayerKills(combat);
        foreach (var row in equipment.CombatAssociations.Values)
        {
            if (row?.PlayerKills == null) throw new ArgumentException("Equipment player-kill partition is missing.");
            row.PlayerKills.Validate(row.KillsByYou);
        }
    }

    internal static void Validate(ProfileStatistics profile)
    {
        foreach (var scope in Scopes(profile)) Validate(scope.Combat, scope.Equipment);
        foreach (var run in profile.Runs)
        {
            if (run.TerminalLoadout == null) throw new ArgumentException("Run terminal loadout state is missing.");
            run.TerminalLoadout.Validate(run.Outcome);
        }
    }

    internal static void Validate(ActiveRunCheckpoint checkpoint)
    {
        Validate(checkpoint.CombatStatistics, checkpoint.EquipmentStatistics);
        if (checkpoint.Segments == null) throw new ArgumentException("Checkpoint segments are missing.");
        foreach (var segment in checkpoint.Segments)
        {
            if (segment == null) throw new ArgumentException("Checkpoint segment is missing.");
            Validate(segment.CombatStatistics, segment.EquipmentStatistics);
        }
        if (checkpoint.TerminalLoadout == null) throw new ArgumentException("Checkpoint terminal loadout state is missing.");
        checkpoint.TerminalLoadout.Validate(checkpoint.PendingTerminalOutcome);
    }

    internal static void Migrate(ActiveRunCheckpoint checkpoint)
    {
        Migrate(checkpoint.CombatStatistics, checkpoint.EquipmentStatistics);
        foreach (var segment in checkpoint.Segments) Migrate(segment.CombatStatistics, segment.EquipmentStatistics);
        checkpoint.TerminalLoadout = TerminalLoadout.Historical();
    }
}
