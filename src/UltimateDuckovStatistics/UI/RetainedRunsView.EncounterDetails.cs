using System.Globalization;
using TMPro;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private sealed partial class RunsView
    {
        private sealed class EncounterFeedRow
        {
            internal EncounterButton Control = null!;
            internal RectTransform Badge = null!;
            internal TextMeshProUGUI Number = null!, Time = null!;
        }
        private sealed class EncounterSourceToken
        {
            internal RectTransform Root = null!;
            internal Image Icon = null!;
            internal TextMeshProUGUI Label = null!;
        }
        private float[] eventHeaderHeights = Array.Empty<float>(), eventTimeWidths = Array.Empty<float>();
        private string[] eventTitles = Array.Empty<string>(), eventTimes = Array.Empty<string>();
        private readonly List<TextMeshProUGUI> encounterDetailLabels = new();
        private readonly List<EncounterSourceToken> encounterSourceTokens = new();
        private int usedDetailLabels, usedSourceTokens;

        private EncounterFeedRow CreateEncounterFeedRow()
        {
            var control = EncounterControl(encounterList.Content, "EncounterRow" + eventRows.Count, 20,
                button => SelectEncounter(button.Index));
            var badge = Panel(control.Root, "EncounterNumberBadge", 16);
            badge.GetComponent<ProceduralImage>().color = EncounterBlue;
            badge.GetComponent<ProceduralImage>().raycastTarget = false;
            var number = Text(badge, "Number", 20); number.alignment = TextAlignmentOptions.Center;
            var time = Text(control.Root, "ElapsedTime", 16); time.alignment = TextAlignmentOptions.MidlineRight;
            time.color = EncounterMuted; time.enableWordWrapping = false;
            return new EncounterFeedRow { Control = control, Badge = badge, Number = number, Time = time };
        }

        private EncounterMarker CreateLocationPin(string name, Color tint)
        {
            var rect = Node(mapPane, name);
            var graphic = rect.gameObject.AddComponent<EncounterPinGraphic>(); graphic.color = tint;
            graphic.raycastTarget = true;
            rect.gameObject.AddComponent<CombatTooltipTrigger>();
            var label = Text(rect, "Label", 18); label.alignment = TextAlignmentOptions.Center;
            return new EncounterMarker { Root = rect, Label = label };
        }

        private float DetailText(string value, float size, Color tint, float y, float width)
        {
            if (usedDetailLabels == encounterDetailLabels.Count)
                encounterDetailLabels.Add(Text(encounterBody, "DetailText" + usedDetailLabels, size));
            var label = encounterDetailLabels[usedDetailLabels++]; label.gameObject.SetActive(true);
            label.fontSize = size; label.color = tint; label.text = value;
            return Put(label, 0, y, width);
        }

        private float LayoutSource(EncounterDamage damage, bool incoming, bool showAmount, float y, float width)
        {
            var parts = new List<(string Text, int? Icon)> { (UiText.Get(incoming ? "ui.encounters_from" : "ui.encounters_with"), null) };
            parts.AddRange(EncounterDamageText.Parts(damage, ItemName, UiText.Get));
            if (showAmount) parts.Add((string.Format(CultureInfo.CurrentCulture, UiText.Get("ui.encounters_damage_amount"), Number(damage.Amount)), null));
            const float imageSize = 60, sourceHeight = imageSize;
            var face = typography.Font.faceInfo;
            var baseline = sourceHeight / 2 + (face.ascentLine + face.descentLine) * 18 / face.pointSize / 2;
            var start = y; var x = 0f; var lineHeight = sourceHeight;
            foreach (var part in parts)
            {
                if (usedSourceTokens == encounterSourceTokens.Count)
                {
                    var root = Panel(encounterBody, "SourceToken" + usedSourceTokens, 0);
                    root.GetComponent<ProceduralImage>().color = Color.clear;
                    root.GetComponent<ProceduralImage>().raycastTarget = false;
                    var icon = Node(root, "Icon").gameObject.AddComponent<Image>(); icon.preserveAspect = true; icon.raycastTarget = false;
                    var label = Text(root, "Source", 18); label.color = EncounterMuted; label.alignment = TextAlignmentOptions.BaselineLeft;
                    encounterSourceTokens.Add(new EncounterSourceToken { Root = root, Icon = icon, Label = label });
                }
                var token = encounterSourceTokens[usedSourceTokens++]; token.Root.gameObject.SetActive(true);
                token.Label.text = part.Text;
                token.Icon.sprite = part.Icon.HasValue ? icons.Resolve("duckov:item:" + part.Icon.Value) : null;
                token.Icon.gameObject.SetActive(token.Icon.sprite != null);
                var iconWidth = token.Icon.sprite == null ? 0 : imageSize + 6;
                var w = Math.Min(width, token.Label.GetPreferredValues().x + iconWidth);
                var textHeight = token.Label.GetPreferredValues(part.Text, Math.Max(1, w - iconWidth), float.PositiveInfinity).y;
                var h = Math.Max(sourceHeight, baseline + textHeight);
                if (x > 0 && x + w > width) { x = 0; y += lineHeight + 3; lineHeight = sourceHeight; }
                Place(token.Root, x, y, w, h); Place(token.Icon.rectTransform, 0, 0, imageSize, imageSize);
                Place(token.Label.rectTransform, iconWidth, baseline - textHeight / 2, Math.Max(1, w - iconWidth), textHeight);
                lineHeight = Math.Max(lineHeight, h); x += w + 10;
            }
            return y - start + lineHeight;
        }

        private float LayoutEncounterBody()
        {
            usedDetailLabels = usedSourceTokens = 0;
            foreach (var label in encounterDetailLabels) label.gameObject.SetActive(false);
            foreach (var token in encounterSourceTokens) token.Root.gameObject.SetActive(false);
            foreach (var tile in lootTiles) tile.Root.gameObject.SetActive(false);
            var record = CurrentEncounter; encounterBody.gameObject.SetActive(record != null);
            if (record == null) return 0;
            var width = listWidth - 40;
            var records = encounterSelection.Run.Records;
            var related = new HashSet<string>(records.Where(row => row.Encounter?.ActorId == record.Encounter!.ActorId).Select(row => row.Id));
            var owned = records.Where(row => row.EncounterId != null && related.Contains(row.EncounterId)).ToArray();
            var y = 0f;
            foreach (var incoming in new[] { false, true })
            {
                var rows = owned.Where(row => row.Damage?.Incoming == incoming).Select(row => row.Damage!).ToArray();
                var heading = rows.Length == 0 ? UiText.Get(incoming ? "ui.encounters_received" : "ui.encounters_dealt") + ": " + UiText.Get("ui.encounters_no_damage")
                    : string.Format(CultureInfo.CurrentCulture, UiText.Get(incoming ? "ui.encounters_received_amount" : "ui.encounters_dealt_amount"), Number(rows.Sum(row => row.Amount)));
                y += DetailText(heading, 22, Color.white, y, width) + 8;
                foreach (var row in rows) y += LayoutSource(row, incoming, rows.Length > 1, y, width) + 5;
                y += 24;
            }
            if (record.Encounter!.PlayerPosition == null || record.Encounter.EnemyPosition == null)
                y += DetailText(UiText.Get("ui.encounters_position_unavailable"), 18, EncounterMuted, y, width) + 16;
            y += DetailText(UiText.Get("ui.encounters_inventory"), 22, Color.white, y, width) + 4;
            var observed = owned.Where(row => row.Inventory != null).SelectMany(row => row.Inventory!.Slots)
                .Where(slot => slot.Inspected && slot.ItemTypeId.HasValue).Select(slot => slot.ItemTypeId!.Value)
                .Union(owned.Where(row => row.Loot != null).Select(row => row.Loot!.ItemTypeId)).ToArray();
            y += DetailText(UiText.Get(observed.Length == 0 ? "ui.encounters_uninspected" : "ui.encounters_loot_legend"), 16, EncounterMuted, y, width) + 10;
            const float tileSize = 60, tileGap = 8;
            var columns = Math.Max(1, (int)((width + tileGap) / (tileSize + tileGap)));
            while (lootTiles.Count < observed.Length)
            {
                var tile = Panel(encounterBody, "ObservedItem" + lootTiles.Count, 10);
                var image = Node(tile, "Icon").gameObject.AddComponent<Image>(); image.raycastTarget = false; image.preserveAspect = true;
                var border = Node(tile, "Border"); Stretch(border);
                var outline = border.gameObject.AddComponent<ProceduralImage>(); outline.color = new Color(1, 1, 1, .8f);
                outline.BorderWidth = 1.5f; outline.FalloffDistance = 1; outline.raycastTarget = false;
                border.gameObject.AddComponent<UniformModifier>().Radius = 10;
                var caption = Text(tile, "Quantity", 16); caption.alignment = TextAlignmentOptions.BottomRight;
                lootTiles.Add((tile, image, caption));
                tile.GetComponent<ProceduralImage>().raycastTarget = true;
                tile.gameObject.AddComponent<CombatTooltipTrigger>();
            }
            for (var i = 0; i < observed.Length; i++)
            {
                var type = observed[i]; var tile = lootTiles[i]; tile.Root.gameObject.SetActive(true);
                var quantity = EncounterLootPresentation.FirstObservedQuantity(owned, type);
                var transfers = owned.Where(row => row.Loot?.ItemTypeId == type).Select(row => row.Loot!).ToArray();
                var taken = transfers.Sum(value => value.TakenToPlayer + value.TakenToPet);
                var returned = transfers.Sum(value => value.ReturnedByPlayer + value.ReturnedByPet);
                tile.Root.GetComponent<ProceduralImage>().color = taken > returned ? new Color32(126, 82, 39, 235) : new Color(0, 0, 0, .5f);
                tile.Image.sprite = icons.Resolve("duckov:item:" + type); tile.Image.enabled = tile.Image.sprite != null;
                tile.Label.text = (tile.Image.sprite == null ? ItemName(type) + "\n" : "") + (quantity > 1 ? quantity.Value.ToString(CultureInfo.CurrentCulture) : "");
                tile.Root.GetComponent<CombatTooltipTrigger>().Bind(encounterTooltip, ItemName(type));
                Place(tile.Root, i % columns * (tileSize + tileGap), y + i / columns * (tileSize + tileGap), tileSize, tileSize);
                Place(tile.Image.rectTransform, 5, 5, tileSize - 10, tileSize - 10); Place(tile.Label.rectTransform, 4, 4, tileSize - 8, tileSize - 8);
            }
            return y + (observed.Length + columns - 1) / columns * (tileSize + tileGap);
        }
    }
}
