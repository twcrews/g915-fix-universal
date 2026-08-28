namespace G915Fix.Core.Input;

public interface IMouseInputFilter
{
    bool ShouldSuppress(MouseInputEvent inputEvent);
}
