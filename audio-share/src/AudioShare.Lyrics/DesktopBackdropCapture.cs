using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AudioShare.Lyrics;

internal sealed class DesktopBackdropCapture
{
    private const int CapturePaddingPixels = 64;
    private const int SourceCopy = 0x00CC0020;
    private const int CaptureLayeredWindows = 0x40000000;
    private const int VirtualScreenLeft = 76;
    private const int VirtualScreenTop = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;

    public string? Capture(Window window)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero || !GetWindowRect(windowHandle, out var windowBounds))
        {
            return null;
        }

        var bounds = GetCaptureBounds(windowBounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var desktopDc = GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            return null;
        }

        var memoryDc = CreateCompatibleDC(desktopDc);
        var bitmap = memoryDc == IntPtr.Zero
            ? IntPtr.Zero
            : CreateCompatibleBitmap(desktopDc, bounds.Width, bounds.Height);
        var previousObject = IntPtr.Zero;

        try
        {
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            previousObject = SelectObject(memoryDc, bitmap);
            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    desktopDc,
                    bounds.Left,
                    bounds.Top,
                    SourceCopy | CaptureLayeredWindows))
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = 86 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(stream.ToArray())}";
        }
        finally
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                SelectObject(memoryDc, previousObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    private static NativeRectangle GetCaptureBounds(NativeRectangle windowBounds)
    {
        var virtualLeft = GetSystemMetrics(VirtualScreenLeft);
        var virtualTop = GetSystemMetrics(VirtualScreenTop);
        var virtualRight = virtualLeft + GetSystemMetrics(VirtualScreenWidth);
        var virtualBottom = virtualTop + GetSystemMetrics(VirtualScreenHeight);

        return new NativeRectangle
        {
            Left = Math.Max(virtualLeft, windowBounds.Left - CapturePaddingPixels),
            Top = Math.Max(virtualTop, windowBounds.Top - CapturePaddingPixels),
            Right = Math.Min(virtualRight, windowBounds.Right + CapturePaddingPixels),
            Bottom = Math.Min(virtualBottom, windowBounds.Bottom + CapturePaddingPixels)
        };
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destinationDeviceContext,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDeviceContext,
        int sourceX,
        int sourceY,
        int rasterOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
}
