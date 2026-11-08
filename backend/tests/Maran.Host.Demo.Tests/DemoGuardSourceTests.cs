namespace Maran.Host.Demo.Tests;

/// <summary>
/// The demo's adapters and guard open no socket, start no process and touch no file: a source scan,
/// because a unit test cannot observe a call that never happens (threat note T3, T4, T5).
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: a call made through a type this list does not name, and anything the folders it does
/// not scan do (the seeder and the status endpoint are other code with other reasons to touch the host).
/// The unit's <c>IPAddressDeny=any</c> is the measurement that covers the rest.
/// </remarks>
public sealed class DemoGuardSourceTests
{
    /// <summary>What the scan reports for the planted source line.</summary>
    private static readonly string[] PlantedViolations = ["HttpClient"];

    /// <summary>The folders of the demo project that must stay free of host effects.</summary>
    private static readonly string[] ScannedFolders =
    [
        "Adapters",
        "Guard",
        "Pseudonymisation",
        "Extensions",
        "Services",
        "Options",
    ];

    /// <summary>What a file in those folders may not mention outside a comment.</summary>
    private static readonly string[] Forbidden =
    [
        "System.Diagnostics.Process",
        "Process.Start",
        "HttpClient",
        "IHttpClientFactory",
        "new Socket(",
        "TcpListener",
        "TcpClient",
        "UdpClient",
        "SmtpClient",
        "File.Read",
        "File.Write",
        "File.Open",
        "File.Create",
        "Directory.Create",
    ];

    /// <summary>The source folder of the demo project, found by walking up from the test binary.</summary>
    /// <returns>The folder.</returns>
    private static string DemoSourceFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Maran.Host.Demo");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("backend/src/Maran.Host.Demo was not found above the test binary.");
    }

    /// <summary>The forbidden names a source text uses outside comment lines.</summary>
    /// <param name="source">The text of one file.</param>
    /// <returns>The forbidden names found, in order.</returns>
    private static List<string> Violations(string source)
    {
        var code = source
            .Split('\n')
            .Where(line => { return !line.TrimStart().StartsWith("//", StringComparison.Ordinal); });
        var text = string.Join('\n', code);
        return Forbidden.Where(name => { return text.Contains(name, StringComparison.Ordinal); }).ToList();
    }

    /// <summary>The positive control: the scan finds what it hunts, and ignores a comment that names it.</summary>
    [Fact]
    public void The_scan_finds_a_planted_violation_and_ignores_a_comment()
    {
        Assert.Equal(PlantedViolations, Violations("var client = new HttpClient();"));
        Assert.Empty(Violations("/// <summary>Opens no HttpClient.</summary>\n// File.Write is not used"));
    }

    /// <summary>No scanned file mentions a forbidden type, and the scan read a meaningful number of files.</summary>
    [Fact]
    public void The_guard_and_the_adapters_open_no_socket_start_no_process_and_touch_no_file()
    {
        var root = DemoSourceFolder();
        var files = ScannedFolders
            .Select(folder => { return Path.Combine(root, folder); })
            .Where(Directory.Exists)
            .SelectMany(folder => { return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories); })
            .ToList();

        Assert.True(files.Count >= 20, $"Only {files.Count} files were scanned; the search may be blind.");
        var found = files
            .Select(file =>
            {
                return (File: Path.GetFileName(file), Hits: Violations(System.IO.File.ReadAllText(file)));
            })
            .Where(pair => { return pair.Hits.Count > 0; })
            .Select(pair => { return $"{pair.File}: {string.Join(", ", pair.Hits)}"; })
            .ToList();
        Assert.True(found.Count == 0, "Host effects in the demo guard: " + string.Join("; ", found));
    }
}
