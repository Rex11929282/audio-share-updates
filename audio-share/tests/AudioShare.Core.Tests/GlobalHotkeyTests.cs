using System.Windows.Input;
using AudioShare.App;

namespace AudioShare.Core.Tests;

public sealed class GlobalHotkeyTests
{
    [Fact]
    public void ToggleShareDefault_IsControlAltS()
    {
        var binding = FlowCastHotkeys.ToggleShare;

        Assert.Equal(HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt, binding.Modifiers);
        Assert.Equal(Key.S, binding.Key);
        Assert.Equal("Ctrl+Alt+S", binding.DisplayText);
        Assert.True(binding.IsValid);
    }

    [Fact]
    public void ToggleWindowDefault_IsControlAltH()
    {
        var binding = FlowCastHotkeys.ToggleWindow;

        Assert.Equal("Ctrl+Alt+H", binding.DisplayText);
        Assert.True(binding.IsValid);
    }

    [Fact]
    public void Win32Modifiers_MatchTheWindowsModifierFlags()
    {
        // MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8.
        var binding = new HotkeyBinding(
            HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt | HotkeyModifierKeys.Shift,
            Key.F9);

        Assert.Equal(0x2u | 0x1u | 0x4u, binding.Win32Modifiers);
    }

    [Fact]
    public void VirtualKey_TranslatesFromTheWpfKey()
    {
        var binding = new HotkeyBinding(HotkeyModifierKeys.Control, Key.A);

        // Virtual-key code for 'A' is 0x41.
        Assert.Equal(0x41u, binding.VirtualKey);
    }

    [Fact]
    public void Binding_WithoutModifier_IsNotValid()
    {
        var binding = new HotkeyBinding(HotkeyModifierKeys.None, Key.S);

        Assert.False(binding.IsValid);
    }

    [Fact]
    public void Binding_WithoutKey_IsNotValid()
    {
        var binding = new HotkeyBinding(HotkeyModifierKeys.Control, Key.None);

        Assert.False(binding.IsValid);
    }
}
