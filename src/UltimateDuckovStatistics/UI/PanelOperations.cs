using System.Threading.Tasks;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Export;

namespace UltimateDuckovStatistics.UI;

internal enum PanelOperationOutcome { None, Running, Pending, Success, Failure, ClipboardUnavailable }

internal sealed class PanelOperationNotice
{
    public PanelOperation Operation { get; }
    public PanelOperationOutcome Outcome { get; }
    public string GenerationId { get; }
    public string Path { get; }
    public string Detail { get; }
    public bool PriorProfileStillActive { get; }
    public PanelOperationNotice(PanelOperation operation, PanelOperationOutcome outcome, string generation,
        string path = "", string detail = "", bool priorProfileStillActive = false)
    { Operation = operation; Outcome = outcome; GenerationId = generation; Path = path; Detail = detail; PriorProfileStillActive = priorProfileStillActive; }
}

/// <summary>Main-thread single-flight owner shared by mouse, keyboard and retained controls.</summary>
internal sealed class PanelOperationController : IDisposable
{
    private readonly PanelOperationGate gate = new();
    private readonly PanelInteractionState interaction;
    private readonly Func<string> generation;
    private readonly Func<bool> transitioning;
    private readonly Func<Task<ProfileExportResult>> export;
    private readonly Func<bool> reset;
    private readonly Func<NativeUserResetAttempt?> resetAttempt;
    private readonly Func<string, bool> clipboard;
    private readonly Action<PanelOperationNotice> notify;
    private Task<ProfileExportResult>? exportTask;
    private string requestedGeneration = "", confirmationGeneration = "";
    private long priorResetTransition;
    private bool queued, disposed;

    public PanelOperation Current => gate.Current;
    public bool ModalVisible => interaction.ResetConfirmationVisible;
    public bool CanStart => !disposed && Current == PanelOperation.None && !ModalVisible && !transitioning();
    public PanelOperationNotice? LastNotice { get; private set; }

    public PanelOperationController(PanelInteractionState interaction, Func<string> generation, Func<bool> transitioning,
        Func<Task<ProfileExportResult>> export, Func<bool> reset, Func<NativeUserResetAttempt?> resetAttempt,
        Func<string, bool> clipboard, Action<PanelOperationNotice> notify)
    {
        this.interaction = interaction; this.generation = generation; this.transitioning = transitioning;
        this.export = export; this.reset = reset; this.resetAttempt = resetAttempt;
        this.clipboard = clipboard; this.notify = notify;
    }

    public bool RequestExport()
    {
        if (!CanStart || string.IsNullOrWhiteSpace(generation()) || !gate.TryBegin(PanelOperation.Export)) return false;
        requestedGeneration = generation(); queued = true;
        Publish(PanelOperationOutcome.Running); return true;
    }
    public bool RequestResetConfirmation()
    {
        if (!CanStart || string.IsNullOrWhiteSpace(generation())) return false;
        confirmationGeneration = generation(); interaction.ShowResetConfirmation(); return true;
    }
    public bool CancelConfirmation()
    {
        confirmationGeneration = ""; return interaction.CancelModal();
    }
    public bool ConfirmReset()
    {
        if (disposed || !ModalVisible || Current != PanelOperation.None) return false;
        var expected = confirmationGeneration;
        CancelConfirmation();
        if (transitioning() || expected.Length == 0 || generation() != expected || !gate.TryBegin(PanelOperation.Reset)) return false;
        requestedGeneration = expected; queued = true;
        Publish(PanelOperationOutcome.Running); return true;
    }

    public void Tick()
    {
        if (disposed) return;
        if (ModalVisible && (generation() != confirmationGeneration || transitioning())) CancelConfirmation();
        if (queued)
        {
            queued = false;
            if (transitioning() || generation() != requestedGeneration)
            { Finish(PanelOperationOutcome.Failure, detail: "The active profile changed before the operation started."); return; }
            try
            {
                if (Current == PanelOperation.Export) exportTask = export();
                else
                {
                    priorResetTransition = resetAttempt()?.TransitionId ?? 0;
                    _ = reset();
                    ObserveReset();
                }
            }
            catch (Exception exception)
            {
                if (Current == PanelOperation.Reset && transitioning())
                    Publish(PanelOperationOutcome.Pending, detail: exception.GetType().Name + ": " + exception.Message);
                else Finish(PanelOperationOutcome.Failure, detail: exception.GetType().Name + ": " + exception.Message);
            }
        }
        if (Current == PanelOperation.Export && exportTask?.IsCompleted == true)
        {
            var completedTask = exportTask; exportTask = null;
            ProfileExportResult result;
            try { result = completedTask.GetAwaiter().GetResult(); }
            catch (Exception exception) { Finish(PanelOperationOutcome.Failure, detail: exception.ToString()); return; }
            bool copied;
            var clipboardDetail = "";
            try { copied = clipboard(result.Directory); }
            catch (Exception exception)
            { copied = false; clipboardDetail = exception.GetType().Name + ": " + exception.Message; }
            Finish(copied ? PanelOperationOutcome.Success : PanelOperationOutcome.ClipboardUnavailable, result.Directory, clipboardDetail);
        }
        if (Current == PanelOperation.Reset && !queued) ObserveReset();
    }

    private void ObserveReset()
    {
        if (Current != PanelOperation.Reset) return;
        var attempt = resetAttempt();
        if (attempt != null && attempt.TransitionId > priorResetTransition && attempt.RequestedGenerationId == requestedGeneration)
        {
            if (attempt.Outcome == NativeUserResetOutcome.Success && attempt.CompletedGenerationId.Length > 0
                && attempt.CompletedGenerationId != requestedGeneration)
            { Finish(PanelOperationOutcome.Success); return; }
            if (attempt.Outcome == NativeUserResetOutcome.Failure)
            { Finish(PanelOperationOutcome.Failure, detail: attempt.Detail); return; }
        }
        if (!transitioning())
            Finish(PanelOperationOutcome.Failure, detail: "The reset service did not confirm a completed reset for the requested UDS generation.");
        else if (LastNotice?.Outcome != PanelOperationOutcome.Pending)
            Publish(PanelOperationOutcome.Pending, detail: attempt?.Detail ?? "");
    }
    private void Finish(PanelOperationOutcome outcome, string path = "", string detail = "")
    {
        var operation = Current;
        var notice = new PanelOperationNotice(operation, outcome, requestedGeneration, path, detail,
            generation() == requestedGeneration && !transitioning());
        gate.Complete(operation);
        LastNotice = notice; Notify(notice);
    }
    private void Publish(PanelOperationOutcome outcome, string path = "", string detail = "")
    {
        LastNotice = new PanelOperationNotice(Current, outcome, requestedGeneration, path, detail,
            generation() == requestedGeneration);
        Notify(LastNotice);
    }

    private void Notify(PanelOperationNotice notice)
    {
        // Reporting cannot reverse an already completed file operation or reopen its gate.
        try { notify(notice); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; queued = false; CancelConfirmation();
        var task = exportTask; exportTask = null;
        if (task != null)
            _ = task.ContinueWith(completed => { _ = completed.Exception; },
                System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        if (Current != PanelOperation.None) gate.Complete(Current);
    }
}
