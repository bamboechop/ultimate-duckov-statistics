using System.Reflection;
using SodaCraft.Localizations;
using TMPro;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests
{
    [Theory]
    [InlineData(true, false, 1280)]
    [InlineData(true, true, 720)]
    [InlineData(false, false, 720)]
    [InlineData(false, true, 1280)]
    public void RetainedModalCaptionsFollowLanguageBeforeOpeningAndWhileVisible(bool reset, bool openBeforeSwitch, int width)
    {
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, width == 720 ? 480 : 720);
        LocalizationManager.SetLanguage(SystemLanguage.English);
        using var panel = new NativeStatisticsPanel(coordinator);
        try
        {
            Press(panel, KeyCode.F8);
            var operations = Field<PanelOperationController>(panel, "operations");
            var cancel = Find("Cancel").GetComponent<Button>();
            var confirm = Find("ConfirmReset").GetComponent<Button>();
            var profile = System.Text.Json.JsonSerializer.Serialize(coordinator.Current);
            var revision = coordinator.Current.Revision;
            void OpenModal()
            {
                if (reset) Assert.True(operations.RequestResetConfirmation());
                else typeof(NativeStatisticsPanel).GetMethod("BeginHotkeyCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null);
                panel.Tick();
            }
            if (openBeforeSwitch)
            {
                OpenModal();
                Assert.Equal(UiText.EnglishFallbacks["ui.diag_cancel"], Find("CancelLabel").GetComponent<TextMeshProUGUI>().text);
                GameManager.EventSystem!.SetSelectedGameObject(reset ? confirm.gameObject : cancel.gameObject);
            }
            var focus = GameManager.EventSystem!.currentSelectedGameObject;
            LocalizationManager.SetLanguage(SystemLanguage.German);
            panel.Tick();
            if (!openBeforeSwitch) OpenModal();
            CheckCaptions(UiText.GermanFallbacks);
            if (openBeforeSwitch) Assert.Same(focus, GameManager.EventSystem.currentSelectedGameObject);
            Assert.Same(cancel, Find("Cancel").GetComponent<Button>());
            Assert.Same(confirm, Find("ConfirmReset").GetComponent<Button>());
            var germanWidth = ((RectTransform)cancel.transform).rect.width;
            Assert.Equal(reset, confirm.gameObject.activeSelf);
            Assert.Equal(reset, confirm.interactable);
            Assert.Single(InputManager.Blocks);

            LocalizationManager.SetLanguage(SystemLanguage.English);
            panel.Tick();
            CheckCaptions(UiText.EnglishFallbacks);
            Assert.True(germanWidth > ((RectTransform)cancel.transform).rect.width);
            Assert.Equal(1, cancel.onClick.ListenerCount);
            Assert.Equal(1, confirm.onClick.ListenerCount);
            cancel.onClick.Invoke(); panel.Tick();
            Assert.False(Find("UDSOperationModal").activeSelf);
            Assert.False(operations.ModalVisible);
            Assert.False(Field<bool>(panel, "capturingHotkey"));
            Assert.Equal(revision, coordinator.Current.Revision);
            Assert.Equal(profile, System.Text.Json.JsonSerializer.Serialize(coordinator.Current));

            void CheckCaptions(IReadOnlyDictionary<string, string> translations)
            {
                Assert.Equal(translations[reset ? "ui.diag_reset_title" : "ui.diag_hotkey_title"], Find("Title").GetComponent<TextMeshProUGUI>().text);
                foreach (var (name, key) in new[] { ("CancelLabel", "ui.diag_cancel"), ("ConfirmResetLabel", "ui.diag_reset_confirm") })
                {
                    var label = Find(name).GetComponent<TextMeshProUGUI>();
                    Assert.Equal(translations[key], label.text);
                    if (!label.gameObject.activeInHierarchy) continue;
                    Assert.True(label.rectTransform.rect.height + .01f >= label.GetPreferredValues(label.rectTransform.rect.width, float.PositiveInfinity).y);
                    var button = (RectTransform)label.transform.parent!;
                    Assert.True(label.rectTransform.anchoredPosition.x + label.rectTransform.rect.width <= button.rect.width);
                }
            }
        }
        finally { LocalizationManager.SetLanguage(SystemLanguage.English); }
    }
}
