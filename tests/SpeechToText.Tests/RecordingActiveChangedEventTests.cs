namespace SpeechToText.Tests;

using SpeechToText;
using Xunit;

// Covers RecordingActiveChanged — the Recording Indicator's signal. Distinct
// from StateChanged: it tracks the 0↔>0 edge of dictations in Phase.Recording,
// not the front-of-queue phase. See ADR-0005 for why the two surfaces
// deliberately diverge during queued-recording windows.
public class RecordingActiveChangedEventTests
{
    [Fact]
    public void HappyPath_FiresTrueOnRecordingStart_FalseOnRecordingStop()
    {
        var h = new Harness();

        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.CompleteNext("hi");
        h.Paster.CompleteNext();

        Assert.Equal(new[] { true, false }, h.RecordingActiveTransitions);
    }

    [Fact]
    public void EscDuringRecording_FiresFalse()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseEsc();

        Assert.Equal(new[] { true, false }, h.RecordingActiveTransitions);
    }

    [Fact]
    public void MaxDurationCutoff_FiresFalse()
    {
        var h = new Harness(maxDuration: TimeSpan.FromSeconds(120));
        h.Hotkey.RaiseHotkey();
        h.Clock.Advance(TimeSpan.FromSeconds(120));

        Assert.Equal(new[] { true, false }, h.RecordingActiveTransitions);
    }

    [Fact]
    public void QueuedSecondRecording_FiresTrueAgain_WhileStateChangedStaysSilent()
    {
        // The exact scenario ADR-0005 calls out: while A is Transcribing, the
        // user taps the Hotkey and starts a second recording. The Indicator
        // must show (RecordingActiveChanged fires true), while the tray icon
        // stays on Transcribing (StateChanged does not fire).
        var h = new Harness();
        h.Hotkey.RaiseHotkey(); // A enters Recording
        h.Hotkey.RaiseHotkey(); // A → Transcribing; Recording count back to 0

        Assert.Equal(new[] { true, false }, h.RecordingActiveTransitions);
        var stateBefore = h.StateTransitions.Count;

        h.Hotkey.RaiseHotkey(); // B enters Recording behind A

        Assert.Equal(new[] { true, false, true }, h.RecordingActiveTransitions);
        Assert.Equal(stateBefore, h.StateTransitions.Count); // tray icon unchanged

        h.Hotkey.RaiseHotkey(); // B stops → AwaitingTranscription; Recording count 0

        Assert.Equal(new[] { true, false, true, false }, h.RecordingActiveTransitions);
        Assert.Equal(stateBefore, h.StateTransitions.Count);
    }

    [Fact]
    public void NoSpuriousFires_DuringTranscribingOrPasting()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.CompleteNext("text");
        h.Paster.CompleteNext();

        // Exactly one rising edge and one falling edge for the whole lifecycle.
        Assert.Equal(new[] { true, false }, h.RecordingActiveTransitions);
    }

    [Fact]
    public void FailedTranscription_DoesNotEmitRecordingActiveEdge()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.FailNext(new InvalidOperationException("boom"));

        // The recording already ended on the second hotkey tap; a later
        // transcription failure changes the front-of-queue phase but not the
        // any-Recording count, so no extra edge.
        Assert.Equal(new[] { true, false }, h.RecordingActiveTransitions);
    }
}
