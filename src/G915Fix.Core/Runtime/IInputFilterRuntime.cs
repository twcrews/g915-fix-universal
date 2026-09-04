using G915Fix.Core.Configuration;

namespace G915Fix.Core.Runtime;

/// <summary>
/// Platform-hosted lifecycle for native input capture. Implementations invoke the
/// Core filters synchronously from their native callback and expose only portable
/// status to the UI.
/// </summary>
public interface IInputFilterRuntime
{
    InputFilterRuntimeSnapshot Current { get; }

    event EventHandler<InputFilterRuntimeSnapshot>? StatusChanged;

    Task<InputFilterRuntimeSnapshot> StartAsync(
        ConfigurationCompilationResult configuration,
        CancellationToken cancellationToken = default);

    Task<InputFilterRuntimeSnapshot> ApplyConfigurationAsync(
        ConfigurationCompilationResult configuration,
        CancellationToken cancellationToken = default);

    Task<InputFilterRuntimeSnapshot> StopAsync(CancellationToken cancellationToken = default);
}

public enum InputFilterRuntimeStatus
{
    Inactive,
    Starting,
    Active,
    PermissionRequired,
    Unsupported,
    Faulted
}

public sealed record InputFilterRuntimeSnapshot(
    InputFilterRuntimeStatus Status,
    bool KeyboardFilteringActive = false,
    bool MouseFilteringActive = false,
    string? Message = null)
{
    public static readonly InputFilterRuntimeSnapshot Inactive = new(InputFilterRuntimeStatus.Inactive);
}

/// <summary>Thread-safe status publisher useful to native runtime implementations.</summary>
public sealed class InputFilterRuntimeState
{
    private readonly object _sync = new();
    private InputFilterRuntimeSnapshot _current = InputFilterRuntimeSnapshot.Inactive;

    public InputFilterRuntimeSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<InputFilterRuntimeSnapshot>? Changed;

    public void Update(InputFilterRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        bool changed;
        lock (_sync)
        {
            changed = !Equals(_current, snapshot);
            _current = snapshot;
        }

        if (changed)
        {
            Changed?.Invoke(this, snapshot);
        }
    }
}
