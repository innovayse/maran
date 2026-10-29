using System.Globalization;
using System.Text.RegularExpressions;
using Maran.Agent.Client.Services.SystemService;

namespace Maran.ArchitectureTests;

/// <summary>
/// The window of contract revisions the panel drives and the revision the agent reports are the same
/// number, stated in two languages and held together here: moving either one without the other fails
/// a named test.
/// </summary>
/// <remarks>
/// <para>
/// <c>AgentProtoVersionWindowTests</c> in <c>Maran.Agent.Client.Tests</c> uses
/// <see cref="AgentProtoVersionWindow.Minimum"/> and <see cref="AgentProtoVersionWindow.Maximum"/>
/// symbolically, so raising <see cref="AgentProtoVersionWindow.Maximum"/> from 2 to 3 leaves every one
/// of those tests green: they would be computing the new edges from the drifted constant. The agent's
/// own test pins <c>PROTO_VERSION</c> to its recorded revisions and cannot see the panel at all. The
/// two sides meet only here, where the literal is written down once in C# and the agent's constant is
/// read from the Rust source text, so a drift on either side is a red test and not a handshake that
/// refuses every agent in production.
/// </para>
/// <para>
/// UNOBSERVED HERE: the agent binary actually running. This reads the constant's text from
/// <c>system_service.rs</c>, not the value a built agent answers on the wire; that half is the
/// handshake gate (<c>maran handshake</c>). A refactor that renames or moves the constant makes the
/// reading fail loudly rather than pass, which the positive control below proves.
/// </para>
/// </remarks>
public sealed class AgentProtoVersionLockstepTests
{
    /// <summary>The repository-relative file declaring the agent's <c>PROTO_VERSION</c>.</summary>
    private const string AgentConstantFile = "agent/crates/agent/src/services/system/system_service.rs";

    /// <summary>The window's newest end, pinned here; raised in the same change as the agent's constant.</summary>
    private const uint PinnedMaximum = 2;

    /// <summary>
    /// The window's oldest end, pinned here; moves with <see cref="PinnedMaximum"/> unless the change says why not.
    /// </summary>
    private const uint PinnedMinimum = 2;

    /// <summary>The agent's declaration: <c>const PROTO_VERSION: u32 = N;</c> at the start of a line.</summary>
    private static readonly Regex ConstantDeclaration =
        new(@"^const PROTO_VERSION: u32 = (?<value>\d+);", RegexOptions.Multiline, TimeSpan.FromSeconds(5));

    /// <summary>Reads the revision a Rust source text declares as <c>PROTO_VERSION</c>.</summary>
    /// <param name="source">The text of the Rust file.</param>
    /// <returns>The declared revision.</returns>
    private static uint DeclaredRevision(string source)
    {
        var match = ConstantDeclaration.Match(source);
        Assert.True(match.Success, "No `const PROTO_VERSION: u32 = N;` line was found in the source text.");
        return uint.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>The panel's window is the literal pair pinned here, so editing either end alone fails.</summary>
    [Fact]
    public void The_window_is_the_pinned_literal_pair()
    {
        Assert.Equal(PinnedMinimum, AgentProtoVersionWindow.Minimum);
        Assert.Equal(PinnedMaximum, AgentProtoVersionWindow.Maximum);
    }

    /// <summary>The agent's <c>PROTO_VERSION</c> is the newest revision the window drives, and inside it.</summary>
    [Fact]
    public void The_agent_constant_equals_the_newest_revision_the_window_drives()
    {
        var source = File.ReadAllText(
            Path.Combine(SpaLocaleKeys.RepositoryRoot(), AgentConstantFile.Replace('/', Path.DirectorySeparatorChar)));

        var declared = DeclaredRevision(source);

        Assert.Equal(AgentProtoVersionWindow.Maximum, declared);
        Assert.True(
            AgentProtoVersionWindow.Contains(declared),
            $"The agent reports revision {declared}, which the panel's window refuses.");
    }

    /// <summary>
    /// The reader finds the number in a planted declaration and refuses text that has none, so a silent miss
    /// cannot pass.
    /// </summary>
    [Fact]
    public void The_constant_reader_finds_a_planted_declaration_and_refuses_text_without_one()
    {
        Assert.Equal(7u, DeclaredRevision("/// docs\nconst PROTO_VERSION: u32 = 7;\n"));
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
        {
            return DeclaredRevision("// const PROTO_VERSION was renamed\nconst REVISION: u32 = 2;\n");
        });
    }
}
