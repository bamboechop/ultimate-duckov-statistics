using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests
{
    [Theory]
    [InlineData(KeyCode.F8)]
    [InlineData(KeyCode.Escape)]
    public void BaseHotkeyPausesThroughNativeMenuAndClosingRestoresGameplay(KeyCode closeKey)
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        var focus = new GameObject("Gameplay focus");
        GameManager.EventSystem!.SetSelectedGameObject(focus);
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        using var panel = new NativeStatisticsPanel(coordinator);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Press(panel, KeyCode.F8);
            Assert.True(GameManager.Paused); // FPC's IsUiBlocking uses this native state.
            Assert.Same(canvas.transform, Find(RetainedDimmerPolicy.RootName).transform.parent);
            Assert.True(Cursor.visible);
            Assert.Equal(CursorLockMode.None, Cursor.lockState);
            Assert.Single(InputManager.Blocks);
            Press(panel, closeKey);
            Assert.False(GameManager.Paused);
            Assert.False(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
            Assert.Empty(InputManager.Blocks);
            Assert.Equal(0, UIInputManager.CancelListeners);
            Assert.Same(focus, GameManager.EventSystem.currentSelectedGameObject);
            Assert.False(Cursor.visible);
            Assert.Equal(CursorLockMode.Locked, Cursor.lockState);
        }
    }

    [Fact]
    public void MissingBasePauseMenuRefusesToOpenAnUnpausedOverlay()
    {
        LevelManager.Instance!.IsBaseLevel = true;
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Assert.Empty(InputManager.Blocks);
        Assert.Equal(0, UIInputManager.CancelListeners);
    }

    [Fact]
    public void ShellConstructionFailureAfterPauseReleasesPauseAndInput()
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        using var panel = new NativeStatisticsPanel(coordinator);
        GameObject.FailCreationOf = RetainedDimmerPolicy.RootName;
        try { Press(panel, KeyCode.F8); }
        finally { GameObject.FailCreationOf = null; }
        Assert.False(GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
        Assert.Equal(0, UIInputManager.CancelListeners);
        Press(panel, KeyCode.F8);
        Assert.True(GameManager.Paused);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpeningOverAnExistingPauseDoesNotOwnOrCloseIt(bool hotkey)
    {
        PreparePauseMenu(canvas);
        PauseMenu.Show();
        using var panel = new NativeStatisticsPanel(coordinator);
        if (hotkey) Press(panel, KeyCode.F8);
        else Find("UltimateDuckovStatisticsButton").GetComponent<Button>().onClick.Invoke();
        Assert.True(GameManager.Paused);
        Press(panel, KeyCode.Escape);
        Assert.True(GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
        panel.Dispose();
        Assert.True(GameManager.Paused);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedHotkeyActivationReleasesOnlyItsOwnPause(bool initiallyPaused)
    {
        PreparePauseMenu(canvas);
        if (!initiallyPaused) PauseMenu.Hide();
        using var panel = new NativeStatisticsPanel(coordinator);
        canvas.enabled = false;
        Press(panel, KeyCode.F8);
        Assert.Equal(initiallyPaused, GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
        Assert.Equal(0, UIInputManager.CancelListeners);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        canvas.enabled = true;
        Press(panel, KeyCode.F8);
        Assert.True(GameManager.Paused);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Fact]
    public void PauseShowSubscriberFailureRollsBackNativePauseAndAllowsRetry()
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        using var panel = new NativeStatisticsPanel(coordinator);
        void Fail() => throw new InvalidOperationException("show subscriber failed");
        PauseMenu.onPauseMenuOn += Fail;
        try { Press(panel, KeyCode.F8); }
        finally { PauseMenu.onPauseMenuOn -= Fail; }
        Assert.False(GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
        Assert.Equal(0, UIInputManager.CancelListeners);
        Press(panel, KeyCode.F8);
        Assert.True(GameManager.Paused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileChangeOrDisposalReleasesHotkeyPause(bool disposal)
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        if (disposal) panel.Dispose();
        else coordinator.ChangeProfile(new ProfileDocument { GenerationId = "replacement" });
        Assert.False(GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
        Assert.Equal(0, UIInputManager.CancelListeners);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalPauseClosureClosesUdsWithoutReopeningPause(bool raisesEvent)
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        if (raisesEvent) PauseMenu.Hide();
        else PauseMenu.Instance!.gameObject.SetActive(false); // UIPanel.Close need not publish PauseMenu.Hide.
        Tick(panel);
        Assert.False(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Assert.False(GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
    }

    [Fact]
    public void ReplacingNativePauseOwnerClosesUdsButPreservesTheReplacement()
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        var old = PauseMenu.Instance!;
        PreparePauseMenu(canvas);
        Tick(panel);
        Assert.False(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Assert.True(PauseMenu.Instance!.Shown);
        Assert.NotSame(old, PauseMenu.Instance);
        Assert.Empty(InputManager.Blocks);
    }

    [Fact]
    public void MainMenuHotkeyDoesNotOpenBasePauseEvenWithALoadedBase()
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        canvas.gameObject.AddComponent<MainMenu>();
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        Assert.False(GameManager.Paused);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Fact]
    public void RejectedRaidHotkeyDoesNotOpenPauseOrCaptureInput()
    {
        PreparePauseMenu(canvas);
        PauseMenu.Hide();
        NativeRaidContext.InRaid = true;
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        Assert.False(GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
    }
}
