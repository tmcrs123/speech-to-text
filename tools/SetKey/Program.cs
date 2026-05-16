using SpeechToText;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: SetKey <groq-api-key>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Encrypts the key with DPAPI (CurrentUser scope) and writes it to");
    Console.Error.WriteLine(@"  %APPDATA%\SpeechToText\config.json");
    Console.Error.WriteLine("Run as the same Windows user that will run SpeechToText.exe.");
    return 1;
}

var store = ConfigStore.Default();
store.SetGroqApiKey(args[0]);
Console.WriteLine("Saved (DPAPI-encrypted) to %APPDATA%\\SpeechToText\\config.json");
return 0;
