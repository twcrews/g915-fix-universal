namespace G915Fix.Core.Input;

public readonly record struct MouseButton(int Code)
{
    public static readonly MouseButton Left = new(0);

    public static readonly MouseButton Right = new(1);

    public static readonly MouseButton Middle = new(2);

    public static readonly MouseButton X1 = new(3);

    public static readonly MouseButton X2 = new(4);
}
