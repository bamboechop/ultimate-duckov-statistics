namespace UltimateDuckovStatistics.Adapters;

[Flags]
internal enum EncounterCombatHookLoss
{
    None = 0,
    Health = 1,
    ProjectileInit = 2,
    ProjectileUpdate = 4,
    ProjectileRelease = 8,
    Melee = 16,
    Effect = 32,
    BuffOwnership = 64,
    Environmental = 128,
    GrenadeOwnership = 256,
    HeadshotEvidence = 512,
    ProjectileSource = ProjectileInit | ProjectileUpdate,
    All = Health | ProjectileSource | ProjectileRelease | Melee | Effect | BuffOwnership | Environmental | GrenadeOwnership | HeadshotEvidence
}
