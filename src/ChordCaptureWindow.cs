using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Border = System.Windows.Controls.Border;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfKey = System.Windows.Input.Key;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using FormsKeys = System.Windows.Forms.Keys;

namespace SpeechToText;

// Modal chord-capture dialog. User presses chord; Escape (with no modifiers)
// cancels; chords that lack a modifier or have a modifier-only key flash red.
internal sealed class ChordCaptureWindow : Window
{
    private readonly TextBlock _hintText;
    private readonly Border _border;

    public ChordDescriptor? Result { get; private set; }

    public ChordCaptureWindow(ChordDescriptor current)
    {
        Title = "Set Hotkey";
        Width = 360;
        Height = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };
        var prompt = new TextBlock
        {
            Text = "Press the new chord…",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _border = new Border
        {
            BorderBrush = WpfBrushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Background = WpfBrushes.WhiteSmoke,
        };
        _hintText = new TextBlock
        {
            Text = current.IsValid ? $"Current: {current}" : "No chord set",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = WpfBrushes.DimGray,
        };
        _border.Child = _hintText;
        panel.Children.Add(prompt);
        panel.Children.Add(_border);
        panel.Children.Add(new TextBlock
        {
            Text = "Esc to cancel.",
            FontSize = 11,
            Foreground = WpfBrushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        });
        Content = panel;

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var pressed = e.Key == WpfKey.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        // Plain Escape (no modifiers) cancels.
        if (pressed == WpfKey.Escape && mods == WpfModifierKeys.None)
        {
            Result = null;
            DialogResult = false;
            Close();
            return;
        }

        if (IsModifierOnly(pressed))
        {
            // Holding modifiers; wait for a non-modifier key.
            _hintText.Text = FormatLive(mods) + "…";
            _hintText.Foreground = WpfBrushes.DimGray;
            return;
        }

        var chordMods = MapMods(mods);
        var formsKey = MapKey(pressed);

        if (chordMods == ChordModifiers.None || formsKey == FormsKeys.None)
        {
            FlashInvalid("Chord must include a modifier (Ctrl, Shift, Alt, or Win).");
            return;
        }

        var chord = new ChordDescriptor(chordMods, formsKey);
        if (!chord.IsValid)
        {
            FlashInvalid("Invalid chord.");
            return;
        }

        Result = chord;
        DialogResult = true;
        Close();
    }

    private void FlashInvalid(string message)
    {
        _hintText.Text = message;
        _hintText.Foreground = WpfBrushes.Firebrick;
        _border.BorderBrush = WpfBrushes.Firebrick;
    }

    private static bool IsModifierOnly(WpfKey k) => k is
        WpfKey.LeftCtrl or WpfKey.RightCtrl or
        WpfKey.LeftShift or WpfKey.RightShift or
        WpfKey.LeftAlt or WpfKey.RightAlt or
        WpfKey.LWin or WpfKey.RWin or
        WpfKey.System;

    private static ChordModifiers MapMods(WpfModifierKeys m)
    {
        var result = ChordModifiers.None;
        if (m.HasFlag(WpfModifierKeys.Control)) result |= ChordModifiers.Ctrl;
        if (m.HasFlag(WpfModifierKeys.Shift)) result |= ChordModifiers.Shift;
        if (m.HasFlag(WpfModifierKeys.Alt)) result |= ChordModifiers.Alt;
        if (m.HasFlag(WpfModifierKeys.Windows)) result |= ChordModifiers.Win;
        return result;
    }

    private static FormsKeys MapKey(WpfKey k)
    {
        int vk = KeyInterop.VirtualKeyFromKey(k);
        return vk == 0 ? FormsKeys.None : (FormsKeys)vk;
    }

    private static string FormatLive(WpfModifierKeys m)
    {
        var parts = new List<string>();
        if (m.HasFlag(WpfModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(WpfModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(WpfModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(WpfModifierKeys.Windows)) parts.Add("Win");
        return parts.Count == 0 ? "" : string.Join("+", parts) + "+";
    }
}
