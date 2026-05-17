namespace SpeechToText;

using System.Diagnostics;
using Microsoft.Win32;

// Manages the HKCU "Run" registry entry that auto-launches the app on Windows
// login. The behaviour is split behind IRunRegistryKey so unit tests exercise
// the Apply/Register/Unregister logic without touching the real HKCU hive.
//
// The on-disk source of truth is ConfigStore.AutoStartOnLogin. The registry
// entry is a derived, idempotent reflection of that flag and the current
// executable path — call Apply(value) whenever either may have changed.
public sealed class LoginAutoStart
{
    public const string DefaultValueName = "SpeechToText";
    public const string MinimizedArgument = "--minimized";

    private readonly IRunRegistryKey _key;
    private readonly Func<string?> _exePathProvider;
    private readonly string _valueName;

    public LoginAutoStart(
        IRunRegistryKey key,
        Func<string?> exePathProvider,
        string valueName = DefaultValueName)
    {
        _key = key;
        _exePathProvider = exePathProvider;
        _valueName = valueName;
    }

    public static LoginAutoStart Default() =>
        new(new HkcuRunRegistryKey(), () => Environment.ProcessPath);

    public bool IsRegistered() => _key.GetValue(_valueName) != null;

    public string? RegisteredCommand() => _key.GetValue(_valueName);

    public void Apply(bool enabled)
    {
        if (enabled) Register();
        else Unregister();
    }

    public void Register()
    {
        var exe = _exePathProvider();
        if (string.IsNullOrEmpty(exe))
        {
            Trace.TraceWarning("LoginAutoStart: cannot resolve executable path; skipping registration.");
            return;
        }
        _key.SetValue(_valueName, FormatCommand(exe));
    }

    public void Unregister() => _key.DeleteValue(_valueName);

    internal static string FormatCommand(string exePath) =>
        $"\"{exePath}\" {MinimizedArgument}";
}

public interface IRunRegistryKey
{
    string? GetValue(string name);
    void SetValue(string name, string value);
    void DeleteValue(string name);
}

internal sealed class HkcuRunRegistryKey : IRunRegistryKey
{
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true)
            ?? throw new InvalidOperationException($"Could not open HKCU\\{SubKey} for writing.");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
