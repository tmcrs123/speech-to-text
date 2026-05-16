using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using Grid = System.Windows.Controls.Grid;
using Orientation = System.Windows.Controls.Orientation;
using PasswordBox = System.Windows.Controls.PasswordBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using RadioButton = System.Windows.Controls.RadioButton;
using RowDefinition = System.Windows.Controls.RowDefinition;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace SpeechToText;

// First-run guided setup. Linear step machine (Welcome → Backend → Cloud or
// Local detail → Hotkey → Audio → Summary). Each step persists to ConfigStore
// on advance so a mid-flow close leaves partial progress; WizardCompleted is
// only flipped to true on Finish. Boot reopens this window until that flag is
// set.
internal sealed class WizardWindow : Window
{
    private static readonly IReadOnlyList<string> SupportedLocalModels = new[]
    {
        "tiny", "base", "small", "medium", "large-v3", "large-v3-turbo",
    };

    private enum Step
    {
        Welcome,
        Backend,
        CloudKey,
        LocalModel,
        Hotkey,
        Audio,
        Summary,
    }

    private readonly ConfigStore _config;
    private readonly KeyboardHook _hook;
    private readonly Func<HttpClient> _httpClientFactory;

    // Buffered state.
    private string _backend;
    private string? _apiKey;
    private string _localModel;
    private ChordDescriptor _chord;
    private string? _deviceId;

    private Step _current;
    private readonly Stack<Step> _history = new();

    // Persistent chrome.
    private readonly TextBlock _stepLabel;
    private readonly TextBlock _progressLabel;
    private readonly System.Windows.Controls.ContentControl _bodyHost;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Button _cancelButton;

    // Volatile per-step controls referenced by handlers.
    private PasswordBox? _apiKeyBox;
    private TextBlock? _apiKeyError;
    private ProgressBar? _apiKeyBusy;
    private ComboBox? _localModelBox;
    private TextBlock? _chordLabel;
    private ComboBox? _deviceBox;

