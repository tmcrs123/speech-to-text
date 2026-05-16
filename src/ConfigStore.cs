namespace SpeechToText;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class ConfigStore
{
    private const string DefaultTranscriptionBackend = "cloud";
    private const string DefaultLocalModel = "large-v3-turbo";
    private const string DefaultHotkey = "Ctrl+Shift+Space";
    private const int DefaultMaxRecordingSeconds = 120;
    private const bool DefaultStartStopSoundsEnabled = true;
    private const bool DefaultAutoStartOnLogin = true;
    private const int CurrentSchemaVersion = 1;

    private static readonly ConcurrentDictionary<string, object> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _filePath;
    private readonly object _fileLock;

    public ConfigStore(string filePath)
    {
        _filePath = filePath;
        _fileLock = FileLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new object());
    }

    public static ConfigStore Default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new ConfigStore(Path.Combine(appData, "SpeechToText", "config.json"));
    }

    public string GetTranscriptionBackend() => Load().TranscriptionBackend ?? DefaultTranscriptionBackend;
    public void SetTranscriptionBackend(string value) => Mutate(s => s.TranscriptionBackend = value);

    public string GetLocalModel() => Load().LocalModel ?? DefaultLocalModel;
    public void SetLocalModel(string value) => Mutate(s => s.LocalModel = value);

    public string GetHotkey() => Load().Hotkey ?? DefaultHotkey;
    public void SetHotkey(string chord) => Mutate(s => s.Hotkey = chord);

    public int GetMaxRecordingSeconds() => Load().MaxRecordingSeconds ?? DefaultMaxRecordingSeconds;
    public void SetMaxRecordingSeconds(int seconds) => Mutate(s => s.MaxRecordingSeconds = seconds);

    public bool GetStartStopSoundsEnabled() => Load().StartStopSoundsEnabled ?? DefaultStartStopSoundsEnabled;
    public void SetStartStopSoundsEnabled(bool enabled) => Mutate(s => s.StartStopSoundsEnabled = enabled);

    public bool GetAutoStartOnLogin() => Load().AutoStartOnLogin ?? DefaultAutoStartOnLogin;
    public void SetAutoStartOnLogin(bool enabled) => Mutate(s => s.AutoStartOnLogin = enabled);

    public string? GetInputDeviceId() => Load().InputDeviceId;
    public void SetInputDeviceId(string? id) => Mutate(s => s.InputDeviceId = id);

    public int GetSchemaVersion() => Load().SchemaVersion;

    public string? GetGroqApiKey()
    {
        var base64 = Load().GroqApiKeyDpapi;
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            var cipherBytes = Convert.FromBase64String(base64);
            var plaintextBytes = ProtectedData.Unprotect(cipherBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void SetGroqApiKey(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var base64 = Convert.ToBase64String(cipherBytes);
        Mutate(s => s.GroqApiKeyDpapi = base64);
    }

    private void Mutate(Action<ConfigFile> apply)
    {
        lock (_fileLock)
        {
            var state = LoadLocked();
            apply(state);
            SaveLocked(state);
        }
    }

    private ConfigFile Load()
    {
        lock (_fileLock)
        {
            return LoadLocked();
        }
    }

    private ConfigFile LoadLocked()
    {
        if (!File.Exists(_filePath)) return new ConfigFile();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ConfigFile>(json) ?? new ConfigFile();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Trace.TraceWarning($"ConfigStore: failed to read '{_filePath}', falling back to defaults. {ex.GetType().Name}: {ex.Message}");
            return new ConfigFile();
        }
    }

    private void SaveLocked(ConfigFile state)
    {
        state.SchemaVersion = CurrentSchemaVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed class ConfigFile
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string? TranscriptionBackend { get; set; }
        public string? LocalModel { get; set; }
        public string? Hotkey { get; set; }
        public int? MaxRecordingSeconds { get; set; }
        public bool? StartStopSoundsEnabled { get; set; }
        public bool? AutoStartOnLogin { get; set; }
        public string? InputDeviceId { get; set; }
        public string? GroqApiKeyDpapi { get; set; }
    }
}
