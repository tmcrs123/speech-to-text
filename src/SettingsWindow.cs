using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using Grid = System.Windows.Controls.Grid;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using PasswordBox = System.Windows.Controls.PasswordBox;
using RadioButton = System.Windows.Controls.RadioButton;
using StackPanel = System.Windows.Controls.StackPanel;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FormsKeys = System.Windows.Forms.Keys;

namespace SpeechToText;

// WPF settings window opened from the tray. Four tabs: Backend / Hotkey /
// Audio / About. Explicit Save/Cancel — edits are buffered locally and only
// committed to ConfigStore on Save. Hotkey re-binding fires through to the
// live KeyboardHook so the new chord takes effect in-session.
internal sealed class SettingsWindow : Window
{
    private static readonly IReadOnlyList<string> SupportedLocalModels = new[]
    {
        "tiny", "base", "small", "medium", "large-v3", "large-v3-turbo",
    };

    private readonly ConfigStore _config;
    private readonly KeyboardHook _hook;
    private readonly ISoundPlayer _sounds;
    private readonly LoginAutoStart? _autoStart;

    // Buffered state — written to ConfigStore only on Save.
    private string _backend;
    private string _localModel;
    private string? _apiKey;
    private ChordDescriptor _chord;
    private string? _deviceId;
    private int _maxSeconds;
    private bool _muteSounds;
    private bool _autoStartOnLogin;
    private bool _showRecordingIndicator;

    // Controls referenced after construction.
    private RadioButton _cloudRadio = null!;
    private RadioButton _localRadio = null!;
    private PasswordBox _apiKeyBox = null!;
    private TextBox _apiKeyVisibleBox = null!;
    private ToggleButton _showKeyButton = null!;
    private ComboBox _localModelBox = null!;
    private TextBlock _chordLabel = null!;
    private ComboBox _deviceBox = null!;
    private CheckBox _muteCheck = null!;
    private CheckBox _autoStartCheck = null!;
    private CheckBox _recordingIndicatorCheck = null!;
    private TextBox _maxSecondsBox = null!;
    private StackPanel _cloudPanel = null!;
    private StackPanel _localPanel = null!;

    public SettingsWindow(ConfigStore config, KeyboardHook hook, ISoundPlayer sounds, LoginAutoStart? autoStart = null)
    {
        _config = config;
        _hook = hook;
        _sounds = sounds;
        _autoStart = autoStart;

        _backend = config.GetTranscriptionBackend();
        _localModel = config.GetLocalModel();
        _apiKey = config.GetGroqApiKey();
        _chord = ChordDescriptor.TryParse(config.GetHotkey(), out var c) ? c : new ChordDescriptor(ChordModifiers.Ctrl | ChordModifiers.Shift, System.Windows.Forms.Keys.Space);
        _deviceId = config.GetInputDeviceId();
        _maxSeconds = config.GetMaxRecordingSeconds();
        _muteSounds = !config.GetStartStopSoundsEnabled();
        _autoStartOnLogin = config.GetAutoStartOnLogin();
        _showRecordingIndicator = config.GetShowRecordingIndicator();

        Title = "SpeechToText — Settings";
        Width = 520;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel();
        root.Children.Add(BuildButtonBar());
        root.Children.Add(BuildTabs());
        Content = root;
    }

    private TabControl BuildTabs()
    {
        var tabs = new TabControl { Margin = new Thickness(8) };
        tabs.Items.Add(new TabItem { Header = "Backend", Content = BuildBackendTab() });
        tabs.Items.Add(new TabItem { Header = "Hotkey", Content = BuildHotkeyTab() });
        tabs.Items.Add(new TabItem { Header = "Audio", Content = BuildAudioTab() });
        tabs.Items.Add(new TabItem { Header = "Usage", Content = BuildUsageTab() });
        tabs.Items.Add(new TabItem { Header = "About", Content = BuildAboutTab() });
        return tabs;
    }

