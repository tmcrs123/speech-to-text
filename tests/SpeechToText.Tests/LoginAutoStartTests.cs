namespace SpeechToText.Tests;

using SpeechToText;
using Xunit;

public sealed class LoginAutoStartTests
{
    private sealed class FakeRunKey : IRunRegistryKey
    {
        public Dictionary<string, string> Values { get; } = new();
        public int DeleteCalls { get; private set; }

        public string? GetValue(string name) =>
            Values.TryGetValue(name, out var v) ? v : null;

        public void SetValue(string name, string value) => Values[name] = value;

        public void DeleteValue(string name)
        {
            DeleteCalls++;
            Values.Remove(name);
        }
    }

    private const string ExePath = @"C:\Program Files\SpeechToText\SpeechToText.exe";

    [Fact]
    public void Apply_True_WritesEntryWithQuotedExeAndMinimizedFlag()
    {
        var key = new FakeRunKey();
        var autoStart = new LoginAutoStart(key, () => ExePath);

        autoStart.Apply(true);

        Assert.True(autoStart.IsRegistered());
        Assert.Equal($"\"{ExePath}\" --minimized", key.Values["SpeechToText"]);
    }

    [Fact]
    public void Apply_False_RemovesEntry()
    {
        var key = new FakeRunKey();
        var autoStart = new LoginAutoStart(key, () => ExePath);
        autoStart.Apply(true);
        Assert.True(autoStart.IsRegistered());

        autoStart.Apply(false);

        Assert.False(autoStart.IsRegistered());
        Assert.False(key.Values.ContainsKey("SpeechToText"));
    }

    [Fact]
    public void Apply_False_WhenAlreadyAbsent_DoesNotThrow()
    {
        var key = new FakeRunKey();
        var autoStart = new LoginAutoStart(key, () => ExePath);

        autoStart.Apply(false);

        Assert.False(autoStart.IsRegistered());
    }

    [Fact]
    public void Apply_True_AfterUnregister_RestoresEntry()
    {
        var key = new FakeRunKey();
        var autoStart = new LoginAutoStart(key, () => ExePath);

        autoStart.Apply(true);
        autoStart.Apply(false);
        autoStart.Apply(true);

        Assert.True(autoStart.IsRegistered());
        Assert.Equal($"\"{ExePath}\" --minimized", key.Values["SpeechToText"]);
    }

    [Fact]
    public void Register_NullExePath_DoesNotWriteEntry()
    {
        var key = new FakeRunKey();
        var autoStart = new LoginAutoStart(key, () => null);

        autoStart.Apply(true);

        Assert.False(autoStart.IsRegistered());
    }

    [Fact]
    public void Register_PicksUpFreshExePath_OnEachCall()
    {
        var key = new FakeRunKey();
        string current = @"C:\Old\SpeechToText.exe";
        var autoStart = new LoginAutoStart(key, () => current);

        autoStart.Apply(true);
        Assert.Equal($"\"{current}\" --minimized", key.Values["SpeechToText"]);

        current = @"C:\New\SpeechToText.exe";
        autoStart.Apply(true);
        Assert.Equal($"\"{current}\" --minimized", key.Values["SpeechToText"]);
    }

    [Fact]
    public void RegisteredCommand_ReturnsStoredValue()
    {
        var key = new FakeRunKey();
        var autoStart = new LoginAutoStart(key, () => ExePath);
        autoStart.Apply(true);

        Assert.Equal($"\"{ExePath}\" --minimized", autoStart.RegisteredCommand());
    }

    [Fact]
    public void CustomValueName_IsUsed()
    {
        var key = new FakeRunKey();
        var autoStart = new LoginAutoStart(key, () => ExePath, valueName: "MyApp");

        autoStart.Apply(true);

        Assert.True(key.Values.ContainsKey("MyApp"));
        Assert.False(key.Values.ContainsKey("SpeechToText"));
    }
}
