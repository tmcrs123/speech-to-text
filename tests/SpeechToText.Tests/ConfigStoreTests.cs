namespace SpeechToText.Tests;

using System.IO;
using SpeechToText;
using Xunit;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpeechToText.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void GetMaxRecordingSeconds_OnMissingFile_ReturnsDefault120()
    {
        var store = new ConfigStore(_path);

        Assert.Equal(120, store.GetMaxRecordingSeconds());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void SetMaxRecordingSeconds_ThenGet_ReturnsValue_AcrossInstances()
    {
        new ConfigStore(_path).SetMaxRecordingSeconds(45);

        Assert.Equal(45, new ConfigStore(_path).GetMaxRecordingSeconds());
    }

    [Fact]
    public void MissingFile_ReturnsDefaults_ForAllNonSecretSettings()
    {
        var store = new ConfigStore(_path);

        Assert.Equal("cloud", store.GetTranscriptionBackend());
        Assert.Equal("large-v3-turbo", store.GetLocalModel());
        Assert.Equal("Ctrl+Shift+Space", store.GetHotkey());
        Assert.True(store.GetStartStopSoundsEnabled());
        Assert.True(store.GetAutoStartOnLogin());
        Assert.Null(store.GetInputDeviceId());
    }

    [Fact]
    public void RoundTrip_NonSecretSettings_AcrossInstances()
    {
        var writer = new ConfigStore(_path);
        writer.SetTranscriptionBackend("local");
        writer.SetLocalModel("medium");
        writer.SetHotkey("Ctrl+Alt+D");
        writer.SetStartStopSoundsEnabled(false);
        writer.SetAutoStartOnLogin(false);
        writer.SetInputDeviceId("device-42");

        var reader = new ConfigStore(_path);
        Assert.Equal("local", reader.GetTranscriptionBackend());
        Assert.Equal("medium", reader.GetLocalModel());
        Assert.Equal("Ctrl+Alt+D", reader.GetHotkey());
        Assert.False(reader.GetStartStopSoundsEnabled());
        Assert.False(reader.GetAutoStartOnLogin());
        Assert.Equal("device-42", reader.GetInputDeviceId());
    }

    [Fact]
    public void SetInputDeviceId_Null_RoundTrips()
    {
        var writer = new ConfigStore(_path);
        writer.SetInputDeviceId("device-42");
        writer.SetInputDeviceId(null);

        Assert.Null(new ConfigStore(_path).GetInputDeviceId());
    }

    [Fact]
    public void GetSchemaVersion_OnMissingFile_Returns1()
    {
        Assert.Equal(1, new ConfigStore(_path).GetSchemaVersion());
    }

    [Fact]
    public void SchemaVersion_IsWrittenOnSave_AndReadOnLoad()
    {
        new ConfigStore(_path).SetMaxRecordingSeconds(30);

        var raw = File.ReadAllText(_path);
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        Assert.Equal(1, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(1, new ConfigStore(_path).GetSchemaVersion());
    }

    [Fact]
    public void ConcurrentWrites_DoNotCorruptFile()
    {
        var store = new ConfigStore(_path);
        // Seed the file so all writers race on the same target.
        store.SetMaxRecordingSeconds(1);

        const int writerCount = 16;
        const int writesPerWriter = 25;
        var start = new System.Threading.ManualResetEventSlim(false);
        var threads = new System.Threading.Thread[writerCount];
        for (int i = 0; i < writerCount; i++)
        {
            int writerIndex = i;
            threads[i] = new System.Threading.Thread(() =>
            {
                start.Wait();
                for (int j = 0; j < writesPerWriter; j++)
                {
                    store.SetMaxRecordingSeconds(writerIndex * 1000 + j);
                }
            });
            threads[i].Start();
        }
        start.Set();
        foreach (var t in threads) t.Join();

        var raw = File.ReadAllText(_path);
        using var doc = System.Text.Json.JsonDocument.Parse(raw); // throws if corrupt
        var seconds = doc.RootElement.GetProperty("MaxRecordingSeconds").GetInt32();
        Assert.InRange(seconds, 0, (writerCount - 1) * 1000 + (writesPerWriter - 1));
    }

    [Fact]
    public void CorruptFile_ReturnsDefaults_NoThrow()
    {
        File.WriteAllText(_path, "{ this is not valid json :::");

        var store = new ConfigStore(_path);

        Assert.Equal(120, store.GetMaxRecordingSeconds());
        Assert.Equal("cloud", store.GetTranscriptionBackend());
        Assert.Null(store.GetGroqApiKey());
    }

    [Fact]
    public void GetGroqApiKey_OnMissingFile_ReturnsNull()
    {
        Assert.Null(new ConfigStore(_path).GetGroqApiKey());
    }

    [Fact]
    public void SetGroqApiKey_ThenGet_ReturnsOriginalPlaintext_AcrossInstances()
    {
        const string secret = "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";

        new ConfigStore(_path).SetGroqApiKey(secret);

        Assert.Equal(secret, new ConfigStore(_path).GetGroqApiKey());
    }

    [Fact]
    public void GetWizardCompleted_OnMissingFile_ReturnsFalse()
    {
        Assert.False(new ConfigStore(_path).GetWizardCompleted());
    }

    [Fact]
    public void SetWizardCompleted_True_RoundTripsAcrossInstances()
    {
        new ConfigStore(_path).SetWizardCompleted(true);

        Assert.True(new ConfigStore(_path).GetWizardCompleted());
    }

    [Fact]
    public void SetGroqApiKey_DoesNotWritePlaintextToDisk()
    {
        const string secret = "gsk_PlAiNtExTsHoUlDnEvErAppEaR_1234567890";

        new ConfigStore(_path).SetGroqApiKey(secret);

        var rawBytes = File.ReadAllBytes(_path);
        var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);
        Assert.DoesNotContain(secret, rawText);
    }
}
