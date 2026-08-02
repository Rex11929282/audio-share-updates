using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AudioShare.Lyrics;

internal static class WindowBackdrop
{
    private const int DwmWindowAttributeSystemBackdropType = 38;
    private const int DwmSystemBackdropTransientWindow = 3;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
