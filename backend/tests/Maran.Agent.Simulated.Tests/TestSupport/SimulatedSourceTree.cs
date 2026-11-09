namespace Maran.Agent.Simulated.Tests.TestSupport;

/// <summary>Finds the simulated agent's source files from wherever the tests run.</summary>
public static class SimulatedSourceTree
{
    /// <summary>The project file whose folder is the simulated agent's source root.</summary>
    private const string ProjectFile = "Maran.Agent.Simulated.csproj";

    /// <summary>Lists every C# source file of the simulated agent, build output excluded.</summary>
    /// <returns>The absolute paths.</returns>
    /// <exception cref="DirectoryNotFoundException">
    /// The source tree is not above the test binaries: a defect of the checkout, reported rather than
    /// answered with an empty list, which would make every scan over it pass over nothing.
    /// </exception>
    public static IReadOnlyList<string> SourceFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Maran.Agent.Simulated", ProjectFile);
            if (File.Exists(candidate))
            {
                var root = Path.GetDirectoryName(candidate)!;
                var separator = Path.DirectorySeparatorChar;

                return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                    .Where(path => { return !path.Contains(separator + "obj" + separator, StringComparison.Ordinal); })
                    .Where(path => { return !path.Contains(separator + "bin" + separator, StringComparison.Ordinal); })
                    .Order(StringComparer.Ordinal)
                    .ToList();
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "The source of Maran.Agent.Simulated was not found above the test output.");
    }
}
