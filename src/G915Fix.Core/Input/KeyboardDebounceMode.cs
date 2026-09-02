namespace G915Fix.Core.Input;

public enum KeyboardDebounceMode
{
    /// <summary>Suppress the duplicate press and its matching release.</summary>
    BlockRepress,

    /// <summary>Defer a release until it is known not to be switch bounce.</summary>
    BlockRelease
}
