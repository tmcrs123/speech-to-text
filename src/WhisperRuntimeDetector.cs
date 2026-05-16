using System.Runtime.InteropServices;

namespace SpeechToText;

// Best-effort probe of which Whisper.net native runtimes can load on this
// machine, in priority order CUDA → Vulkan → CPU. The actual runtime
// selection inside Whisper.net happens when WhisperFactory.FromPath runs;
// this probe is for the wizard so it can filter model sizes to viable ones
// before download.
internal enum WhisperRuntime
{
    Cuda,
    Vulkan,
    Cpu,
}

internal static class WhisperRuntimeDetector
{
    public static WhisperRuntime Detect()
    {
        if (CanLoadModule("nvcuda.dll")) return WhisperRuntime.Cuda;
        if (CanLoadModule("vulkan-1.dll")) return WhisperRuntime.Vulkan;
        return WhisperRuntime.Cpu;
    }

    // Models small enough to be usable on the detected runtime. CPU caps at
    // 'small' because medium/large are too slow for an interactive Dictation
    // (a 10s clip on a 'large' CPU run is on the order of 30s+ on common
    // laptops). GPU runtimes can handle all sizes.
    public static IReadOnlyList<string> ViableModels(WhisperRuntime runtime) => runtime switch
    {
        WhisperRuntime.Cpu => new[] { "tiny", "base", "small" },
        _ => new[] { "tiny", "base", "small", "medium", "large-v3", "large-v3-turbo" },
    };

    private static bool CanLoadModule(string name)
    {
        var h = LoadLibraryW(name);
        if (h == IntPtr.Zero) return false;
        FreeLibrary(h);
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);
}
