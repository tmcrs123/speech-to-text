namespace SpeechToText.Tests;

using SpeechToText;
using Xunit;

public class DictationOrchestratorTests
{
    [Fact]
    public void HappyPath_TapRecordTapTranscribePaste_ReturnsToIdle()
    {
        var h = new Harness();
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);

        h.Hotkey.RaiseHotkey();
        Assert.Equal(DictationState.Recording, h.Orchestrator.CurrentState);
        Assert.Equal(1, h.Audio.StartCalls);

        h.Hotkey.RaiseHotkey();
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
        Assert.Equal(1, h.Audio.StopCalls);
        Assert.Equal(1, h.Backend.PendingCount);

        h.Backend.CompleteNext("hello world");
        Assert.Equal(DictationState.Pasting, h.Orchestrator.CurrentState);
        Assert.Equal(new[] { "hello world" }, h.Paster.Pasted);

        h.Paster.CompleteNext();
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);
        Assert.Empty(h.ErrorFlashes);
    }

    [Fact]
    public void EscDuringRecording_AbortsAndReturnsToIdle()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();

        bool consumed = h.Hotkey.RaiseEsc();

        Assert.True(consumed);
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);
        Assert.Equal(1, h.Audio.AbortCalls);
        Assert.Equal(0, h.Backend.PendingCount);
        Assert.Empty(h.Paster.Pasted);
    }

    [Fact]
    public void EscWhenIdle_NotConsumed()
    {
        var h = new Harness();
        Assert.False(h.Hotkey.RaiseEsc());
    }

    [Fact]
    public void EscWhenTranscribing_NotConsumed()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);

        Assert.False(h.Hotkey.RaiseEsc());
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
    }

    [Fact]
    public void EscWhenPasting_NotConsumed()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.CompleteNext("hi");
        Assert.Equal(DictationState.Pasting, h.Orchestrator.CurrentState);

        Assert.False(h.Hotkey.RaiseEsc());
        Assert.Equal(DictationState.Pasting, h.Orchestrator.CurrentState);
    }

    [Fact]
    public void MaxDurationElapsed_StopsRecordingAndTranscribes()
    {
        var h = new Harness(maxDuration: TimeSpan.FromSeconds(120));
        h.Hotkey.RaiseHotkey();

        h.Clock.Advance(TimeSpan.FromSeconds(120));

        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
        Assert.Equal(1, h.Audio.StopCalls);
        Assert.Equal(1, h.Backend.PendingCount);
    }

    [Fact]
    public void MaxDurationTimer_Cancelled_OnNormalStop()
    {
        var h = new Harness(maxDuration: TimeSpan.FromSeconds(120));
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);

        // Advancing past the original 120s must not retrigger anything since the
        // dictation already left Recording.
        h.Clock.Advance(TimeSpan.FromSeconds(200));

        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
        Assert.Equal(1, h.Backend.PendingCount);
    }

    [Fact]
    public void QueueDuringTranscribing_QueuedRunsAfterInflightCompletes()
    {
        var h = new Harness();

        // Dictation A: tap, tap → Transcribing
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);

        // During A's Transcribing: start B's Recording.
        h.Hotkey.RaiseHotkey();
        Assert.Equal(2, h.Audio.StartCalls);
        // Public state is still front's (A) Transcribing.
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);

        // Stop B's recording — its audio is captured and B awaits transcription.
        h.Hotkey.RaiseHotkey();
        Assert.Equal(2, h.Audio.StopCalls);
        // Only A's transcription is in flight.
        Assert.Equal(1, h.Backend.PendingCount);

        // Complete A's transcription → A pastes.
        h.Backend.CompleteNext("first");
        Assert.Equal(new[] { "first" }, h.Paster.Pasted);
        Assert.Equal(DictationState.Pasting, h.Orchestrator.CurrentState);

        // Finish A's paste → B is now front, starts transcribing.
        h.Paster.CompleteNext();
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
        Assert.Equal(1, h.Backend.PendingCount);

        // Complete B's transcription → B pastes.
        h.Backend.CompleteNext("second");
        Assert.Equal(new[] { "first", "second" }, h.Paster.Pasted);

        h.Paster.CompleteNext();
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);
    }

    [Fact]
    public void QueueDuringPasting_QueuedRunsAfterInflightPasteCompletes()
    {
        var h = new Harness();

        // A: tap, tap, complete transcription → A in Pasting.
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        h.Backend.CompleteNext("first");
        Assert.Equal(DictationState.Pasting, h.Orchestrator.CurrentState);

        // During A's Pasting: tap → B starts Recording in parallel.
        h.Hotkey.RaiseHotkey();
        Assert.Equal(2, h.Audio.StartCalls);
        Assert.Equal(DictationState.Pasting, h.Orchestrator.CurrentState);

        // Tap to stop B's recording — B awaits transcription.
        h.Hotkey.RaiseHotkey();
        Assert.Equal(2, h.Audio.StopCalls);
        Assert.Equal(0, h.Backend.PendingCount);

        // Complete A's paste → B starts transcribing.
        h.Paster.CompleteNext();
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
        Assert.Equal(1, h.Backend.PendingCount);

        h.Backend.CompleteNext("second");
        Assert.Equal(new[] { "first", "second" }, h.Paster.Pasted);
        h.Paster.CompleteNext();
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);
    }

    [Fact]
    public void EmptyTranscription_NoPaste_FlashesError()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();

        h.Backend.CompleteNext("   ");

        Assert.Empty(h.Paster.Pasted);
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);
        Assert.Single(h.ErrorFlashes);
    }

    [Fact]
    public void EmptyTranscriptionString_NoPaste_FlashesError()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();

        h.Backend.CompleteNext("");

        Assert.Empty(h.Paster.Pasted);
        Assert.Single(h.ErrorFlashes);
    }

    [Fact]
    public void FailedTranscription_NoPaste_FlashesError()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();

        h.Backend.FailNext(new HttpRequestException("network down"));

        Assert.Empty(h.Paster.Pasted);
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);
        Assert.Single(h.ErrorFlashes);
    }

    [Fact]
    public void HwndCapturedAtRecordingEnd_NotAtHotkeyPress_NorAtPasteTime()
    {
        var h = new Harness();
        h.Targeter.ForegroundHwnd = new IntPtr(0xAAA);

        // Tap to start Recording — HWND must NOT be captured yet.
        h.Hotkey.RaiseHotkey();
        Assert.Empty(h.Targeter.Captured);

        // Move focus before stopping recording.
        h.Targeter.ForegroundHwnd = new IntPtr(0xBBB);

        // Tap to stop — HWND captured NOW.
        h.Hotkey.RaiseHotkey();
        Assert.Equal(new[] { new IntPtr(0xBBB) }, h.Targeter.Captured);

        // Move focus again before paste happens.
        h.Targeter.ForegroundHwnd = new IntPtr(0xCCC);

        h.Backend.CompleteNext("text");

        // RestoreFocus must use the HWND captured at Recording-end, not the current foreground.
        Assert.Equal(new[] { new IntPtr(0xBBB) }, h.Targeter.Restored);
    }

    [Fact]
    public void HwndCaptureTiming_PerQueuedDictation()
    {
        var h = new Harness();
        h.Targeter.ForegroundHwnd = new IntPtr(0x111);

        // A: record, stop (captures 0x111), enters Transcribing.
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        Assert.Equal(new[] { new IntPtr(0x111) }, h.Targeter.Captured);

        // Move focus, start B.
        h.Targeter.ForegroundHwnd = new IntPtr(0x222);
        h.Hotkey.RaiseHotkey();

        // Move focus again before B's recording ends.
        h.Targeter.ForegroundHwnd = new IntPtr(0x333);
        h.Hotkey.RaiseHotkey();

        Assert.Equal(new[] { new IntPtr(0x111), new IntPtr(0x333) }, h.Targeter.Captured);

        // Complete and paste both.
        h.Backend.CompleteNext("a");
        h.Paster.CompleteNext();
        h.Backend.CompleteNext("b");
        h.Paster.CompleteNext();

        Assert.Equal(new[] { new IntPtr(0x111), new IntPtr(0x333) }, h.Targeter.Restored);
    }

    [Fact]
    public void HotkeyDuringRecording_StopsRecording_NotQueueNewOne()
    {
        var h = new Harness();
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        Assert.Equal(1, h.Audio.StartCalls);
        Assert.Equal(1, h.Audio.StopCalls);
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);
    }

    [Fact]
    public void QueuedDictationFails_FlashesError_DoesNotBlockSubsequent()
    {
        var h = new Harness();
        // A → Transcribing
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();
        // B → AwaitingTranscription
        h.Hotkey.RaiseHotkey();
        h.Hotkey.RaiseHotkey();

        // Fail A: error flash, queue advances to B.
        h.Backend.FailNext(new InvalidOperationException("boom"));
        Assert.Single(h.ErrorFlashes);
        Assert.Equal(DictationState.Transcribing, h.Orchestrator.CurrentState);

        h.Backend.CompleteNext("ok");
        Assert.Equal(new[] { "ok" }, h.Paster.Pasted);
        h.Paster.CompleteNext();
        Assert.Equal(DictationState.Idle, h.Orchestrator.CurrentState);
    }
}
