using Reshot.Core.Input;
using Xunit;

namespace Reshot.Core.Tests;

public class HotkeyDefinitionTests
{
    [Fact]
    public void Parses_PrtScn_with_no_modifiers()
    {
        var def = HotkeyDefinition.Parse("PrtScn");

        Assert.Equal(0x2Cu, def.VirtualKey);
        Assert.Equal("PrtScn", def.KeyName);
        // NoRepeat is always added; no user modifiers.
        Assert.False(def.Modifiers.HasFlag(HotkeyModifiers.Control));
        Assert.False(def.Modifiers.HasFlag(HotkeyModifiers.Alt));
        Assert.True(def.Modifiers.HasFlag(HotkeyModifiers.NoRepeat));
    }

    [Theory]
    [InlineData("printscreen")]
    [InlineData("PRINT")]
    [InlineData("Prnt")]
    [InlineData("  snapshot ")]
    public void PrtScn_aliases_and_casing_normalize(string input)
    {
        var def = HotkeyDefinition.Parse(input);
        Assert.Equal(0x2Cu, def.VirtualKey);
        Assert.Equal("PrtScn", def.KeyName);
    }

    [Fact]
    public void Parses_modifier_combo()
    {
        var def = HotkeyDefinition.Parse("Ctrl+Shift+A");

        Assert.True(def.Modifiers.HasFlag(HotkeyModifiers.Control));
        Assert.True(def.Modifiers.HasFlag(HotkeyModifiers.Shift));
        Assert.False(def.Modifiers.HasFlag(HotkeyModifiers.Alt));
        Assert.Equal((uint)'A', def.VirtualKey);
        Assert.Equal("A", def.KeyName);
    }

    [Fact]
    public void Parses_function_keys()
    {
        var def = HotkeyDefinition.Parse("Alt+F4");
        Assert.True(def.Modifiers.HasFlag(HotkeyModifiers.Alt));
        Assert.Equal(0x73u, def.VirtualKey); // VK_F4
        Assert.Equal("F4", def.KeyName);
    }

    [Fact]
    public void Parses_digits()
    {
        var def = HotkeyDefinition.Parse("Win+1");
        Assert.True(def.Modifiers.HasFlag(HotkeyModifiers.Win));
        Assert.Equal((uint)'1', def.VirtualKey);
    }

    [Fact]
    public void ToString_roundtrips_to_canonical_form()
    {
        var def = HotkeyDefinition.Parse("shift+ctrl+prtscn");
        Assert.Equal("Ctrl+Shift+PrtScn", def.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]           // modifier with no main key
    [InlineData("Ctrl+A+B")]       // two main keys
    [InlineData("Ctrl+Nope")]      // unknown key
    public void Rejects_invalid_input(string input)
    {
        Assert.False(HotkeyDefinition.TryParse(input, out _, out var error));
        Assert.NotEmpty(error);
    }
}
