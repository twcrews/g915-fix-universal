namespace G915Fix.Core.Input;

public interface IReleaseScheduler : IDisposable
{
    void Schedule(int keyCode, TimeSpan delay, Action<int> callback);

    void Cancel();
}

public interface IReleaseSchedulerFactory
{
    IReleaseScheduler Create();
}
