namespace SpeechToText.Tests;

using System.Text.RegularExpressions;
using Xunit;

// Architectural assertion (user story 26 / issue #5): no audio or transcript is
// ever written to disk by the orchestrator or its collaborators. Verified by
// statically scanning the source tree for disk-write APIs.
public class NoDiskWritesTests
{
    [Fact]
    public void NoSourceFileReferencesDiskWriteApis()
    {
        string srcDir = LocateSrcDir();
        var offenders = new List<string>();
        var forbidden = new Regex(
            @"\b(File\.(Write|AppendAll|AppendText|Create|Open(Write|Append))|StreamWriter\s*\(|FileStream\s*\(|Path\.GetTempFileName)\b",
            RegexOptions.Compiled);

        foreach (string cs in Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(cs);
            if (forbidden.IsMatch(text))
                offenders.Add(Path.GetFileName(cs));
        }

        Assert.Empty(offenders);
    }

    private static string LocateSrcDir()
    {
        // Walk up from the test binary location until we find a sibling `src` folder
        // containing DictationOrchestrator.cs.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "DictationOrchestrator.cs")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("could not locate src/ from " + AppContext.BaseDirectory);
    }
}
