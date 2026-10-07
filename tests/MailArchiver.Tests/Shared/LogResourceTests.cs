using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace MailArchiver.Tests.Shared;

/// <summary>
/// Every log event written with LogText.Event("Key", ...) needs a "Log_Key" text in English
/// and German, otherwise the log shows the bare key.
/// </summary>
public class LogResourceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MailArchiver.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> LogKeys(string resx) =>
        XDocument.Load(resx).Root!.Elements("data")
            .Select(d => (string)d.Attribute("name")!)
            .Where(n => n.StartsWith("Log_", StringComparison.Ordinal))
            .ToHashSet();

    [Fact]
    public void EveryLogEvent_HasEnglishAndGermanText()
    {
        var root = RepoRoot();
        var en = LogKeys(Path.Combine(root, "Resources", "SharedResource.resx"));
        var de = LogKeys(Path.Combine(root, "Resources", "SharedResource.de.resx"));

        var used = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "LogText\\.Event\\(\\s*\"([A-Za-z0-9]+)\""))
            .Select(m => "Log_" + m.Groups[1].Value)
            .ToHashSet();

        Assert.Contains("Log_ApiAccess", used);
        Assert.Contains("Log_McpSearch", used);
        Assert.Empty(used.Except(en));
        Assert.Empty(used.Except(de));
        Assert.Equal(en.OrderBy(k => k), de.OrderBy(k => k));
    }
}
