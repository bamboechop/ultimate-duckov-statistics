using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

/// <summary>Partition of proven player kills, classified only by the same event's attack kind.</summary>
[DataContract]
public sealed class PlayerKillPartition
{
    [DataMember(Order = 1)] public long Ranged { get; set; }
    [DataMember(Order = 2)] public long Melee { get; set; }
    [DataMember(Order = 3)] public long Effect { get; set; }
    [DataMember(Order = 4)] public long Environmental { get; set; }
    [DataMember(Order = 5)] public long Unknown { get; set; }
    [DataMember(Order = 6)] public long HistoricalUnclassified { get; set; }
    [DataMember(Order = 7)] public bool HistoricalIncomplete { get; set; }

    [DataMember(Order = 8)] public string EvidenceSource { get; set; } = "CombatRecorded.AttackKind on the same proven KillsByYou event; HistoricalUnclassified has no attack-kind evidence.";

    public bool ClassificationComplete => !HistoricalIncomplete && Unknown == 0;
    public string Provenance => HistoricalIncomplete
        ? "Pre-schema-17 attack-kind history is unavailable; retained new buckets do not backfill it."
        : Unknown > 0 ? "Same-event attack kind includes unknown player kills; ranged/melee split is incomplete."
        : "Every proven player kill is partitioned by its same-event native attack kind.";

    public PlayerKillPartition Clone() => (PlayerKillPartition)MemberwiseClone();

    public static PlayerKillPartition Historical(long total) => new()
    {
        HistoricalUnclassified = total,
        HistoricalIncomplete = true
    };

    public static PlayerKillPartition FromEvent(CombatRecorded value)
    {
        var result = new PlayerKillPartition();
        switch (value.AttackKind)
        {
            case CombatAttackKind.Ranged: result.Ranged = value.KillsByYou; break;
            case CombatAttackKind.Melee: result.Melee = value.KillsByYou; break;
            case CombatAttackKind.Effect: result.Effect = value.KillsByYou; break;
            case CombatAttackKind.Environmental: result.Environmental = value.KillsByYou; break;
            default: result.Unknown = value.KillsByYou; break;
        }
        return result;
    }

    public void Validate(long total)
    {
        if (string.IsNullOrWhiteSpace(EvidenceSource) || Ranged < 0 || Melee < 0 || Effect < 0 || Environmental < 0 || Unknown < 0
            || HistoricalUnclassified < 0 || HistoricalUnclassified > 0 && !HistoricalIncomplete)
            throw new ArgumentException("Player-kill partition contains invalid counters or history.");
        try
        {
            if (checked(Ranged + Melee + Effect + Environmental + Unknown + HistoricalUnclassified) != total)
                throw new ArgumentException("Player-kill partition does not reconcile to KillsByYou.");
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("Player-kill partition reconciliation overflowed.", exception);
        }
    }

    public static PlayerKillPartition Merge(PlayerKillPartition left, PlayerKillPartition right) => new()
    {
        Ranged = checked(left.Ranged + right.Ranged),
        Melee = checked(left.Melee + right.Melee),
        Effect = checked(left.Effect + right.Effect),
        Environmental = checked(left.Environmental + right.Environmental),
        Unknown = checked(left.Unknown + right.Unknown),
        HistoricalUnclassified = checked(left.HistoricalUnclassified + right.HistoricalUnclassified),
        HistoricalIncomplete = left.HistoricalIncomplete || right.HistoricalIncomplete
    };
}
