using System.Text.RegularExpressions;
using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>What the sample files an operator copies actually publish, read from the files themselves.</summary>
public sealed partial class SecretKeyPolicySampleFilesTests
{
    /// <summary>The development key literal, as base64.</summary>
    private const string PublishedKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every key a sample file or workflow publishes is a key the policy knows.</summary>
    [Fact]
    public void Every_key_a_sample_file_publishes_is_a_known_published_key()
    {
        var root = RepositoryRoot();
        var files = new List<string>
        {
            Path.Combine(root, ".env.example"),
            Path.Combine(root, "docker", ".env.example"),
        };
        files.AddRange(Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"));

        var found = new List<string>();
        foreach (var file in files.Where(File.Exists))
        {
            foreach (var value in KeyValues(file))
            {
                found.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
                Assert.True(
                    SecretKeyPolicy.IsPublishedDevelopmentKey(value),
                    $"{Path.GetRelativePath(root, file)} publishes a key the policy does not know");
            }
        }

        Assert.Equal(2, found.Count(name =>
        {
            return name == ".env.example";
        }));
        Assert.Contains(".github/workflows/cross.yml", found);
    }

    /// <summary>The installer sample carries no key at all.</summary>
    [Fact]
    public void The_installer_sample_ships_both_keys_empty()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "installer", "panel.env.example"));

        Assert.Contains("Security__EncryptionKey=", lines);
        Assert.Contains("Jwt__SigningKey=", lines);
    }

    /// <summary>The published literal lives in the policy and nowhere else in production code.</summary>
    [Fact]
    public void The_published_key_is_written_once_in_production_code()
    {
        var backend = Path.Combine(RepositoryRoot(), "backend");

        var inTests = FilesHoldingTheLiteral(Path.Combine(backend, "tests"));
        Assert.NotEmpty(inTests);

        var inSource = FilesHoldingTheLiteral(Path.Combine(backend, "src"));
        var only = Assert.Single(inSource);
        Assert.EndsWith("SecretKeyPolicy.cs", only, StringComparison.Ordinal);
    }

    /// <summary>The non-comment values assigned to the two key settings in a file.</summary>
    /// <param name="file">The file to read.</param>
    /// <returns>Each non-empty value found.</returns>
    private static IEnumerable<string> KeyValues(string file)
    {
        foreach (var line in File.ReadAllLines(file))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            var match = KeyAssignment().Match(trimmed);
            if (match.Success && match.Groups[1].Value.Length > 0)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    /// <summary>The C# files under a folder that contain the published literal, build output excluded.</summary>
    /// <param name="folder">The folder to scan.</param>
    /// <returns>The matching file paths.</returns>
    private static List<string> FilesHoldingTheLiteral(string folder)
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                return !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                    && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
            })
            .Where(path =>
            {
                return File.ReadAllText(path).Contains(PublishedKey, StringComparison.Ordinal);
            })
            .ToList();
    }

    /// <summary>Matches an assignment of either key setting, as an env line or a YAML entry.</summary>
    /// <returns>The regular expression.</returns>
    [GeneratedRegex(@"^(?:Security__EncryptionKey|Jwt__SigningKey)\s*[=:]\s*([A-Za-z0-9+/=]*)")]
    private static partial Regex KeyAssignment();

    /// <summary>Walks up from the test output to the folder holding <c>backend/Maran.sln</c>.</summary>
    /// <returns>The repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "Maran.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
