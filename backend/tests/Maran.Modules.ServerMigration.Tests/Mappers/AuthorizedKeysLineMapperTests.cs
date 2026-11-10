using Maran.Modules.ServerMigration.Mappers;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>The line the administrator installs: the public key with the options that remove everything but a command session.</summary>
public sealed class AuthorizedKeysLineMapperTests
{
    /// <summary>A public key line as the agent returns it.</summary>
    private const string PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIexample";

    /// <summary>The options that are always present.</summary>
    private const string Options = "no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty";

    /// <summary>With no restriction the line is the options, the key and the marker comment, and has no from option.</summary>
    [Fact]
    public void Without_a_restriction_the_line_has_no_from_option()
    {
        var line = AuthorizedKeysLineMapper.From(PublicKey, null);

        Assert.Equal($"{Options} {PublicKey} maran-migration", line);
        Assert.DoesNotContain("from=", line, StringComparison.Ordinal);
    }

    /// <summary>With a restriction the from option comes first and names the ranges.</summary>
    [Fact]
    public void With_a_restriction_the_from_option_comes_first()
    {
        var line = AuthorizedKeysLineMapper.From(PublicKey, "10.0.0.1/32,192.168.0.0/24");

        Assert.Equal($"from=\"10.0.0.1/32,192.168.0.0/24\",{Options} {PublicKey} maran-migration", line);
    }

    /// <summary>A key line with surrounding space or a trailing newline does not break the line in two.</summary>
    [Fact]
    public void A_key_with_surrounding_space_stays_one_line()
    {
        var line = AuthorizedKeysLineMapper.From($"  {PublicKey}\n", null);

        Assert.DoesNotContain('\n', line);
        Assert.Equal($"{Options} {PublicKey} maran-migration", line);
    }

    /// <summary>The line always removes forwarding and the terminal, which is the whole of what the options promise.</summary>
    [Fact]
    public void The_line_always_removes_forwarding_and_the_terminal()
    {
        var line = AuthorizedKeysLineMapper.From(PublicKey, "10.0.0.1/32");

        foreach (var option in new[] { "no-port-forwarding", "no-agent-forwarding", "no-X11-forwarding", "no-pty" })
        {
            Assert.Contains(option, line, StringComparison.Ordinal);
        }
    }
}
