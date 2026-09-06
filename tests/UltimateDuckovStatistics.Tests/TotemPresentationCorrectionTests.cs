using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class TotemPresentationCorrectionTests
{
    private static EquipmentDocument Document(params EquipmentEntry[] slots)
    {
        var empty = Array.Empty<EquipmentEntry>();
        var direct = new EquipmentEntry("direct:totem", "Totem", "duckov:totem:966", 15, "total time equipped",
            groups: new[] { new EquipmentGroup("Equipped time by slot", slots) });
        var snapshot = new EquipmentPresentation("g", null, empty, empty, empty, Array.Empty<EquipmentGroup>(),
            new[] { direct }, empty, empty, empty,
            new Dictionary<string, string> { ["direct"] = "", ["empty"] = "", ["sets"] = "", ["tote"] = "" });
        var selection = new EquipmentSelection(); selection.Refresh(snapshot); selection.SelectPage(EquipmentPanelSection.Totems);
        selection.Toggle("g", direct.Id);
        var document = new EquipmentDocument((value, width, size) => Math.Max(size, (float)Math.Ceiling(value.Length * size / Math.Max(1, width)) * size));
        document.Page(selection, false, 900);
        return document;
    }

    [Fact]
    public void ActiveTotemUsesCompactSlotLineAndSecondLineTotal()
    {
        var doc = Document(new EquipmentEntry("slot2-active", "Totem slot 2", "", 15, "Proven active"));
        var header = Assert.Single(doc.Rows, row => row.Actionable);
        Assert.Empty(header.Value); Assert.Equal("00:15.000 total time equipped", header.Caption);
        var slot = Assert.Single(doc.Rows, row => row.Kind == EquipmentRowKind.SlotDuration);
        Assert.Equal("Totem slot 2: 00:15.000", slot.Name); Assert.False(slot.Actionable);
        Assert.DoesNotContain(doc.Rows, row => row.Name == "Equipped time by slot");
        var surface = Assert.Single(doc.Surfaces);
        Assert.Equal(header.Y, surface.Y); Assert.True(surface.Y + surface.Height >= slot.Y + slot.Height);
    }

    [Theory]
    [InlineData("Proven inactive")]
    [InlineData("Activation unknown")]
    public void NonActivePresenceKeepsItsQualification(string state)
    {
        var doc = Document(new EquipmentEntry("slot2-state", "Totem slot 2", "", 15, state));
        Assert.Equal("Totem slot 2: 00:15.000 · " + state, Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.SlotDuration).Name);
    }

    [Fact]
    public void MultipleActivationIntervalsStayDistinctWithoutParsingIdentity()
    {
        var doc = Document(new EquipmentEntry("opaqueA", "Totem slot 2", "", 10, "Proven active"),
            new EquipmentEntry("opaqueB", "Totem slot 2", "", 5, "Proven inactive"));
        var slots = doc.Rows.Where(r => r.Kind == EquipmentRowKind.SlotDuration).ToArray();
        Assert.Equal(2, slots.Length);
        Assert.Equal("Totem slot 2: 00:10.000 · Proven active", slots[0].Name);
        Assert.Equal("Totem slot 2: 00:05.000 · Proven inactive", slots[1].Name);
    }
}
