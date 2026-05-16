namespace SpeechToText.Tests;

using SpeechToText;
using System.Windows.Forms;
using Xunit;

public class ChordDescriptorTests
{
    [Fact]
    public void ParsesDefaultChord()
    {
        Assert.True(ChordDescriptor.TryParse("Ctrl+Shift+Space", out var chord));
        Assert.True(chord.IsValid);
        Assert.Equal(Keys.Space, chord.Key);
        Assert.True(chord.Modifiers.HasFlag(ChordModifiers.Ctrl));
        Assert.True(chord.Modifiers.HasFlag(ChordModifiers.Shift));
        Assert.False(chord.Modifiers.HasFlag(ChordModifiers.Alt));
        Assert.False(chord.Modifiers.HasFlag(ChordModifiers.Win));
    }

    [Theory]
    [InlineData("Ctrl+Shift+Space")]
    [InlineData("Ctrl+Alt+F1")]
    [InlineData("Shift+Win+A")]
    public void RoundTripsThroughToString(string text)
    {
        Assert.True(ChordDescriptor.TryParse(text, out var chord));
        Assert.Equal(text, chord.ToString());
    }

    [Fact]
    public void ToStringEmitsCanonicalModifierOrder()
    {
        // Input order is irrelevant — output is always Ctrl, Shift, Alt, Win.
        Assert.True(ChordDescriptor.TryParse("Win+Shift+A", out var chord));
        Assert.Equal("Shift+Win+A", chord.ToString());
    }

    [Fact]
    public void DigitKeysFormatNumerically()
    {
        Assert.True(ChordDescriptor.TryParse("Ctrl+5", out var chord));
        Assert.Equal("Ctrl+5", chord.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Space")]                  // no modifier
    [InlineData("Ctrl+")]                  // trailing plus
    [InlineData("Ctrl+Shift")]             // modifier-only chord
    [InlineData("NotAKey+Space")]          // unknown modifier
    [InlineData("Ctrl+Ctrl+Space")]        // duplicate modifier
    [InlineData("Ctrl+NotAKey")]           // unknown key
    public void RejectsInvalid(string? text)
    {
        Assert.False(ChordDescriptor.TryParse(text, out _));
    }

    [Fact]
    public void EscIsAValidKeyName()
    {
        Assert.True(ChordDescriptor.TryParse("Ctrl+Esc", out var chord));
        Assert.Equal(Keys.Escape, chord.Key);
    }

    [Fact]
    public void IsValidRejectsModifierOnlyKey()
    {
        var chord = new ChordDescriptor(ChordModifiers.Ctrl, Keys.ShiftKey);
        Assert.False(chord.IsValid);
    }

    [Fact]
    public void IsValidRequiresAtLeastOneModifier()
    {
        var chord = new ChordDescriptor(ChordModifiers.None, Keys.Space);
        Assert.False(chord.IsValid);
    }
}
