using System.Globalization;
using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Encounters;

internal static class EncounterDamageText
{
    internal static IReadOnlyList<(string Text, int? Icon)> Parts(EncounterDamage damage, Func<int?, string> itemName, Func<string, string> text)
    {
        var parts = new List<(string Text, int? Icon)> { (itemName(damage.Source.WeaponTypeId), damage.Source.WeaponTypeId) };
        var mechanism = damage.Source.Mechanism;
        if (mechanism is "effect" or "unscoped-effect")
        {
            parts.Add((text("ui.encounters_effect_damage"), null));
            return parts;
        }
        if (mechanism == "melee") parts.Add((text("ui.encounters_melee_damage"), null));
        else parts.Add((itemName(damage.Source.AmmunitionTypeId), damage.Source.AmmunitionTypeId));
        var hits = string.Format(CultureInfo.CurrentCulture, text(damage.Hits == 1 ? "ui.encounters_hit_one" : "ui.encounters_hit_count"), damage.Hits);
        // Native classification covers the player's outgoing projectiles only. Incoming
        // zero is an unpopulated counter, not evidence that the enemy missed the head.
        if (!damage.Incoming && mechanism == "projectile")
        {
            var headshots = string.Format(CultureInfo.CurrentCulture,
                text(damage.Headshots == 1 ? "ui.encounters_headshot_one" : "ui.encounters_headshot_count"), damage.Headshots);
            hits += " (" + headshots + ")";
        }
        parts.Add((hits, null));
        return parts;
    }
}
