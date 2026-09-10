using System.Reflection;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class RetainedPanelModalTests : IDisposable
{
    private readonly GameObject host = new("Modal host");
    private readonly GameObject prior = new("Prior selection");
    private readonly PanelOperationController operations;
    private readonly object modal;
    private readonly Type modalType = typeof(RetainedStatisticsShell).GetNestedType("PanelModal", BindingFlags.NonPublic)!;
    private int resetCalls, hotkeyCancellations, fallbackCalls;

    public RetainedPanelModalTests()
    {
        GameManager.EventSystem = new();
        prior.transform.SetParent(host.transform);
        operations = new(new PanelInteractionState(), () => "g", () => false,
            () => Task.FromException<ProfileExportResult>(new InvalidOperationException("No export requested")),
            () => { resetCalls++; return false; }, () => null, _ => true, _ => { });
        modal = Activator.CreateInstance(modalType, (RectTransform)host.transform,
            new NativeHeaderTitleTypography(), new Material(), operations,
            (Action)(() => hotkeyCancellations++), (Action)(() => fallbackCalls++))!;
        GameManager.EventSystem.SetSelectedGameObject(prior);
    }

    [Fact]
    public void ResetStartsOnCancelContainsFocusAndRestoresOriginalBackgroundStates()
    {
        var enabled = prior.AddComponent<CanvasGroup>();
        var disabledObject = new GameObject("Already disabled");
        disabledObject.transform.SetParent(host.transform);
        var disabled = disabledObject.AddComponent<CanvasGroup>();
        disabled.interactable = false;
        Assert.True(operations.RequestResetConfirmation());
        Sync(reset: true);
        var root = Find("UDSOperationModal");
        Assert.True(root.GetComponent<Image>().raycastTarget);
        Assert.Same(Cancel.gameObject, Selected);
        Assert.False(enabled.interactable);
        Assert.False(disabled.interactable);
        Move(false); Assert.Same(Confirm.gameObject, Selected);
        Move(false); Assert.Same(Cancel.gameObject, Selected);
        Move(true); Assert.Same(Confirm.gameObject, Selected);
        Move(true); Assert.Same(Cancel.gameObject, Selected);
        GameManager.EventSystem!.SetSelectedGameObject(prior);
        Sync(reset: true);
        Assert.Same(Cancel.gameObject, Selected);
        Cancel.onClick.Invoke();
        Assert.False(operations.ModalVisible);
        Sync();
        Assert.Same(prior, Selected);
        Assert.True(enabled.interactable);
        Assert.False(disabled.interactable);
        Assert.Equal(0, resetCalls);
    }

    [Fact]
    public void HotkeyModeCannotReachOrInvokeResetAndCancellationRestoresPriorFocus()
    {
        Sync(hotkey: true);
        Assert.Same(Cancel.gameObject, Selected);
        Assert.False(Confirm.gameObject.activeSelf);
        Assert.False(Confirm.interactable);
        Move(false); Move(true);
        Assert.Same(Cancel.gameObject, Selected);
        Confirm.onClick.Invoke();
        operations.Tick();
        Assert.Equal(0, resetCalls);
        Cancel.onClick.Invoke();
        Assert.Equal(1, hotkeyCancellations);
        Sync();
        Assert.Same(prior, Selected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DismissalFallsBackWhenThePriorSelectionIsInactiveOrDestroyed(bool destroyed)
    {
        Sync(hotkey: true);
        if (destroyed) UnityEngine.Object.Destroy(prior);
        else prior.SetActive(false);
        Sync();
        Assert.Equal(1, fallbackCalls);
    }

    [Fact]
    public void RepeatedConfirmQueuesOneResetAndDisposeRestoresBlockingAndDisarmsButtons()
    {
        var background = prior.AddComponent<CanvasGroup>();
        Assert.True(operations.RequestResetConfirmation());
        Sync(reset: true);
        Confirm.onClick.Invoke();
        Confirm.onClick.Invoke();
        operations.Tick();
        Assert.Equal(1, resetCalls);
        var cancel = Cancel;
        var confirm = Confirm;
        ((IDisposable)modal).Dispose();
        ((IDisposable)modal).Dispose();
        Assert.True(background.interactable);
        Assert.Equal(0, cancel.onClick.ListenerCount);
        Assert.Equal(0, confirm.onClick.ListenerCount);
        cancel.onClick.Invoke(); confirm.onClick.Invoke(); operations.Tick();
        Assert.Equal(1, resetCalls);
        Assert.Equal(0, hotkeyCancellations);
    }

    private GameObject? Selected => GameManager.EventSystem!.currentSelectedGameObject;
    private Button Cancel => Find("Cancel").GetComponent<Button>();
    private Button Confirm => Find("ConfirmReset").GetComponent<Button>();
    private static GameObject Find(string name) => Assert.Single(GameObject.Live, value => value.name == name);
    private void Sync(bool reset = false, bool hotkey = false) => modalType.GetMethod("Sync")!.Invoke(modal, [reset, hotkey, "Current profile", ""]);
    private void Move(bool reverse) => modalType.GetMethod("MoveFocus")!.Invoke(modal, [reverse]);
    public void Dispose()
    {
        ((IDisposable)modal).Dispose();
        operations.Dispose();
        foreach (var go in GameObject.Live.ToArray()) UnityEngine.Object.Destroy(go);
        GameManager.EventSystem = new();
    }
}
