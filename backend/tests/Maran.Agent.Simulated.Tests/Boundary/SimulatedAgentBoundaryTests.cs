using Maran.Agent.Simulated.Tests.TestSupport;

namespace Maran.Agent.Simulated.Tests.Boundary;

/// <summary>The simulated agent changes nothing outside its own memory.</summary>
public sealed class SimulatedAgentBoundaryTests
{
    /// <summary>
    /// No source file of the simulated agent spawns a process, touches the file system or opens a
    /// socket. The scan reads the project's own files, and the controls below prove it can see a
    /// violation and does not mistake commentary for one; the file count proves it looked at the tree.
    /// </summary>
    [Fact]
    public void No_source_file_spawns_a_process_touches_a_file_system_or_opens_a_socket()
    {
        var files = SimulatedSourceTree.SourceFiles();

        Assert.True(files.Count >= 30, $"The scan found only {files.Count} files; it is not looking at the project.");
        var offenders = files
            .Select(path => { return (Path: path, Violations: ForbiddenApiScan.Violations(File.ReadAllText(path))); })
            .Where(entry => { return entry.Violations.Count > 0; })
            .Select(entry => { return entry.Path + ": " + string.Join(", ", entry.Violations); })
            .ToList();
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Positive control: each kind of violation is found in code that commits it.</summary>
    [Fact]
    public void The_scan_finds_a_process_a_file_access_and_a_socket_in_code_that_has_them()
    {
        AssertFlaggedAs("process", "var p = System.Diagnostics.Process.Start(\"ls\");");
        AssertFlaggedAs("file system", "var text = File.ReadAllText(path);");
        AssertFlaggedAs("file system", "using var writer = new StreamWriter(path);");
        AssertFlaggedAs("network", "using var client = new HttpClient();");
        AssertFlaggedAs("network", "var socket = new TcpClient(host, 80);");
    }

    /// <summary>Accepting control: code that only mentions these things in comments and text is clean.</summary>
    [Fact]
    public void The_scan_accepts_commentary_and_text_that_only_mention_them()
    {
        const string Source = """
            // opens no Socket and spawns no Process.Start
            /// <summary>Never touches File.Delete or an HttpClient.</summary>
            public sealed class Clean
            {
                private const string Note = "HttpClient and File.Delete are named here only as text";

                private const string FpmSocketDirectory = "/run/maran-php";
            }
            """;

        Assert.Empty(ForbiddenApiScan.Violations(Source));
    }

    /// <summary>Asserts the scan flags <paramref name="source"/> as exactly one kind of violation.</summary>
    /// <param name="expected">The name of the rule it must break.</param>
    /// <param name="source">The code to scan.</param>
    private static void AssertFlaggedAs(string expected, string source)
    {
        Assert.Equal(expected, Assert.Single(ForbiddenApiScan.Violations(source)));
    }
}
