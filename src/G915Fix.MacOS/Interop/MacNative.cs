using System.Runtime.InteropServices;

namespace G915Fix.MacOS.Interop;

internal static class MacNative
{
    internal const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    internal const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    internal const string LibSystem = "/usr/lib/libSystem.B.dylib";

    internal const ulong EventTapLocationHid = 0;
    internal const uint EventTapPlacementHeadInsert = 0;
    internal const uint EventTapOptionsDefault = 0;
    internal const uint Utf8StringEncoding = 0x08000100;
    internal const long KeyboardEventKeyCode = 9;
    internal const long EventSourceUnixProcessId = 41;
    internal const long EventSourceUserData = 42;

    internal const nuint KeyDown = 10;
    internal const nuint KeyUp = 11;
    internal const nuint FlagsChanged = 12;
    internal const nuint LeftMouseDown = 1;
    internal const nuint LeftMouseUp = 2;
    internal const nuint RightMouseDown = 3;
    internal const nuint RightMouseUp = 4;
    internal const nuint OtherMouseDown = 25;
    internal const nuint OtherMouseUp = 26;
    internal const nuint EventTapDisabledByTimeout = 0xfffffffe;
    internal const nuint EventTapDisabledByUserInput = 0xffffffff;
    internal const long MouseEventButtonNumber = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr EventTapCallback(IntPtr proxy, nuint eventType, IntPtr @event, IntPtr userInfo);

    [DllImport(ApplicationServices)]
    internal static extern IntPtr CGEventTapCreate(
        ulong tap,
        uint place,
        uint options,
        ulong eventsOfInterest,
        EventTapCallback callback,
        IntPtr userInfo);

    [DllImport(ApplicationServices)]
    internal static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(ApplicationServices)]
    internal static extern ulong CGEventGetTimestamp(IntPtr @event);

    [DllImport(ApplicationServices)]
    internal static extern long CGEventGetIntegerValueField(IntPtr @event, long field);

    [DllImport(ApplicationServices)]
    internal static extern void CGEventSetIntegerValueField(IntPtr @event, long field, long value);

    [DllImport(ApplicationServices)]
    internal static extern ulong CGEventGetFlags(IntPtr @event);

    [DllImport(ApplicationServices)]
    internal static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(ApplicationServices)]
    internal static extern void CGEventPost(ulong tap, IntPtr @event);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CGPreflightListenEventAccess();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CGRequestListenEventAccess();

    [DllImport(CoreFoundation)]
    internal static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);

    [DllImport(CoreFoundation)]
    internal static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [DllImport(CoreFoundation)]
    internal static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopRemoveSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopRun();

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopStop(IntPtr runLoop);

    [DllImport(CoreFoundation)]
    internal static extern void CFRelease(IntPtr cf);


    [DllImport(LibSystem)]
    internal static extern int mach_timebase_info(out MachTimebaseInfo info);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MachTimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }
}
