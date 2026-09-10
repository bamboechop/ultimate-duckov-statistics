using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class PanelOperationControllerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DismissedExportCompletesWithoutRestoringResultOrClipboard(bool changeProfile, bool fail)
    {
        using var h = new Harness();
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        if (changeProfile) { h.Generation = "other"; h.Controller.Tick(); }
        else h.Controller.DismissExportResult();
        Assert.Null(h.Controller.LastNotice);
        Assert.Equal(PanelOperation.Export, h.Controller.Current);
        Assert.False(h.Controller.RequestExport());
        // Even returning to the original profile cannot revive the dismissed request.
        h.Generation = "g";
        if (fail) h.ExportCompletion.SetException(new IOException("disk failure"));
        else h.ExportCompletion.SetResult(new ProfileExportResult("completed-file-directory", Array.Empty<string>()));
        h.Controller.Tick();
        Assert.Null(h.Controller.LastNotice); Assert.Equal(0, h.ClipboardCalls);
        Assert.Equal(PanelOperation.None, h.Controller.Current);
        var completed = h.Notices.Last(); Assert.False(completed.PresentResult);
        Assert.Equal(fail ? PanelOperationOutcome.Failure : PanelOperationOutcome.Success, completed.Outcome);
        if (!fail) Assert.Equal("completed-file-directory", completed.Path);
        h.Export = () => Task.FromResult(new ProfileExportResult("new-export", Array.Empty<string>()));
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        Assert.Equal("new-export", h.Controller.LastNotice!.Path); Assert.Equal(1, h.ClipboardCalls);
    }

    [Fact]
    public void ClosingBeforeQueuedExportStartsStillCompletesTheExportWithoutConfirmation()
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestExport());
        h.Controller.DismissExportResult(); h.Controller.Tick();
        Assert.Equal(1, h.ExportCalls); Assert.Equal(PanelOperation.Export, h.Controller.Current);
        h.ExportCompletion.SetResult(new ProfileExportResult("completed", Array.Empty<string>()));
        h.Controller.Tick(); Assert.Null(h.Controller.LastNotice); Assert.Equal(0, h.ClipboardCalls);
        Assert.True(h.Controller.CanStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedExportResultIsClearedByCloseOrGenerationChange(bool generationChange)
    {
        using var h = new Harness { Export = () => Task.FromResult(new ProfileExportResult("completed", Array.Empty<string>())) };
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick(); Assert.NotNull(h.Controller.LastNotice);
        if (generationChange) { h.Generation = "other"; h.Controller.Tick(); }
        else h.Controller.DismissExportResult();
        Assert.Null(h.Controller.LastNotice); Assert.Equal(1, h.ClipboardCalls);
    }

    private sealed class Harness : IDisposable
    {
        public string Generation = "g";
        public NativeUserResetAttempt? ResetAttempt;
        public bool Transitioning;
        public int ExportCalls, ResetCalls, ClipboardCalls;
        public string Copied = "";
        public readonly PanelInteractionState Interaction = new();
        public readonly List<PanelOperationNotice> Notices = new();
        public readonly TaskCompletionSource<ProfileExportResult> ExportCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Task<ProfileExportResult>> Export;
        public Func<bool> Reset = () => false;
        public Func<string, bool> Clipboard = _ => true;
        public Action<PanelOperationNotice>? Observer;
        public readonly PanelOperationController Controller;
        public Harness()
        {
            Export = () => ExportCompletion.Task;
            Controller = new PanelOperationController(Interaction, () => Generation, () => Transitioning,
                () => { ExportCalls++; return Export(); }, () => { ResetCalls++; return Reset(); },
                () => ResetAttempt,
                path => { ClipboardCalls++; Copied = path; return Clipboard(path); },
                notice => { Notices.Add(notice); Observer?.Invoke(notice); });
        }
        public void ConfirmReset()
        {
            Assert.True(Controller.RequestResetConfirmation()); Assert.True(Controller.ConfirmReset());
        }
        public bool CommitReset()
        {
            var requested = ResetAttempt?.Outcome == NativeUserResetOutcome.Pending ? ResetAttempt.RequestedGenerationId : Generation;
            var token = ResetAttempt?.Outcome == NativeUserResetOutcome.Pending ? ResetAttempt.TransitionId : (ResetAttempt?.TransitionId ?? 0) + 1;
            ResetAttempt = new NativeUserResetAttempt(token, requested, NativeUserResetOutcome.Success, "next", "");
            Generation = "next"; Transitioning = false; return true;
        }
        public void Dispose() => Controller.Dispose();
    }

    [Fact]
    public void ConfirmationStartsOnCancelAndCancelNeverInvokesReset()
    {
        using var h = new Harness();
        Assert.True(h.Controller.RequestResetConfirmation()); Assert.True(h.Controller.ModalVisible);
        Assert.True(h.Interaction.ResetCancelHasInitialFocus); Assert.False(h.Controller.CanStart);
        Assert.False(h.Controller.RequestExport()); Assert.False(h.Controller.RequestResetConfirmation());
        Assert.True(h.Controller.CancelConfirmation()); Assert.False(h.Controller.ModalVisible);
        Assert.False(h.Interaction.ResetCancelHasInitialFocus); Assert.False(h.Controller.CancelConfirmation());
        Assert.False(h.Controller.ConfirmReset()); h.Controller.Tick();
        Assert.Equal(0, h.ResetCalls); Assert.Equal(0, h.ExportCalls); Assert.Empty(h.Notices); Assert.True(h.Controller.CanStart);
    }

    [Fact]
    public void ConfirmQueuesOneResetAndRejectsDuplicateMouseOrKeyboardActivation()
    {
        using var h = new Harness(); h.Reset = h.CommitReset;
        h.ConfirmReset(); Assert.False(h.Controller.ModalVisible); Assert.Equal(0, h.ResetCalls);
        Assert.False(h.Controller.ConfirmReset()); Assert.False(h.Controller.RequestResetConfirmation()); Assert.False(h.Controller.RequestExport());
        h.Controller.Tick(); h.Controller.Tick();
        Assert.Equal(1, h.ResetCalls); Assert.Equal(PanelOperation.None, h.Controller.Current);
        Assert.Equal(new[] { PanelOperationOutcome.Running, PanelOperationOutcome.Success }, h.Notices.Select(n => n.Outcome));
        Assert.Equal("g", h.Controller.LastNotice!.GenerationId); Assert.False(h.Controller.LastNotice.PriorProfileStillActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedGenerationOrTransitionCancelsAnOpenConfirmation(bool transition)
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestResetConfirmation());
        if (transition) h.Transitioning = true; else h.Generation = "next";
        h.Controller.Tick(); Assert.False(h.Controller.ModalVisible); Assert.False(h.Controller.ConfirmReset());
        Assert.Equal(0, h.ResetCalls); Assert.Empty(h.Notices);
    }

    [Fact]
    public void ConfirmRejectsChangedGenerationEvenBeforeNextTick()
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestResetConfirmation()); h.Generation = "next";
        Assert.False(h.Controller.ConfirmReset()); Assert.False(h.Controller.ModalVisible);
        h.Controller.Tick(); Assert.Equal(0, h.ResetCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void QueuedOperationChecksGenerationAndTransitionBeforeTouchingServices(bool reset, bool transition)
    {
        using var h = new Harness(); if (reset) h.ConfirmReset(); else Assert.True(h.Controller.RequestExport());
        if (transition) h.Transitioning = true; else h.Generation = "next";
        h.Controller.Tick();
        Assert.Equal(0, h.ResetCalls); Assert.Equal(0, h.ExportCalls); Assert.Equal(0, h.ClipboardCalls);
        var result = h.Notices.Last();
        Assert.Equal(PanelOperationOutcome.Failure, result.Outcome);
        Assert.Equal("g", result.GenerationId); Assert.Equal(PanelOperation.None, h.Controller.Current);
        Assert.False(result.PriorProfileStillActive);
        Assert.Equal(reset, result.PresentResult);
        if (!reset) Assert.Null(h.Controller.LastNotice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyGenerationOrPendingTransitionRejectsNewOperations(bool transition)
    {
        using var h = new Harness(); if (transition) h.Transitioning = true; else h.Generation = "";
        Assert.False(h.Controller.RequestExport()); Assert.False(h.Controller.RequestResetConfirmation()); Assert.False(h.Controller.ConfirmReset());
        h.Controller.Tick(); Assert.Empty(h.Notices); Assert.Equal(0, h.ResetCalls); Assert.Equal(0, h.ExportCalls);
    }

    [Fact]
    public void ExportWaitsForTheTaskAndCopiesItsActualResultExactlyOnce()
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestExport()); Assert.Equal(0, h.ExportCalls);
        Assert.False(h.Controller.RequestExport()); Assert.False(h.Controller.RequestResetConfirmation());
        h.Controller.Tick(); h.Controller.Tick(); Assert.Equal(1, h.ExportCalls); Assert.Equal(0, h.ClipboardCalls);
        Assert.Equal(PanelOperation.Export, h.Controller.Current); Assert.Equal(PanelOperationOutcome.Running, h.Controller.LastNotice!.Outcome);
        h.ExportCompletion.SetResult(new ProfileExportResult("C:\\exports\\completed-g", Array.Empty<string>()));
        h.Controller.Tick(); h.Controller.Tick();
        Assert.Equal(1, h.ClipboardCalls); Assert.Equal("C:\\exports\\completed-g", h.Copied);
        Assert.Equal(PanelOperationOutcome.Success, h.Controller.LastNotice!.Outcome);
        Assert.Equal(h.Copied, h.Controller.LastNotice.Path); Assert.Equal(PanelOperation.None, h.Controller.Current);
        Assert.Equal(2, h.Notices.Count); Assert.True(h.Controller.CanStart);
    }

    [Fact]
    public void SynchronouslyCompletedExportStillUsesItsReturnedDirectory()
    {
        using var h = new Harness { Export = () => Task.FromResult(new ProfileExportResult("actual", Array.Empty<string>())) };
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Success, h.Controller.LastNotice!.Outcome); Assert.Equal("actual", h.Copied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrCancelledExportTaskDoesNotCopyOrClaimSuccess(bool cancelled)
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        if (cancelled) h.ExportCompletion.SetCanceled(); else h.ExportCompletion.SetException(new IOException("disk denied"));
        h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome); Assert.Equal(0, h.ClipboardCalls);
        Assert.Empty(h.Controller.LastNotice.Path); Assert.NotEmpty(h.Controller.LastNotice.Detail);
        Assert.Equal(PanelOperation.None, h.Controller.Current); Assert.True(h.Controller.CanStart);
        Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Success);
    }

    [Fact]
    public void SynchronousExportFailureReleasesTheGateForRetry()
    {
        using var h = new Harness { Export = () => throw new IOException("snapshot unavailable") };
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome); Assert.Equal(0, h.ClipboardCalls);
        h.Export = () => Task.FromResult(new ProfileExportResult("retry", Array.Empty<string>()));
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Success, h.Controller.LastNotice!.Outcome); Assert.Equal(2, h.ExportCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipboardFailurePreservesSuccessfulExportAndVisibleLocation(bool throws)
    {
        using var h = new Harness(); h.Clipboard = _ => throws ? throw new InvalidOperationException("clipboard") : false;
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        h.ExportCompletion.SetResult(new ProfileExportResult("saved-export", Array.Empty<string>())); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.ClipboardUnavailable, h.Controller.LastNotice!.Outcome);
        Assert.Equal("saved-export", h.Controller.LastNotice.Path); Assert.Equal(1, h.ExportCalls); Assert.Equal(1, h.ClipboardCalls);
        Assert.Equal(PanelOperation.None, h.Controller.Current); Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Failure);
    }

    [Fact]
    public void AlreadyStartedExportKeepsItsCapturedGenerationWhenTheActiveProfileChanges()
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        h.Generation = "next"; h.ExportCompletion.SetResult(new ProfileExportResult("old-generation-export", Array.Empty<string>()));
        h.Controller.Tick();
        var result = h.Notices.Last();
        Assert.Equal(PanelOperationOutcome.Success, result.Outcome);
        Assert.Equal("g", result.GenerationId); Assert.False(result.PriorProfileStillActive);
        Assert.Equal("old-generation-export", result.Path);
        Assert.False(result.PresentResult); Assert.Null(h.Controller.LastNotice); Assert.Equal(0, h.ClipboardCalls);
    }

    [Fact]
    public void ResetStaysSingleFlightAcrossRetryUntilServiceConfirmsNewGeneration()
    {
        using var h = new Harness(); h.Reset = () => { h.Transitioning = true; return false; };
        h.ConfirmReset(); h.Controller.Tick(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Pending, h.Controller.LastNotice!.Outcome); Assert.Equal(PanelOperation.Reset, h.Controller.Current);
        Assert.False(h.Controller.RequestExport()); Assert.False(h.Controller.RequestResetConfirmation()); Assert.False(h.Controller.ConfirmReset());
        Assert.Equal(1, h.ResetCalls); Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Success);
        h.CommitReset(); h.Controller.Tick(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Success, h.Controller.LastNotice!.Outcome); Assert.Equal(1, h.ResetCalls);
        Assert.Equal(new[] { PanelOperationOutcome.Running, PanelOperationOutcome.Pending, PanelOperationOutcome.Success }, h.Notices.Select(n => n.Outcome));
    }

    [Fact]
    public void ResetExceptionWhileBoundaryIsPendingCannotBeReportedAsSafeFailure()
    {
        using var h = new Harness(); h.Reset = () => { h.Transitioning = true; throw new IOException("retry publication"); };
        h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Pending, h.Controller.LastNotice!.Outcome); Assert.Equal(PanelOperation.Reset, h.Controller.Current);
        Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Failure);
        h.CommitReset(); h.Controller.Tick(); Assert.Equal(PanelOperationOutcome.Success, h.Controller.LastNotice!.Outcome);
    }

    [Fact]
    public void ResetExceptionWithPriorGenerationStillActiveIsFailureAndCanBeRetried()
    {
        using var h = new Harness { Reset = () => throw new IOException("archive denied") };
        h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome); Assert.True(h.Controller.LastNotice.PriorProfileStillActive);
        Assert.Equal(PanelOperation.None, h.Controller.Current); Assert.True(h.Controller.RequestResetConfirmation());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetReturnValueAloneCannotClaimACommittedNewGeneration(bool returned)
    {
        using var h = new Harness { Reset = () => returned };
        h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome);
        Assert.True(h.Controller.LastNotice.PriorProfileStillActive); Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Success);
    }

    [Fact]
    public void APreviousSuccessfulResetDoesNotSatisfyTheNextRequestedReset()
    {
        using var h = new Harness(); h.Reset = h.CommitReset; h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Success, h.Controller.LastNotice!.Outcome);
        h.Reset = () => true; h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome);
        Assert.Equal("next", h.Controller.LastNotice.GenerationId); Assert.Equal(2, h.ResetCalls);
    }

    [Fact]
    public void TypedPreCommitFailureAfterCleanupReportsTheStillActivePriorGeneration()
    {
        using var h = new Harness();
        h.Reset = () =>
        {
            h.ResetAttempt = new NativeUserResetAttempt(1, "g", NativeUserResetOutcome.Pending, "", "archive blocked");
            h.Transitioning = true; return false;
        };
        h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Pending, h.Controller.LastNotice!.Outcome);
        h.ResetAttempt = new NativeUserResetAttempt(1, "g", NativeUserResetOutcome.Failure, "", "archive denied; adapter cleanup complete");
        h.Transitioning = false; h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome);
        Assert.True(h.Controller.LastNotice.PriorProfileStillActive);
        Assert.Equal("archive denied; adapter cleanup complete", h.Controller.LastNotice.Detail);
        Assert.True(h.Controller.CanStart); Assert.Equal(1, h.ResetCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTheRequestedGenerationAndNewTransitionTokenCanCompleteReset(bool oldToken)
    {
        using var h = new Harness();
        h.ResetAttempt = new NativeUserResetAttempt(8, "previous", NativeUserResetOutcome.Success, "g", "");
        h.Reset = () =>
        {
            h.Transitioning = true;
            h.ResetAttempt = new NativeUserResetAttempt(oldToken ? 8 : 9, oldToken ? "g" : "unrelated",
                NativeUserResetOutcome.Success, "another", "");
            return true;
        };
        h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Pending, h.Controller.LastNotice!.Outcome);
        Assert.Equal(PanelOperation.Reset, h.Controller.Current); Assert.False(h.Controller.RequestExport());
        h.Transitioning = false; h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome);
        Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("g")]
    public void TypedSuccessStillRequiresAProvenDifferentCompletedGeneration(string completedGeneration)
    {
        using var h = new Harness();
        h.Reset = () =>
        {
            h.ResetAttempt = new NativeUserResetAttempt(1, "g", NativeUserResetOutcome.Success, completedGeneration, "");
            return true;
        };
        h.ConfirmReset(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Failure, h.Controller.LastNotice!.Outcome);
        Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Success);
    }

    [Fact]
    public void ReportingExceptionsCannotChangeExportResultOrInvokeClipboardAgain()
    {
        using var h = new Harness { Observer = _ => throw new InvalidOperationException("UI observer disposed") };
        Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        h.ExportCompletion.SetResult(new ProfileExportResult("complete", Array.Empty<string>()));
        h.Controller.Tick(); h.Controller.Tick();
        Assert.Equal(PanelOperationOutcome.Success, h.Controller.LastNotice!.Outcome);
        Assert.Equal(PanelOperation.None, h.Controller.Current); Assert.Equal(1, h.ClipboardCalls);
        Assert.Equal(new[] { PanelOperationOutcome.Running, PanelOperationOutcome.Success }, h.Notices.Select(n => n.Outcome));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposalCancelsQueuedWorkWithoutCallingTheService(bool reset)
    {
        using var h = new Harness();
        if (reset) h.ConfirmReset(); else Assert.True(h.Controller.RequestExport());
        h.Controller.Dispose(); h.Controller.Dispose(); h.Controller.Tick();
        Assert.Equal(0, h.ExportCalls); Assert.Equal(0, h.ResetCalls); Assert.Single(h.Notices);
        Assert.Equal(PanelOperation.None, h.Controller.Current); Assert.False(h.Controller.CanStart);
        Assert.False(h.Controller.RequestExport()); Assert.False(h.Controller.RequestResetConfirmation());
    }

    [Fact]
    public void DisposalDismissesTheConfirmationWithoutConfirmingIt()
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestResetConfirmation());
        h.Controller.Dispose(); Assert.False(h.Controller.ModalVisible); Assert.False(h.Controller.ConfirmReset());
        Assert.Equal(0, h.ResetCalls); Assert.Empty(h.Notices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedExportCannotCallClipboardOrReportIntoTheRemovedPanel(bool faulted)
    {
        using var h = new Harness(); Assert.True(h.Controller.RequestExport()); h.Controller.Tick();
        h.Controller.Dispose();
        if (faulted) h.ExportCompletion.SetException(new IOException("late failure"));
        else h.ExportCompletion.SetResult(new ProfileExportResult("late export", Array.Empty<string>()));
        h.Controller.Tick();
        Assert.Equal(1, h.ExportCalls); Assert.Equal(0, h.ClipboardCalls); Assert.Single(h.Notices);
        Assert.Equal(PanelOperation.None, h.Controller.Current);
    }

    [Fact]
    public void DisposalDoesNotRetryOrOwnTheAlreadyPendingResetServiceBoundary()
    {
        using var h = new Harness(); h.Reset = () => { h.Transitioning = true; return false; };
        h.ConfirmReset(); h.Controller.Tick(); h.Controller.Dispose(); h.CommitReset(); h.Controller.Tick();
        Assert.Equal(1, h.ResetCalls); Assert.Equal(2, h.Notices.Count);
        Assert.DoesNotContain(h.Notices, n => n.Outcome == PanelOperationOutcome.Success);
    }
}
