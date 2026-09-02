namespace G915Fix.Core.Input;

public interface IReleaseScheduler : IDisposable
{
    void Schedule(HidKeyboardUsage key, TimeSpan delay, Action<HidKeyboardUsage> callback);

    void Cancel();
}

public interface IReleaseSchedulerFactory
{
    IReleaseScheduler Create();
}
