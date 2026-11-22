using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit;

/// <summary>
/// The Revit API only works on Revit's own thread, inside an API context.
/// Everything that comes from the MCP server or the approval pane is queued here
/// and run when Revit raises the ExternalEvent (as soon as it is idle).
/// </summary>
internal sealed class RevitDispatcher : IExternalEventHandler
{
    private readonly ConcurrentQueue<IWorkItem> _queue = new();
    private readonly ExternalEvent _event;

    /// <summary>Must be constructed inside an API context (OnStartup).</summary>
    public RevitDispatcher() => _event = ExternalEvent.Create(this);

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs work on the Revit thread. If Revit doesn't get to it within the timeout
    /// (a modal dialog is open, a long command is running) it is cancelled and never runs.
    /// </summary>
    public async Task<T> InvokeAsync<T>(Func<UIApplication, T> work, TimeSpan? timeout = null)
    {
        var item = new WorkItem<T>(work);
        _queue.Enqueue(item);
        var request = _event.Raise();
        if (request == ExternalEventRequest.Denied)
        {
            item.TryCancel();
            throw new InvalidOperationException("Revit refused the request. Close any open Revit dialog and try again.");
        }

        var finished = await Task.WhenAny(item.Task, Task.Delay(timeout ?? DefaultTimeout)).ConfigureAwait(false);
        if (finished != item.Task && item.TryCancel())
        {
            throw new TimeoutException("Revit is busy (a dialog, edit mode or long command may be open). Finish it and try again.");
        }
        // Either it finished, or it had already started running when the timeout hit: wait for it.
        return await item.Task.ConfigureAwait(false);
    }

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var item)) item.Run(app);
    }

    public string GetName() => "Allegro Drafter";

    private interface IWorkItem
    {
        void Run(UIApplication app);
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private const int Queued = 0, Running = 1, Cancelled = 2;
        private readonly Func<UIApplication, T> _work;
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state;

        public WorkItem(Func<UIApplication, T> work) => _work = work;

        public Task<T> Task => _tcs.Task;

        public bool TryCancel()
        {
            if (Interlocked.CompareExchange(ref _state, Cancelled, Queued) != Queued) return false;
            _tcs.TrySetCanceled();
            return true;
        }

        public void Run(UIApplication app)
        {
            if (Interlocked.CompareExchange(ref _state, Running, Queued) != Queued) return;
            try
            {
                _tcs.TrySetResult(_work(app));
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }
    }
}
