using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;

namespace AudioShare.App;

/// <summary>
/// Modifier keys for a global hotkey. Values match the Win32 <c>MOD_*</c> flags so the
/// enum can be passed straight to <c>RegisterHotKey</c>.
/// </summary>
[Flags]
internal enum HotkeyModifierKeys
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// A system-wide hotkey: a set of modifier keys plus a single key. Pure value type with no
/// Win32 dependency beyond the key/modifier translation, so it can be unit tested directly.
/// </summary>
internal sealed record HotkeyBinding(HotkeyModifierKeys Modifiers, Key Key)
{
    public uint Win32Modifiers => (uint)Modifiers;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>A hotkey is only usable when it has a real key and at least one modifier.</summary>
    public bool IsValid => Key != Key.None && VirtualKey != 0 && Modifiers != HotkeyModifierKeys.None;

    /// <summary>Human-readable form such as <c>Ctrl+Alt+S</c>, modifiers in a stable order.</summary>
    public string DisplayText
    {
        get
        {
            var builder = new StringBuilder();
            if (Modifiers.HasFlag(HotkeyModifierKeys.Control))
            {
                builder.Append("Ctrl+");
            }

            if (Modifiers.HasFlag(HotkeyModifierKeys.Alt))
            {
                builder.Append("Alt+");
            }

            if (Modifiers.HasFlag(HotkeyModifierKeys.Shift))
            {
                builder.Append("Shift+");
            }

            if (Modifiers.HasFlag(HotkeyModifierKeys.Win))
            {
                builder.Append("Win+");
            }

            builder.Append(Key.ToString());
            return builder.ToString();
        }
    }
}

/// <summary>
/// The default FlowCast global hotkeys. Kept in one place so preferences copy and UI can
/// describe them consistently.
/// </summary>
internal static class FlowCastHotkeys
{
    public static readonly HotkeyBinding ToggleShare =
        new(HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt, Key.S);

    public static readonly HotkeyBinding ToggleWindow =
        new(HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt, Key.H);
}

/// <summary>
/// Registers system-wide hotkeys against a window handle and dispatches them to actions.
/// Registration failures (for example a key already owned by another app) are reported per
/// binding and never crash the host window.
/// </summary>
internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource source;
    private readonly Dictionary<int, Action> actions = [];
    private int nextId = 1;
    private bool disposed;

    public GlobalHotkeyService(HwndSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.source.AddHook(WndProc);
    }

    /// <summary>
    /// Attempts to register a hotkey. Returns <c>true</c> when Windows accepts it; a
    /// <c>false</c> result (invalid binding or a conflict) simply leaves the action unbound.
    /// </summary>
    public bool TryRegister(HotkeyBinding binding, Action action)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(action);
        if (disposed || !binding.IsValid)
        {
            return false;
        }

        var id = nextId++;
        if (!RegisterHotKey(source.Handle, id, binding.Win32Modifiers | ModNoRepeat, binding.VirtualKey))
        {
            return false;
        }

        actions[id] = action;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var id in actions.Keys)
        {
            UnregisterHotKey(source.Handle, id);
        }

        actions.Clear();
        source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
