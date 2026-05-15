namespace SpeechToText;

internal interface IHotkeyListener
{
    event Action? HotkeyPressed;

    // Invoked on Esc-keydown. Return true to consume Esc (block from focused window),
    // false to pass through. Orchestrator returns true only when in Recording.
    Func<bool>? EscPressed { get; set; }
}
