namespace G915Fix.Core.Input;

public interface IKeyboardInputInjector
{
    void InjectKeyUp(int keyCode, bool isExtended = false, uint scanCode = 0);
}
