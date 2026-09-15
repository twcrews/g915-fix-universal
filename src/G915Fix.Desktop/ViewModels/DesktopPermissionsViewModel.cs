using System.Collections.ObjectModel;
using System.Windows.Input;
using G915Fix.Core.Permissions;
using G915Fix.Desktop.Infrastructure;

namespace G915Fix.Desktop.ViewModels;

/// <summary>Portable presentation model for a host's OS permission window.</summary>
public sealed class DesktopPermissionsViewModel : ObservableObject
{
    private readonly IPermissionService _permissions;
    private string? _message;

    public DesktopPermissionsViewModel(IPermissionService permissions)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
    }

    public ObservableCollection<PermissionEntryViewModel> Permissions { get; } = [];

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Refreshes the displayed requirements and returns whether any still need consent.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PermissionRequirement> requirements = await _permissions.GetRequiredPermissionsAsync(cancellationToken);
        var existing = Permissions.ToDictionary(permission => permission.Id, StringComparer.Ordinal);
        var refreshed = new List<PermissionEntryViewModel>(requirements.Count);

        foreach (PermissionRequirement requirement in requirements)
        {
            if (!existing.Remove(requirement.Id, out PermissionEntryViewModel? entry))
            {
                entry = new PermissionEntryViewModel(requirement, RequestPermissionAsync);
            }
            else
            {
                entry.Update(requirement);
            }

            refreshed.Add(entry);
        }

        Permissions.Clear();
        foreach (PermissionEntryViewModel entry in refreshed)
        {
            Permissions.Add(entry);
        }

        return Permissions.Any(permission => permission.NeedsPermission);
    }

    private async Task RequestPermissionAsync(PermissionEntryViewModel entry)
    {
        PermissionRequestResult result = await _permissions.RequestPermissionAsync(entry.Id);
        Message = result.Message;
        await RefreshAsync();
    }
}

/// <summary>A permission requirement with UI-specific state and its request action.</summary>
public sealed class PermissionEntryViewModel : ObservableObject
{
    private PermissionRequirement _permission;

    internal PermissionEntryViewModel(
        PermissionRequirement permission,
        Func<PermissionEntryViewModel, Task> requestPermissionAsync)
    {
        _permission = permission;
        RequestCommand = new AsyncCommand(
            () => requestPermissionAsync(this),
            () => NeedsPermission);
    }

    public string Id => _permission.Id;
    public string DisplayName => _permission.DisplayName;
    public string StatusText => IsGranted ? "Allowed" : "Not allowed";
    public bool IsGranted => _permission.Status == PermissionStatus.Granted;
    public bool NeedsPermission => _permission.Status is not (PermissionStatus.Granted or PermissionStatus.NotRequired);
    public ICommand RequestCommand { get; }

    internal void Update(PermissionRequirement permission)
    {
        _permission = permission;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsGranted));
        OnPropertyChanged(nameof(NeedsPermission));
        if (RequestCommand is AsyncCommand command)
        {
            command.RaiseCanExecuteChanged();
        }
    }
}
