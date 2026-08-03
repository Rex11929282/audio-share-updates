using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AudioShare.Lyrics;

internal static class WindowBackdrop
{
    private const int DwmWindowAttributeSystemBackdropType = 38;
    private const int DwmSystemBackdropTransientWindow = 3;
    private const uint DisplayAffinityNone = 0;
    private const uint DisplayAffinityExcludeFromCapture = 0x00000011;

    public static bool TryEnableLightBackdrop(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var backdrop = DwmSystemBackdropTransientWindow;
        return DwmSetWindowAttribute(
            handle,
            DwmWindowAttributeSystemBackdropType,
            ref backdrop,
            sizeof(int)) == 0;
    }

    public static bool TryExcludeFromCapture(Window window)
    {
        return TrySetDisplayAffinity(window, DisplayAffinityExcludeFromCapture);
    }

    public static bool TryAllowCapture(Window window)
    {
        return TrySetDisplayAffinity(window, DisplayAffinityNone);
    }

    private static bool TrySetDisplayAffinity(Window window, uint affinity)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !SetWindowDisplayAffinity(handle, affinity))
        {
            return false;
        }

        DwmFlush();
        return true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr windowHandle, uint affinity);
}
