using System.Threading.Tasks;
using System.Threading;
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
    public bool PresentResult { get; }
    public PanelOperationNotice(PanelOperation operation, PanelOperationOutcome outcome, string generation,
        string path = "", string detail = "", bool priorProfileStillActive = false, bool presentResult = true)
    { Operation = operation; Outcome = outcome; GenerationId = generation; Path = path; Detail = detail; PriorProfileStillActive = priorProfileStillActive; PresentResult = presentResult; }
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
    private bool exportResultDismissed;
    private Func<Task<IReadOnlyList<string>>>? listRestoreSources;
    private Func<string, CancellationToken, Task<StatisticsRestorePreview>>? readRestore;
    private Func<StatisticsRestorePreview, bool>? restore;
    private Task<IReadOnlyList<string>>? restoreListTask;
    private Task<StatisticsRestorePreview>? restorePreviewTask;
    private CancellationTokenSource? restorePreviewCancellation;
    private StatisticsRestorePreview? queuedRestore;
    public bool RestoreSelectionVisible { get; private set; }
    public bool RestoreAvailable => restore != null;
    public IReadOnlyList<string> RestoreSources { get; private set; } = Array.Empty<string>();
    public string RestorePath { get; private set; } = "";
    public string RestoreError { get; private set; } = "";
    public StatisticsRestorePreview? RestorePreview { get; private set; }
    public bool RestoreLoading => restoreListTask != null || restorePreviewTask != null;

    public PanelOperation Current => gate.Current;
    public bool ModalVisible => interaction.ResetConfirmationVisible || RestoreSelectionVisible;
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
        requestedGeneration = generation(); queued = true; exportResultDismissed = false;
        Publish(PanelOperationOutcome.Running); return true;
    }
    public void ConfigureRestore(Func<Task<IReadOnlyList<string>>> list, Func<string, CancellationToken, Task<StatisticsRestorePreview>> preview,
        Func<StatisticsRestorePreview, bool> apply)
    { listRestoreSources = list; readRestore = preview; restore = apply; }

    public bool RequestRestoreSelection()
    {
        if (!CanStart || !RestoreAvailable || string.IsNullOrWhiteSpace(generation())) return false;
        confirmationGeneration = generation(); RestoreSelectionVisible = true; RestoreError = "";
        RestoreSources = Array.Empty<string>(); RestorePreview = null; RestorePath = "";
        try { restoreListTask = listRestoreSources!(); }
        catch (Exception exception) { RestoreError = exception.Message; }
        return true;
    }

    public void SelectRestorePath(string path)
    {
        if (!RestoreSelectionVisible || transitioning()) return;
        CancelPreview();
        RestorePath = path; RestorePreview = null; RestoreError = "";
        restorePreviewCancellation = new CancellationTokenSource();
        try { restorePreviewTask = readRestore!(path, restorePreviewCancellation.Token); }
        catch (Exception exception) { RestoreError = exception.Message; }
    }

    public void MoveRestoreSource(int delta)
    {
        if (!RestoreSelectionVisible || RestoreSources.Count == 0) return;
        var index = Math.Max(0, RestoreSources.ToList().IndexOf(RestorePath));
        SelectRestorePath(RestoreSources[((index + delta) % RestoreSources.Count + RestoreSources.Count) % RestoreSources.Count]);
    }

    public bool ConfirmRestore()
    {
        if (disposed || !RestoreSelectionVisible || RestoreLoading || RestorePreview == null || Current != PanelOperation.None) return false;
        var expected = confirmationGeneration;
        var preview = RestorePreview;
        CancelConfirmation();
        if (transitioning() || generation() != expected || !gate.TryBegin(PanelOperation.Restore)) return false;
        requestedGeneration = expected; queuedRestore = preview; queued = true;
        Publish(PanelOperationOutcome.Running); return true;
    }

    private void TickRestoreSelection()
    {
        if (!RestoreSelectionVisible) return;
        if (restoreListTask?.IsCompleted == true)
        {
            var task = restoreListTask; restoreListTask = null;
            try { RestoreSources = task.GetAwaiter().GetResult(); if (RestoreSources.Count > 0 && RestorePath.Length == 0) SelectRestorePath(RestoreSources[0]); }
            catch (Exception exception) { RestoreError = exception.Message; }
        }
        if (restorePreviewTask?.IsCompleted == true)
        {
            var task = restorePreviewTask; restorePreviewTask = null;
            restorePreviewCancellation?.Dispose(); restorePreviewCancellation = null;
            try { RestorePreview = task.GetAwaiter().GetResult(); }
            catch (Exception exception) { RestoreError = exception.Message; }
        }
    }

    private static void ObserveAbandoned(Task? task)
    {
        if (task != null) _ = task.ContinueWith(completed => { _ = completed.Exception; },
            System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
    private void CancelPreview()
    {
        restorePreviewCancellation?.Cancel(); restorePreviewCancellation?.Dispose(); restorePreviewCancellation = null;
        ObserveAbandoned(restorePreviewTask); restorePreviewTask = null;
    }
    public bool RequestResetConfirmation()
    {
        if (!CanStart || string.IsNullOrWhiteSpace(generation())) return false;
        confirmationGeneration = generation(); interaction.ShowResetConfirmation(); return true;
    }
    public bool CancelConfirmation()
    {
        confirmationGeneration = "";
        var restoring = RestoreSelectionVisible;
        RestoreSelectionVisible = false; RestorePreview = null; RestoreSources = Array.Empty<string>();
        ObserveAbandoned(restoreListTask); restoreListTask = null; CancelPreview();
        return interaction.CancelModal() || restoring;
    }
    public bool ConfirmReset()
    {
        if (disposed || !interaction.ResetConfirmationVisible || Current != PanelOperation.None) return false;
        var expected = confirmationGeneration;
        CancelConfirmation();
        if (transitioning() || expected.Length == 0 || generation() != expected || !gate.TryBegin(PanelOperation.Reset)) return false;
        requestedGeneration = expected; queued = true;
        Publish(PanelOperationOutcome.Running); return true;
    }

    public void Tick()
    {
        if (disposed) return;
        if ((Current == PanelOperation.Export || LastNotice?.Operation == PanelOperation.Export)
            && (transitioning() || generation() != (Current == PanelOperation.Export ? requestedGeneration : LastNotice!.GenerationId)))
            DismissExportResult();
        if (ModalVisible && (generation() != confirmationGeneration || transitioning())) CancelConfirmation();
        TickRestoreSelection();
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
                    if (Current == PanelOperation.Restore) { var preview = queuedRestore!; queuedRestore = null; _ = restore!(preview); }
                    else _ = reset();
                    ObserveReset();
                }
            }
            catch (Exception exception)
            {
                if ((Current == PanelOperation.Reset || Current == PanelOperation.Restore) && transitioning())
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
            if (exportResultDismissed)
            { Finish(PanelOperationOutcome.Success, result.Directory); return; }
            bool copied;
            var clipboardDetail = "";
            try { copied = clipboard(result.Directory); }
            catch (Exception exception)
            { copied = false; clipboardDetail = exception.GetType().Name + ": " + exception.Message; }
            Finish(copied ? PanelOperationOutcome.Success : PanelOperationOutcome.ClipboardUnavailable, result.Directory, clipboardDetail);
        }
        if ((Current == PanelOperation.Reset || Current == PanelOperation.Restore) && !queued) ObserveReset();
    }

    private void ObserveReset()
    {
        if (Current != PanelOperation.Reset && Current != PanelOperation.Restore) return;
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
            Finish(PanelOperationOutcome.Failure, detail: "The profile replacement service did not confirm completion for the requested UDS generation.");
        else if (LastNotice?.Outcome != PanelOperationOutcome.Pending)
            Publish(PanelOperationOutcome.Pending, detail: attempt?.Detail ?? "");
    }
    private void Finish(PanelOperationOutcome outcome, string path = "", string detail = "")
    {
        queuedRestore = null;
        var operation = Current;
        var notice = new PanelOperationNotice(operation, outcome, requestedGeneration, path, detail,
            generation() == requestedGeneration && !transitioning(),
            operation != PanelOperation.Export || !exportResultDismissed);
        gate.Complete(operation);
        if (notice.PresentResult) LastNotice = notice;
        Notify(notice);
    }
    private void Publish(PanelOperationOutcome outcome, string path = "", string detail = "")
    {
        LastNotice = new PanelOperationNotice(Current, outcome, requestedGeneration, path, detail,
            generation() == requestedGeneration);
        Notify(LastNotice);
    }

    // The file operation retains its gate and generation. Only its transient UI
    // and automatic clipboard action lose ownership across this boundary.
    public void DismissExportResult()
    {
        if (Current == PanelOperation.Export) exportResultDismissed = true;
        if (LastNotice?.Operation == PanelOperation.Export) LastNotice = null;
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
        disposed = true; queued = false; queuedRestore = null; CancelConfirmation();
        var task = exportTask; exportTask = null;
        if (task != null)
            _ = task.ContinueWith(completed => { _ = completed.Exception; },
                System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        if (Current != PanelOperation.None) gate.Complete(Current);
    }
}
