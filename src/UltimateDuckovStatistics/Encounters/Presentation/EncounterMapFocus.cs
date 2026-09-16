using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Encounters;

// Selection preserves each known endpoint; only a pair proves a connector/distance.
internal readonly struct EncounterMapFocus
{
    private EncounterMapFocus(bool playerAvailable, bool enemyAvailable, EncounterMapPoint player,
        EncounterMapPoint enemy, EncounterMapFrame frame)
    { PlayerAvailable = playerAvailable; EnemyAvailable = enemyAvailable; Player = player; Enemy = enemy; Frame = frame; }

    public bool PlayerAvailable { get; }
    public bool EnemyAvailable { get; }
    public bool ConnectorAvailable => PlayerAvailable && EnemyAvailable;
    public EncounterMapPoint Player { get; }
    public EncounterMapPoint Enemy { get; }
    public EncounterMapFrame Frame { get; }

    public static EncounterMapFocus Create(EncounterDetail? encounter, StoredEncounterMap map, double width, double height)
    {
        var hasPlayer = TryProject(encounter?.PlayerPosition, map, out var player);
        var hasEnemy = TryProject(encounter?.EnemyPosition, map, out var enemy);
        var frame = hasPlayer || hasEnemy
            ? EncounterMapGeometry.Focus(hasPlayer ? player : enemy, hasEnemy ? enemy : player, width, height)
            : EncounterMapGeometry.Overview(width, height);
        return new EncounterMapFocus(hasPlayer, hasEnemy, player, enemy, frame);
    }

    public static bool TryProjectOverview(EncounterDetail encounter, StoredEncounterMap map, out EncounterMapPoint point) =>
        // The marker denotes where the death occurred, never the surviving endpoint.
        TryProject(encounter.Outcome == EncounterOutcome.PlayerDeath ? encounter.PlayerPosition : encounter.EnemyPosition, map, out point);

    public static bool TryProject(EncounterPosition? position, StoredEncounterMap map, out EncounterMapPoint point)
    {
        point = default;
        return position != null && map.Calibration != null && (string.IsNullOrEmpty(position.MapId)
            || EncounterPathMath.NativeSceneId(position.MapId!) == EncounterPathMath.NativeSceneId(map.MapId))
            && EncounterMapGeometry.TryProject(map.Calibration, position.X, position.Z, out point);
    }
}
