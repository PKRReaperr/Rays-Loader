namespace RaysLoader.Services;

public interface IUiDispatcher
{
    void Enqueue(Action action);
}

public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Enqueue(Action action) => action();
}
