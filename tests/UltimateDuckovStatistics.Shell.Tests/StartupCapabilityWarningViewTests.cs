using TMPro;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class StartupCapabilityWarningViewTests
{
    [Fact]
    public void WaitsForNativeUiThenBlocksClicksListsDisabledCapabilitiesAndRestoresFocus()
    {
        var previous = EventSystem.current;
        EventSystem.current = null;
        Assert.False(NativeStartupCapabilityWarning.TryCreate(new[] { "native-headshots" }, out _));
        var menu = new GameObject("WarningTestMenu", typeof(RectTransform));
        var template = menu.AddComponent<TextMeshProUGUI>();
        template.font = new TMP_FontAsset(); template.fontSharedMaterial = new Material();
        EventSystem.current = new EventSystem();
        EventSystem.current.SetSelectedGameObject(menu);
        try
        {
            Assert.True(NativeStartupCapabilityWarning.TryCreate(new[] { "native-headshots", "native-healing-attribution" }, out var warning));
            using (warning!)
            {
                var root = GameObject.Live.Single(value => value.name == "UDSStartupCapabilityWarning");
                Assert.Equal(RenderMode.ScreenSpaceOverlay, root.GetComponent<Canvas>().renderMode);
                Assert.NotNull(root.GetComponent<GraphicRaycaster>());
                Assert.True(root.GetComponentsInChildren<Image>().Single(value => value.gameObject.name == "Shade").raycastTarget);
                Assert.True(Assert.Single(root.GetComponentsInChildren<ScrollRect>()).vertical);
                var labels = root.GetComponentsInChildren<TextMeshProUGUI>();
                Assert.Contains(labels, value => value.text.Contains(UiText.Get("ui.diag_cap_native-headshots"), StringComparison.Ordinal));
                Assert.All(labels, value => Assert.NotNull(value.font));
                var button = Assert.Single(root.GetComponentsInChildren<Button>());
                Assert.Same(button.gameObject, EventSystem.current.currentSelectedGameObject);
                button.onClick.Invoke();
                Assert.True(root.Destroyed);
                Assert.Same(menu, EventSystem.current.currentSelectedGameObject);
            }
        }
        finally { EventSystem.current = previous; UnityEngine.Object.Destroy(menu); }
    }
}