    public WizardWindow(ConfigStore config, KeyboardHook hook, Func<HttpClient>? httpClientFactory = null)
    {
        _config = config;
        _hook = hook;
        _httpClientFactory = httpClientFactory ?? DefaultHttpClientFactory;

        _backend = config.GetTranscriptionBackend();
        _apiKey = config.GetGroqApiKey();
        _localModel = config.GetLocalModel();
        _chord = ChordDescriptor.TryParse(config.GetHotkey(), out var c)
            ? c
            : new ChordDescriptor(ChordModifiers.Ctrl | ChordModifiers.Shift, System.Windows.Forms.Keys.Space);
        _deviceId = config.GetInputDeviceId();

        Title = "SpeechToText — Setup";
        Width = 560;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        _stepLabel = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 16, 16, 0),
        };
        _progressLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(16, 2, 16, 8),
        };
        _bodyHost = new System.Windows.Controls.ContentControl { Margin = new Thickness(16, 4, 16, 8) };

        _backButton = new Button { Content = "Back", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 6, 0) };
        _backButton.Click += (_, _) => GoBack();
        _nextButton = new Button { Content = "Next", Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        _nextButton.Click += async (_, _) => await AdvanceAsync().ConfigureAwait(true);
        _cancelButton = new Button
        {
            Content = "Close",
            Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(0, 0, 6, 0),
            IsCancel = true,
        };
        _cancelButton.Click += (_, _) => Close();

        Content = BuildLayout();

        GoTo(Step.Welcome, pushHistory: false);
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 4, 12, 12),
        };
        DockPanel.SetDock(buttonBar, Dock.Bottom);
        buttonBar.Children.Add(_cancelButton);
        buttonBar.Children.Add(_backButton);
        buttonBar.Children.Add(_nextButton);
        root.Children.Add(buttonBar);

        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(_stepLabel);
        header.Children.Add(_progressLabel);
        root.Children.Add(header);

        root.Children.Add(_bodyHost);
        return root;
    }

    private void GoTo(Step step, bool pushHistory = true)
    {
        if (pushHistory) _history.Push(_current);
        _current = step;
        _bodyHost.Content = BuildBody(step);
        _stepLabel.Text = TitleFor(step);
        _progressLabel.Text = ProgressFor(step);
        _backButton.IsEnabled = _history.Count > 0 && step != Step.Welcome;
        _nextButton.Content = step == Step.Summary ? "Finish" : "Next";
        _nextButton.IsEnabled = true;
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        var prev = _history.Pop();
        _current = prev;
        _bodyHost.Content = BuildBody(prev);
        _stepLabel.Text = TitleFor(prev);
        _progressLabel.Text = ProgressFor(prev);
        _backButton.IsEnabled = _history.Count > 0 && prev != Step.Welcome;
        _nextButton.Content = prev == Step.Summary ? "Finish" : "Next";
        _nextButton.IsEnabled = true;
    }

    private static string TitleFor(Step step) => step switch
    {
        Step.Welcome => "Welcome",
        Step.Backend => "Choose a transcription backend",
        Step.CloudKey => "Enter your Groq API key",
        Step.LocalModel => "Choose a local model",
        Step.Hotkey => "Pick a hotkey",
        Step.Audio => "Pick an input device",
        Step.Summary => "All set",
        _ => "",
    };

    private string ProgressFor(Step step)
    {
        int total = 5; // Welcome / Backend / (Cloud or Local) / Hotkey / Audio + Summary
        int index = step switch
        {
            Step.Welcome => 1,
            Step.Backend => 2,
            Step.CloudKey or Step.LocalModel => 3,
            Step.Hotkey => 4,
            Step.Audio => 5,
            Step.Summary => 6,
            _ => 1,
        };
        return step == Step.Summary ? "Review" : $"Step {index} of {total}";
    }

    private UIElement BuildBody(Step step) => step switch
    {
        Step.Welcome => BuildWelcomeBody(),
        Step.Backend => BuildBackendBody(),
        Step.CloudKey => BuildCloudKeyBody(),
        Step.LocalModel => BuildLocalModelBody(),
        Step.Hotkey => BuildHotkeyBody(),
        Step.Audio => BuildAudioBody(),
        Step.Summary => BuildSummaryBody(),
        _ => new TextBlock(),
    };

    private UIElement BuildWelcomeBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "SpeechToText turns spoken audio into text and pastes it into whatever app has focus. "
                 + "Tap a keyboard chord to start recording, speak, tap again to stop — your transcript "
                 + "appears wherever the cursor was.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "This short setup will pick a transcription backend, capture a hotkey, and choose your microphone.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
        });
        return panel;
    }

    private UIElement BuildBackendBody()
    {
        var panel = new StackPanel();

        var cloud = new RadioButton
        {
            Content = "Cloud (Groq) — fast, requires an API key",
            IsChecked = _backend == "cloud",
            Margin = new Thickness(0, 0, 0, 8),
            GroupName = "wizard-backend",
        };
        cloud.Checked += (_, _) => _backend = "cloud";

        var local = new RadioButton
        {
            Content = "Local (Whisper) — runs on this machine, no key needed",
            IsChecked = _backend == "local",
            Margin = new Thickness(0, 0, 0, 8),
            GroupName = "wizard-backend",
        };
        local.Checked += (_, _) => _backend = "local";

        panel.Children.Add(cloud);
        panel.Children.Add(local);
        panel.Children.Add(new TextBlock
        {
            Text = "You can change this later in Settings, but the app must restart for a backend change to take effect.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return panel;
    }

    private UIElement BuildCloudKeyBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Paste your Groq API key. It's stored encrypted on this machine (DPAPI, current user).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _apiKeyBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 4) };
        if (!string.IsNullOrEmpty(_apiKey)) _apiKeyBox.Password = _apiKey;
        _apiKeyBox.PasswordChanged += (_, _) => _apiKey = _apiKeyBox.Password;
        panel.Children.Add(_apiKeyBox);

        _apiKeyError = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(_apiKeyError);

        _apiKeyBusy = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(_apiKeyBusy);

        var help = new TextBlock
        {
            Text = "Next will check the key with a tiny live request before saving it.",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(help);

        return panel;
    }

    private UIElement BuildLocalModelBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Pick a Whisper model size. Larger models are more accurate but slower and need more memory.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _localModelBox = new ComboBox { ItemsSource = SupportedLocalModels, SelectedItem = _localModel };
        _localModelBox.SelectionChanged += (_, _) =>
        {
            if (_localModelBox.SelectedItem is string s) _localModel = s;
        };
        panel.Children.Add(_localModelBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Model download (with progress and integrity check) ships in a follow-up slice. "
                 + "Your selection is recorded; the download will run on first use.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return panel;
    }

    private UIElement BuildHotkeyBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Default is Ctrl+Shift+Space. Click 'Set hotkey…' to pick something else.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

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
            Content = "Set hotkey…",
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        setButton.Click += (_, _) =>
        {
            var dialog = new ChordCaptureWindow(_chord) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is { } captured)
            {
                _chord = captured;
                if (_chordLabel != null) _chordLabel.Text = _chord.ToString();
            }
        };
        panel.Children.Add(setButton);
        return panel;
    }

    private UIElement BuildAudioBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Choose the microphone to record from. '(System default)' follows whatever Windows is set to.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _deviceBox = new ComboBox();
        _deviceBox.DropDownOpened += (_, _) => RefreshDevices();
        _deviceBox.SelectionChanged += (_, _) =>
        {
            if (_deviceBox.SelectedItem is AudioDeviceItem item)
                _deviceId = item.Id == AudioDevices.SystemDefaultId ? null : item.Id;
        };
        RefreshDevices();
        panel.Children.Add(_deviceBox);
        return panel;
    }

    private void RefreshDevices()
    {
        if (_deviceBox == null) return;
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

    private UIElement BuildSummaryBody()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Review the choices below and click Finish to start using SpeechToText.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddSummaryRow(grid, 0, "Backend", _backend == "cloud" ? "Cloud (Groq)" : "Local (Whisper)");
        if (_backend == "cloud")
            AddSummaryRow(grid, 1, "API key", "•••• (validated)");
        else
            AddSummaryRow(grid, 1, "Local model", _localModel);
        AddSummaryRow(grid, 2, "Hotkey", _chord.ToString());
        AddSummaryRow(grid, 3, "Input device",
            string.IsNullOrEmpty(_deviceId) ? "(System default)" : DescribeDevice(_deviceId));
        panel.Children.Add(grid);

        panel.Children.Add(new TextBlock
        {
            Text = "You can revisit any of these from the tray icon's Settings… menu.",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private static void AddSummaryRow(Grid grid, int row, string label, string value)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelText = new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 2, 8, 2) };
        Grid.SetRow(labelText, row);
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        var valueText = new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
    }

    private static string DescribeDevice(string id)
    {
        foreach (var d in AudioDevices.EnumerateInputs())
            if (d.Id == id) return d.FriendlyName;
        return id;
    }

    private async Task AdvanceAsync()
    {
        switch (_current)
        {
            case Step.Welcome:
                GoTo(Step.Backend);
                break;

            case Step.Backend:
                _config.SetTranscriptionBackend(_backend);
                GoTo(_backend == "cloud" ? Step.CloudKey : Step.LocalModel);
                break;

            case Step.CloudKey:
                if (string.IsNullOrWhiteSpace(_apiKey))
                {
                    ShowKeyError("Paste your Groq API key to continue.");
                    return;
                }
                await ValidateAndPersistKeyAsync(_apiKey).ConfigureAwait(true);
                break;

            case Step.LocalModel:
                _config.SetLocalModel(_localModel);
                GoTo(Step.Hotkey);
                break;

            case Step.Hotkey:
                _config.SetHotkey(_chord.ToString());
                _hook.Chord = _chord;
                GoTo(Step.Audio);
                break;

            case Step.Audio:
                _config.SetInputDeviceId(_deviceId);
                GoTo(Step.Summary);
                break;

            case Step.Summary:
                _config.SetWizardCompleted(true);
                DialogResult = true;
                Close();
                break;
        }
    }

    private async Task ValidateAndPersistKeyAsync(string apiKey)
    {
        if (_apiKeyError == null || _apiKeyBusy == null || _apiKeyBox == null) return;
        _apiKeyError.Visibility = Visibility.Collapsed;
        _apiKeyBusy.Visibility = Visibility.Visible;
        _nextButton.IsEnabled = false;
        _backButton.IsEnabled = false;
        _apiKeyBox.IsEnabled = false;

        try
        {
            using var http = _httpClientFactory();
            var result = await GroqApiKeyValidator
                .ValidateAsync(apiKey, http)
                .ConfigureAwait(true);

            if (result.IsOk)
            {
                _config.SetGroqApiKey(apiKey);
                GoTo(Step.Hotkey);
                return;
            }

            ShowKeyError(result.Outcome switch
            {
                GroqApiKeyValidator.Outcome.Unauthorized =>
                    "Groq rejected this key. Double-check the value and try again.",
                GroqApiKeyValidator.Outcome.NetworkError =>
                    "Couldn't reach Groq. Check your network and try again.",
                _ => $"Unexpected response from Groq (HTTP {result.StatusCode}). Try again or pick a different key.",
            });
        }
        finally
        {
            _apiKeyBusy.Visibility = Visibility.Collapsed;
            _nextButton.IsEnabled = true;
            _backButton.IsEnabled = _history.Count > 0;
            _apiKeyBox.IsEnabled = true;
        }
    }

    private void ShowKeyError(string message)
    {
        if (_apiKeyError == null) return;
        _apiKeyError.Text = message;
        _apiKeyError.Visibility = Visibility.Visible;
    }

    private static HttpClient DefaultHttpClientFactory() => new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly record struct AudioDeviceItem(string Id, string Name);
}
