namespace SpeechToText.Tests;

using SpeechToText;
using Xunit;

// Covers the StateChanged event the tray (slice #6) subscribes to. Verifies
// that every externally-visible state transition fires once and only once,
// and that internal-only phase moves (e.g. AwaitingTranscription queued
// behind another Dictation) don't generate spurious events.
public class StateChangedEventTests
{
    [Fact]
    public void HappyPath_FiresIdleRecordingTranscribingPastingIdle()
    {
        var h = new Harness();

        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.CompleteNext("hi");
        h.Paster.CompleteNext();

        Assert.Equal(new[]
        {
            DictationState.Recording,
            DictationState.Transcribing,
            DictationState.Pasting,
            DictationState.Idle,
        }, h.StateTransitions);
    }

    [Fact]
    public void EscDuringRecording_FiresRecordingThenIdle()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseEsc();

        Assert.Equal(new[]
        {
            DictationState.Recording,
            DictationState.Idle,
        }, h.StateTransitions);
    }

    [Fact]
    public void MaxDurationCutoff_FiresRecordingThenTranscribing()
    {
        var h = new Harness(maxDuration: TimeSpan.FromSeconds(120));
        h.Hotkey.RaiseHotkey();
        h.Clock.Advance(TimeSpan.FromSeconds(120));

        Assert.Equal(new[]
        {
            DictationState.Recording,
            DictationState.Transcribing,
        }, h.StateTransitions);
    }

    [Fact]
    public void EmptyTranscription_FiresPastingIsSkippedAndReturnsToIdle()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.CompleteNext("   ");

        Assert.Equal(new[]
        {
            DictationState.Recording,
            DictationState.Transcribing,
            DictationState.Idle,
        }, h.StateTransitions);
        Assert.Single(h.ErrorFlashes);
    }

    [Fact]
    public void FailedTranscription_FiresIdleWithoutPasting()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.FailNext(new InvalidOperationException("backend down"));

        Assert.Equal(new[]
        {
            DictationState.Recording,
            DictationState.Transcribing,
            DictationState.Idle,
        }, h.StateTransitions);
        Assert.Single(h.ErrorFlashes);
    }

    [Fact]
    public void TapDuringTranscribing_DoesNotChangeVisibleState_NoFire()
    {
        // Front is Transcribing; a new hotkey starts a *queued* Dictation in
        // Recording behind it. The visible state stays Transcribing — no event.
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);

        var beforeCount = h.StateTransitions.Count;
        h.Hotkey.RaiseHotkey(); // enqueues second Dictation in Recording
        Assert.Equal(beforeCount, h.StateTransitions.Count);
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
    }

    [Fact]
    public void QueuedDictation_FiresWhenItBecomesFrontOfQueue()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey(); // first: Recording
        h.Hotkey.RaiseHotkey(); // first: Transcribing
        h.Hotkey.RaiseHotkey(); // second queued, Recording — internal only
        h.Hotkey.RaiseHotkey(); // second stops -> AwaitingTranscription — internal only

        var beforeAdvance = h.StateTransitions.ToList();

        h.Backend.CompleteNext("first text"); // first -> Pasting; second still queued
        h.Paster.CompleteNext();              // first done; second promoted to Transcribing

        // The promotion from "first done -> second front -> Transcribing" must be
        // observable as a single Idle?->Transcribing edge (we never sit at Idle).
        var after = h.StateTransitions.Skip(beforeAdvance.Count).ToList();
        Assert.Equal(new[]
        {
            DictationState.Pasting,
            DictationState.Transcribing,
        }, after);
    }
}
