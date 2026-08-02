using Microsoft.UI.Dispatching;

namespace RaysLoader.Services;

public sealed class DispatcherQueueService(DispatcherQueue dispatcherQueue) : IUiDispatcher
{
    public void Enqueue(Action action)
    {
        if (dispatcherQueue.HasThreadAccess) action();
        else if (!dispatcherQueue.TryEnqueue(() => action())) throw new InvalidOperationException("The UI dispatcher is shutting down.");
    }
}
