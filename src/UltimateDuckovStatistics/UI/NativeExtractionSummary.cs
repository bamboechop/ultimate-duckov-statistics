using System.Globalization;
using Duckov.UI;
using Duckov.Utilities;
using TMPro;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

/// <summary>Owns only the extra results block; never opens/closes the native view or handles Continue.</summary>
internal sealed class NativeExtractionSummary : IDisposable
{
    private readonly ExtractionSummarySession session = new();
    private readonly Func<string> generation;
    private readonly Action<string> diagnostic;
    private readonly Func<decimal?> readValue;
    private ClosureView? owner;
    private GameObject? root;
    private TextMeshProUGUI[] values = Array.Empty<TextMeshProUGUI>();
    private TextMeshProUGUI? note;
    private bool disposed;

    public NativeExtractionSummary(Func<string> generation, Action<string> diagnostic, Func<decimal?>? readValue = null)
    {
        this.generation = generation;
        this.diagnostic = diagnostic;
        this.readValue = readValue ?? (() => NativeExtractionValuation.Read(diagnostic));
        ManagedUIElement.onOpen += OnOpen;
        ManagedUIElement.onClose += OnClose;
    }

    public void Begin(string generationId, string runId, bool observedFreshRaid)
    {
        if (disposed) return;
        Reset();
        session.Begin(generationId, runId, observedFreshRaid ? readValue() : null);
    }

    public void Terminal(RunOutcome outcome)
    {
        if (!disposed) session.ObserveTerminal(outcome, outcome == RunOutcome.Extracted ? readValue() : null);
    }

    public void Complete(RunSummary summary)
    {
        if (disposed) return;
        session.Complete(summary);
        SafelyRefresh();
    }

    public void Reset()
    {
        session.Clear();
        DestroyBlock();
    }

    private void OnOpen(ManagedUIElement element)
    {
        if (disposed || element is not ClosureView closure) return;
        DestroyBlock();
        owner = closure;
        try
        {
            var content = closure.transform.Find("Content");
            var exp = content?.Find("ExpBarContainer");
            var template = GameplayDataSettings.UIStyle.TemplateTextUGUI;
            if (content == null || exp == null || template == null)
            {
                diagnostic("Results summary omitted: native Content/ExpBarContainer or typography unavailable.");
                return;
            }
            root = new GameObject("UDS.ResultsSummary", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            root.transform.SetParent(content, false);
            root.transform.SetSiblingIndex(exp.GetSiblingIndex());
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(900, 150);
            var size = root.GetComponent<LayoutElement>();
            size.minHeight = 150; size.preferredHeight = 150; size.flexibleWidth = 1;
            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            var row = new GameObject("Metrics", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(root.transform, false);
            row.GetComponent<LayoutElement>().preferredHeight = 84;
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 24; rowLayout.childControlWidth = true; rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true; rowLayout.childForceExpandHeight = true;
            var keys = new[] { "ui.results_time", "ui.results_kills", "ui.results_net_value" };
            values = new TextMeshProUGUI[3];
            for (var index = 0; index < keys.Length; index++)
            {
                var cell = new GameObject(keys[index], typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
                cell.transform.SetParent(row.transform, false);
                var cellSize = cell.GetComponent<LayoutElement>();
                cellSize.minWidth = 0; cellSize.preferredWidth = 0; cellSize.flexibleWidth = 1;
                var cellLayout = cell.GetComponent<VerticalLayoutGroup>();
                cellLayout.childControlWidth = true; cellLayout.childControlHeight = true;
                cellLayout.childForceExpandHeight = false; cellLayout.spacing = 4;
                Text(cell.transform, template, UiText.Get(keys[index]), 24, 30, new Color(.75f, .75f, .75f));
                values[index] = Text(cell.transform, template, "", 34, 46, Color.white);
            }
            note = Text(root.transform, template, "", 18, 52, new Color(.75f, .75f, .75f));
            Refresh();
        }
        catch (Exception exception)
        {
            DestroyBlock();
            diagnostic($"Results summary UI omitted safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static TextMeshProUGUI Text(Transform parent, TextMeshProUGUI template, string text, float fontSize, float height, Color color)
    {
        // New components avoid cloning native buttons, localized captions, animations, or sound handlers.
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = template.font;
        label.fontSharedMaterial = template.fontSharedMaterial;
        label.fontSize = fontSize;
        label.enableAutoSizing = true; label.fontSizeMin = fontSize - 4; label.fontSizeMax = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color; label.richText = false; label.raycastTarget = false;
        label.enableWordWrapping = true; label.text = text;
        go.GetComponent<LayoutElement>().preferredHeight = height;
        return label;
    }

    private void SafelyRefresh()
    {
        if (disposed || owner == null || !owner.open) return;
        try { Refresh(); }
        catch (Exception exception)
        {
            DestroyBlock();
            diagnostic($"Results summary refresh omitted safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void Refresh()
    {
        if (root == null || values.Length != 3 || note == null) return;
        var snapshot = session.Completed;
        var raid = RaidUtilities.CurrentRaid;
        if (snapshot?.GenerationId != generation()
            || (snapshot.NativeRaidId != null && (!raid.valid || snapshot.NativeRaidId != raid.ID.ToString(CultureInfo.InvariantCulture))))
            snapshot = null;
        var texts = ExtractionSummaryPresentation.Values(snapshot, UiText.Get);
        for (var index = 0; index < values.Length; index++) values[index].text = texts[index];
        note.text = ExtractionSummaryPresentation.Note(snapshot, UiText.Get);
    }

    private void OnClose(ManagedUIElement element)
    {
        if (element is ClosureView) Reset();
    }

    private void DestroyBlock()
    {
        if (root != null)
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
        root = null; owner = null; note = null; values = Array.Empty<TextMeshProUGUI>();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ManagedUIElement.onOpen -= OnOpen;
        ManagedUIElement.onClose -= OnClose;
        Reset();
    }
}
