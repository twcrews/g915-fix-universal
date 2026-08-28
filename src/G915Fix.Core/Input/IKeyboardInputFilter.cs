namespace G915Fix.Core.Input;

public interface IKeyboardInputFilter
{
    bool ShouldSuppress(KeyboardInputEvent inputEvent);
}
