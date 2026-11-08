namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>
/// The paths a validator at a route's boundary must refuse, and one it must accept, shared by every
/// route's validator test so a route cannot be covered by a smaller corpus than the others.
/// </summary>
public static class FilesHostilePaths
{
    /// <summary>A legal path: the control that stops a validator mutated to refuse everything from passing.</summary>
    public const string Legal = "docs/readme.txt";

    /// <summary>Paths no route accepts.</summary>
    /// <returns>One row per hostile path, for a theory.</returns>
    public static TheoryData<string> Refused()
    {
        return new TheoryData<string>
        {
            "..",
            "a/../../b",
            "../etc/passwd",
            "/etc/passwd",
            "a\\..\\b",
            "a//b",
            "a/",
            "./a",
            "a/./b",
            "a\0b",
            "a\nb",
            "a\u2028b",
            new string('x', 256),
            string.Join('/', Enumerable.Repeat("d", 65)),
            ".maran-trash/anything",
        };
    }
}
