using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace SpeechToText;

// Passive, click-through, always-on-top window that reports transcription status
// ("Transcribing…", then "Transcription finished") near the bottom-centre of the
// focused monitor. Modelled on RecordingIndicatorWindow and, like it, never takes
// focus (WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW) —
// this is load-bearing so it can't disturb WindowTargeter's paste target (ADR-0005).
//
// Semantics: front-of-queue (drives off DictationOrchestrator.StateChanged), because
// the popup reports the transcription that is completing. See ADR-0007.
internal sealed class StatusPopupWindow : Window
{
    private const double WindowWidthDip = 200;
    private const double WindowHeightDip = 36;
    private const double BottomMarginDip = 80;
    private const int FinishedDismissMs = 2000;

    private TextBlock _label = null!;
    private DispatcherTimer? _dismissTimer;
    private ConfigStore? _config;
    private DictationState _prevState = DictationState.Idle;

    public StatusPopupWindow()
    {
        Title = "SpeechToText Status Popup";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = WindowWidthDip;
        Height = WindowHeightDip;
        Focusable = false;
        // Park off-screen until first show so the initial creation has no visible flash.
        Left = -32000;
        Top = -32000;
        Content = BuildGlyph();
        // Force HWND creation now so the extended styles are stamped before any Show().
        new WindowInteropHelper(this).EnsureHandle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        // TRANSPARENT + LAYERED: mouse events pass through to the window beneath.
        // NOACTIVATE: the window never steals focus from the target app.
        // TOOLWINDOW: keeps the popup out of Alt-Tab.
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    // Subscribe to orchestrator. Called once after construction; StateChanged and
    // ErrorFlashRequested fire from worker threads so we marshal to the dispatcher.
    public void Attach(DictationOrchestrator orchestrator, ConfigStore config)
    {
        _config = config;
        orchestrator.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(DictationState next)
    {
        if (Dispatcher.CheckAccess()) ApplyState(next);
        else Dispatcher.BeginInvoke(new Action(() => ApplyState(next)));
    }

    private void ApplyState(DictationState next)
    {
        var prev = _prevState;
        _prevState = next;

        if (_config?.GetShowStatusPopup() == false)
        {
            HideNow();
            return;
        }

        switch (next)
        {
            case DictationState.Transcribing:
                CancelDismissTimer();
                ShowMessage("Transcribing…");
                break;

            case DictationState.Pasting:
                // Brief phase between transcribe and idle; keep whatever is shown.
                break;

            case DictationState.Idle:
                // Success always passes through Pasting (both output modes). A failure
                // leaves Transcribing straight to Idle, so this guard skips "finished".
                if (prev == DictationState.Pasting)
                    ShowFinished();
                else
                    HideNow();
                break;

            case DictationState.Recording:
            default:
                // The recording indicator owns the recording phase.
                HideNow();
                break;
        }
    }

    private void ShowFinished()
    {
        ShowMessage("Transcription finished");
        _dismissTimer ??= CreateDismissTimer();
        _dismissTimer.Stop();
        _dismissTimer.Start();
    }

    private DispatcherTimer CreateDismissTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FinishedDismissMs) };
        timer.Tick += (_, _) =>
        {
            CancelDismissTimer();
            HideNow();
        };
        return timer;
    }

    private void CancelDismissTimer() => _dismissTimer?.Stop();

    private void ShowMessage(string text)
    {
        _label.Text = text;
        PositionAtFocusedMonitor();
        if (!IsVisible) Show();
    }

    private void HideNow()
    {
        CancelDismissTimer();
        if (IsVisible) Hide();
    }

    private void PositionAtFocusedMonitor()
    {
        IntPtr fg = GetForegroundWindow();
        var screen = fg != IntPtr.Zero
            ? WinFormsScreen.FromHandle(fg)
            : WinFormsScreen.PrimaryScreen;
        if (screen == null) return;

        // System.Windows.Forms.Screen.WorkingArea is in physical pixels. Convert to
        // DIPs so WPF's Left/Top land correctly on per-monitor-DPI systems.
        var source = PresentationSource.FromVisual(this)
            ?? HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = fromDevice.Transform(new System.Windows.Point(
            screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(
            screen.WorkingArea.Right, screen.WorkingArea.Bottom));

        Left = (topLeft.X + bottomRight.X - WindowWidthDip) / 2;
        Top = bottomRight.Y - WindowHeightDip - BottomMarginDip;
    }

    private UIElement BuildGlyph()
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(180, 24, 24, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(140, 60, 130, 220)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
        };
        _label = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(230, 230, 230, 230)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        border.Child = _label;
        return border;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
