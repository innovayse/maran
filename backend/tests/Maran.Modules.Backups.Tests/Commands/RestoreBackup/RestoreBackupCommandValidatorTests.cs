using Maran.Modules.Backups.Commands.RestoreBackup;

namespace Maran.Modules.Backups.Tests.Commands.RestoreBackup;

/// <summary>
/// What a restore request must look like before the handler reads anything: the confirmation's shape,
/// and — for a restore narrowed to one database or one site (issue #160) — the shape of the name and
/// the refusal of a request that names both.
/// </summary>
public sealed class RestoreBackupCommandValidatorTests
{
    /// <summary>A request the validator accepts, which each refusal below is a deviation from.</summary>
    private static RestoreBackupCommand Sound()
    {
        return new RestoreBackupCommand(Guid.NewGuid(), "alice");
    }

    /// <summary>The messages the validator raised for <paramref name="command"/>.</summary>
    /// <param name="command">The request.</param>
    /// <returns>Every message, as the resx keys the exception middleware resolves.</returns>
    private static List<string> Messages(RestoreBackupCommand command)
    {
        return new RestoreBackupCommandValidator()
            .Validate(command)
            .Errors
            .Select(error => { return error.ErrorMessage; })
            .ToList();
    }

    /// <summary>The inverse control: a whole restore, a database restore and a site restore are all accepted.</summary>
    [Fact]
    public void A_whole_a_database_and_a_site_restore_are_all_accepted()
    {
        Assert.Empty(Messages(Sound()));
        Assert.Empty(Messages(Sound() with { DatabaseName = "alice_shop" }));
        Assert.Empty(Messages(Sound() with { SiteDomain = "shop.example.com" }));
    }

    /// <summary>Naming a database and a site together is refused, whichever is the valid one.</summary>
    [Fact]
    public void Naming_both_a_database_and_a_site_is_refused()
    {
        var messages = Messages(Sound() with { DatabaseName = "alice_shop", SiteDomain = "shop.example.com" });

        Assert.Contains("RestoreScopeAmbiguous", messages);
    }

    /// <summary>A database name outside the alphabet the panel creates them in is refused.</summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("alice shop")]
    [InlineData("alice-shop")]
    [InlineData("alice_shop;drop database mysql")]
    [InlineData("../../etc")]
    [InlineData("alice_shop\n")]
    public void A_database_name_outside_the_alphabet_is_refused(string name)
    {
        var messages = Messages(Sound() with { DatabaseName = name });

        Assert.Contains("RestoreDatabaseInvalid", messages);
    }

    /// <summary>A database name over the server's ceiling is refused.</summary>
    [Fact]
    public void A_database_name_over_the_ceiling_is_refused()
    {
        var messages = Messages(Sound() with { DatabaseName = new string('a', 65) });

        Assert.Contains("RestoreDatabaseInvalid", messages);
    }

    /// <summary>A site name that is not a host name — a path above all — is refused.</summary>
    /// <param name="domain">The name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("shop")]
    [InlineData("../shop.example.com")]
    [InlineData("shop.example.com/..")]
    [InlineData("shop example.com")]
    [InlineData("shop.example.com\n")]
    [InlineData("-shop.example.com")]
    public void A_site_name_that_is_not_a_host_name_is_refused(string domain)
    {
        var messages = Messages(Sound() with { SiteDomain = domain });

        Assert.Contains("RestoreSiteInvalid", messages);
    }

    /// <summary>A site name over the host-name ceiling is refused.</summary>
    [Fact]
    public void A_site_name_over_the_ceiling_is_refused()
    {
        var domain = string.Join('.', Enumerable.Repeat(new string('a', 60), 5)) + ".com";

        var messages = Messages(Sound() with { SiteDomain = domain });

        Assert.Contains("RestoreSiteInvalid", messages);
    }

    /// <summary>The confirmation rules are untouched by the scope: a missing confirmation still refuses.</summary>
    [Fact]
    public void A_narrowed_restore_still_needs_the_typed_confirmation()
    {
        var messages = Messages(Sound() with { ConfirmAccountUsername = string.Empty, DatabaseName = "alice_shop" });

        Assert.Contains("RestoreConfirmationRequired", messages);
    }
}
