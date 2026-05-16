namespace SpeechToText.Tests;

using SpeechToText;
using System.Windows.Forms;
using Xunit;

// These tests verify the in-process state of KeyboardHook.Chord without
// installing the global Windows hook. The actual OS-level key matching is
// only exercised manually (it requires real keyboard input).
public class KeyboardHookChordTests
{
    [Fact]
    public void ConstructorRejectsInvalidChord()
    {
        Assert.Throws<ArgumentException>(() =>
            new KeyboardHook(new ChordDescriptor(ChordModifiers.None, Keys.Space)));
    }

    [Fact]
    public void ChordSetterRejectsInvalidChord()
    {
        using var hook = new KeyboardHook(new ChordDescriptor(ChordModifiers.Ctrl, Keys.Space));
        Assert.Throws<ArgumentException>(() =>
            hook.Chord = new ChordDescriptor(ChordModifiers.None, Keys.Space));
    }

    [Fact]
    public void ChordSetterUpdatesPropertyAtomically()
    {
        var initial = new ChordDescriptor(ChordModifiers.Ctrl | ChordModifiers.Shift, Keys.Space);
        var next = new ChordDescriptor(ChordModifiers.Ctrl | ChordModifiers.Alt, Keys.F1);
        using var hook = new KeyboardHook(initial);

        Assert.Equal(initial, hook.Chord);
        hook.Chord = next;
        Assert.Equal(next, hook.Chord);
    }
}
