namespace G915Fix.Core.Input;

/// <summary>
/// Determines whether the active input backend can filter input for the currently
/// focused application.
/// </summary>
public interface IForegroundInputAccessDetector
{
    ForegroundInputAccessResult GetCurrentStatus();
}