    private UIElement BuildBackendTab()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };

        _cloudRadio = new RadioButton { Content = "Cloud (Groq)", IsChecked = _backend == "cloud", Margin = new Thickness(0, 0, 0, 6) };
        _localRadio = new RadioButton { Content = "Local (Whisper)", IsChecked = _backend == "local", Margin = new Thickness(0, 0, 0, 12) };
        _cloudRadio.Checked += (_, _) => { _backend = "cloud"; UpdateBackendPanels(); };
        _localRadio.Checked += (_, _) => { _backend = "local"; UpdateBackendPanels(); };
        panel.Children.Add(_cloudRadio);
        panel.Children.Add(_localRadio);

        _cloudPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        _cloudPanel.Children.Add(new TextBlock { Text = "Groq API key", Margin = new Thickness(0, 0, 0, 4) });

        var keyRow = new Grid();
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _apiKeyBox = new PasswordBox { Margin = new Thickness(0, 0, 6, 0) };
        if (!string.IsNullOrEmpty(_apiKey)) _apiKeyBox.Password = _apiKey;
        _apiKeyBox.PasswordChanged += (_, _) => { if (_showKeyButton.IsChecked != true) _apiKey = _apiKeyBox.Password; };

        _apiKeyVisibleBox = new TextBox { Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
        _apiKeyVisibleBox.TextChanged += (_, _) => { if (_showKeyButton.IsChecked == true) _apiKey = _apiKeyVisibleBox.Text; };

        var keyHostPanel = new Grid();
        keyHostPanel.Children.Add(_apiKeyBox);
        keyHostPanel.Children.Add(_apiKeyVisibleBox);
        Grid.SetColumn(keyHostPanel, 0);
        keyRow.Children.Add(keyHostPanel);

        _showKeyButton = new ToggleButton { Content = "Show", Padding = new Thickness(8, 2, 8, 2) };
        _showKeyButton.Checked += (_, _) =>
        {
            _apiKeyVisibleBox.Text = _apiKeyBox.Password;
            _apiKey = _apiKeyVisibleBox.Text;
            _apiKeyVisibleBox.Visibility = Visibility.Visible;
            _apiKeyBox.Visibility = Visibility.Collapsed;
        };
        _showKeyButton.Unchecked += (_, _) =>
        {
            _apiKeyBox.Password = _apiKeyVisibleBox.Text;
            _apiKey = _apiKeyBox.Password;
            _apiKeyBox.Visibility = Visibility.Visible;
            _apiKeyVisibleBox.Visibility = Visibility.Collapsed;
        };
        Grid.SetColumn(_showKeyButton, 1);
        keyRow.Children.Add(_showKeyButton);

        _cloudPanel.Children.Add(keyRow);
        _cloudPanel.Children.Add(new TextBlock
        {
            Text = "Stored encrypted at rest (DPAPI, current user).",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0),
        });
        panel.Children.Add(_cloudPanel);

        _localPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        _localPanel.Children.Add(new TextBlock { Text = "Whisper model size", Margin = new Thickness(0, 0, 0, 4) });
        _localModelBox = new ComboBox { ItemsSource = SupportedLocalModels, SelectedItem = _localModel };
        _localModelBox.SelectionChanged += (_, _) =>
        {
            if (_localModelBox.SelectedItem is string s) _localModel = s;
        };
        _localPanel.Children.Add(_localModelBox);
        _localPanel.Children.Add(new TextBlock
        {
            Text = "Model download UX ships in a later slice. This selection is recorded for now.",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(_localPanel);

        UpdateBackendPanels();
        return panel;
    }

    private void UpdateBackendPanels()
    {
        if (_cloudPanel == null || _localPanel == null) return;
        _cloudPanel.Visibility = _backend == "cloud" ? Visibility.Visible : Visibility.Collapsed;
        _localPanel.Visibility = _backend == "local" ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildHotkeyTab()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Current hotkey", Margin = new Thickness(0, 0, 0, 4) });

        _chordLabel = new TextBlock
        {
            Text = _chord.ToString(),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        panel.Children.Add(_chordLabel);

        var setButton = new Button
        {
            Content = "Set Hotkey…",
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        setButton.Click += (_, _) => CaptureChord();
        panel.Children.Add(setButton);

        panel.Children.Add(new TextBlock
        {
            Text = "Pressing the chord starts and stops a dictation. Default Ctrl+Shift+Space.",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private void CaptureChord()
    {
        var dialog = new ChordCaptureWindow(_chord) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } captured)
        {
            _chord = captured;
            _chordLabel.Text = _chord.ToString();
        }
    }

    private UIElement BuildAudioTab()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Input device", Margin = new Thickness(0, 0, 0, 4) });

        _deviceBox = new ComboBox();
        _deviceBox.DropDownOpened += (_, _) => RefreshDevices();
        _deviceBox.SelectionChanged += (_, _) =>
        {
            if (_deviceBox.SelectedItem is AudioDeviceItem item)
                _deviceId = item.Id == AudioDevices.SystemDefaultId ? null : item.Id;
        };
        RefreshDevices();
        panel.Children.Add(_deviceBox);

        panel.Children.Add(new TextBlock
        {
            Text = "List refreshes each time you open the dropdown — plug in a headset and reopen to see it.",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        _recordingIndicatorCheck = new CheckBox
        {
            Content = "Show Recording Indicator during recording",
            IsChecked = _showRecordingIndicator,
            Margin = new Thickness(0, 16, 0, 0),
            ToolTip = "When off, the recording indicator overlay is hidden. Takes effect on the next recording.",
        };
        _recordingIndicatorCheck.Checked += (_, _) => _showRecordingIndicator = true;
        _recordingIndicatorCheck.Unchecked += (_, _) => _showRecordingIndicator = false;
        panel.Children.Add(_recordingIndicatorCheck);

        return panel;
    }

    private void RefreshDevices()
    {
        var items = new List<AudioDeviceItem>
        {
            new(AudioDevices.SystemDefaultId, "(System default)"),
        };
        foreach (var d in AudioDevices.EnumerateInputs())
            items.Add(new AudioDeviceItem(d.Id, d.FriendlyName));

        var previouslySelected = _deviceId ?? AudioDevices.SystemDefaultId;
        _deviceBox.ItemsSource = items;
        _deviceBox.DisplayMemberPath = nameof(AudioDeviceItem.Name);
        var matchIndex = items.FindIndex(i => i.Id == previouslySelected);
        _deviceBox.SelectedIndex = matchIndex >= 0 ? matchIndex : 0;
    }

    private UIElement BuildAboutTab()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
        panel.Children.Add(new TextBlock
        {
            Text = $"SpeechToText {version}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "A keyboard chord turns spoken audio into text and pastes it into the focused app.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var repoLink = new Hyperlink(new Run("Repository on GitHub"))
        {
            NavigateUri = new Uri("https://github.com/tmcrs123/speech-to-text"),
        };
        repoLink.RequestNavigate += OnHyperlinkNavigate;
        panel.Children.Add(new TextBlock(repoLink) { Margin = new Thickness(0, 0, 0, 4) });

        var adrLink = new Hyperlink(new Run("Architecture decisions (ADRs)"))
        {
            NavigateUri = new Uri("https://github.com/tmcrs123/speech-to-text/tree/main/docs/adr"),
        };
        adrLink.RequestNavigate += OnHyperlinkNavigate;
        panel.Children.Add(new TextBlock(adrLink) { Margin = new Thickness(0, 0, 0, 12) });

        return panel;
    }

    private UIElement BuildUsageTab()
    {
        const int MinSeconds = 5;
        const int MaxSeconds = 600;

        var panel = new StackPanel { Margin = new Thickness(12) };

        _muteCheck = new CheckBox
        {
            Content = "Mute start/stop sounds",
            IsChecked = _muteSounds,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _muteCheck.Checked += (_, _) => _muteSounds = true;
        _muteCheck.Unchecked += (_, _) => _muteSounds = false;
        panel.Children.Add(_muteCheck);

        _autoStartCheck = new CheckBox
        {
            Content = "Start with Windows (launch on login, minimised to tray)",
            IsChecked = _autoStartOnLogin,
            Margin = new Thickness(0, 0, 0, 16),
        };
        _autoStartCheck.Checked += (_, _) => _autoStartOnLogin = true;
        _autoStartCheck.Unchecked += (_, _) => _autoStartOnLogin = false;
        panel.Children.Add(_autoStartCheck);

        panel.Children.Add(new TextBlock
        {
            Text = "Max recording seconds",
            Margin = new Thickness(0, 0, 0, 4),
        });

        var spinRow = new StackPanel { Orientation = Orientation.Horizontal };
        var down = new Button { Content = "−", Width = 28, Height = 24, Padding = new Thickness(0) };
        var up = new Button { Content = "+", Width = 28, Height = 24, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0) };
        _maxSecondsBox = new TextBox
        {
            Text = _maxSeconds.ToString(),
            Width = 70,
            Height = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 4, 0),
        };
        _maxSecondsBox.LostFocus += (_, _) => NormaliseMaxSecondsBox(MinSeconds, MaxSeconds);
        _maxSecondsBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(_maxSecondsBox.Text, out int v) && v >= MinSeconds && v <= MaxSeconds)
                _maxSeconds = v;
        };
        down.Click += (_, _) => SetMaxSecondsBox(_maxSeconds - 5, MinSeconds, MaxSeconds);
        up.Click += (_, _) => SetMaxSecondsBox(_maxSeconds + 5, MinSeconds, MaxSeconds);

        spinRow.Children.Add(_maxSecondsBox);
        spinRow.Children.Add(down);
        spinRow.Children.Add(up);
        panel.Children.Add(spinRow);

        panel.Children.Add(new TextBlock
        {
            Text = $"Recording auto-stops at this duration. Min {MinSeconds}s, max {MaxSeconds}s.",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        return panel;
    }

    private void SetMaxSecondsBox(int desired, int min, int max)
    {
        _maxSeconds = Math.Clamp(desired, min, max);
        _maxSecondsBox.Text = _maxSeconds.ToString();
    }

    private void NormaliseMaxSecondsBox(int min, int max)
    {
        if (!int.TryParse(_maxSecondsBox.Text, out int v))
            v = _maxSeconds;
        SetMaxSecondsBox(v, min, max);
    }

    private static void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
        e.Handled = true;
    }

    private UIElement BuildButtonBar()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 4, 8, 8),
        };
        DockPanel.SetDock(bar, Dock.Bottom);

        var save = new Button
        {
            Content = "Save",
            Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(0, 0, 6, 0),
            IsDefault = true,
        };
        save.Click += (_, _) => OnSave();

        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(16, 4, 16, 4),
            IsCancel = true,
        };
        cancel.Click += (_, _) => Close();

        bar.Children.Add(save);
        bar.Children.Add(cancel);
        return bar;
    }

    private void OnSave()
    {
        if (_backend == "cloud" && string.IsNullOrWhiteSpace(_apiKey))
        {
            MessageBox.Show(this,
                "The Cloud backend requires a Groq API key.\n\nEither paste a key, or switch to the Local backend.",
                "API key required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool changed =
            _backend != _config.GetTranscriptionBackend()
            || _localModel != _config.GetLocalModel()
            || (!string.IsNullOrWhiteSpace(_apiKey) && _apiKey != _config.GetGroqApiKey())
            || _chord.ToString() != _config.GetHotkey()
            || _deviceId != _config.GetInputDeviceId()
            || !_muteSounds != _config.GetStartStopSoundsEnabled()
            || _maxSeconds != _config.GetMaxRecordingSeconds()
            || _autoStartOnLogin != _config.GetAutoStartOnLogin();

        _config.SetTranscriptionBackend(_backend);
        _config.SetLocalModel(_localModel);
        if (!string.IsNullOrWhiteSpace(_apiKey))
            _config.SetGroqApiKey(_apiKey);
        _config.SetHotkey(_chord.ToString());
        _config.SetInputDeviceId(_deviceId);
        _config.SetStartStopSoundsEnabled(!_muteSounds);
        _config.SetMaxRecordingSeconds(_maxSeconds);
        _config.SetAutoStartOnLogin(_autoStartOnLogin);
        _config.SetShowRecordingIndicator(_showRecordingIndicator);

        _hook.Chord = _chord;
        _sounds.Muted = _muteSounds;
        // Reflect the (possibly new) auto-start flag into the HKCU Run entry
        // immediately so it takes effect on the very next login — no restart
        // needed (and the visible restart prompt below is for other settings).
        _autoStart?.Apply(_autoStartOnLogin);

        Close();

        if (changed) RestartApp();
    }

    private static void RestartApp()
    {
        MessageBox.Show(
            "Settings saved. The app will now restart to apply the changes.",
            "SpeechToText — restarting",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }); }
            catch (Exception ex) { Trace.TraceWarning($"Restart failed: {ex.Message}"); }
        }
        System.Windows.Forms.Application.Exit();
    }

    private readonly record struct AudioDeviceItem(string Id, string Name);
}
