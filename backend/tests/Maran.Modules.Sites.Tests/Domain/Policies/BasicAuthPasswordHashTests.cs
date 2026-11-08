using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="BasicAuthPasswordHash"/>.</summary>
public sealed class BasicAuthPasswordHashTests
{
    /// <summary>A hash is the 60-character bcrypt form the web server and the agent accept.</summary>
    [Fact]
    public void A_hash_is_the_sixty_character_bcrypt_form_the_web_server_and_the_agent_accept()
    {
        var hash = BasicAuthPasswordHash.Hash("correct horse battery");

        Assert.Equal(60, hash.Length);
        Assert.StartsWith("$2y$12$", hash, StringComparison.Ordinal);
        Assert.True(BasicAuthPasswordHash.IsHash(hash));
    }

    /// <summary>A hash never contains the password.</summary>
    [Fact]
    public void A_hash_never_contains_the_password()
    {
        Assert.DoesNotContain("correct horse battery", BasicAuthPasswordHash.Hash("correct horse battery"), StringComparison.Ordinal);
    }

    /// <summary>Two hashes of one password differ, because each gets its own salt.</summary>
    [Fact]
    public void Two_hashes_of_one_password_differ_because_each_gets_its_own_salt()
    {
        Assert.NotEqual(BasicAuthPasswordHash.Hash("correct horse battery"), BasicAuthPasswordHash.Hash("correct horse battery"));
    }

    /// <summary>The hash verifies the password it was made from and no other.</summary>
    [Fact]
    public void The_hash_verifies_the_password_it_was_made_from_and_no_other()
    {
        var hash = BasicAuthPasswordHash.Hash("correct horse battery");

        Assert.True(BCrypt.Net.BCrypt.Verify("correct horse battery", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("wrong horse battery", hash));
    }

    /// <summary>Anything that is not a hash this type writes is not accepted as one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("$apr1$abcdefgh$abcdefghijklmnopqrstuv")]
    [InlineData("{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=")]
    [InlineData("$2y$12$short")]
    public void Anything_that_is_not_a_hash_this_type_writes_is_not_accepted_as_one(string? value)
    {
        Assert.False(BasicAuthPasswordHash.IsHash(value));
    }

    /// <summary>A password within the bounds is acceptable.</summary>
    [Fact]
    public void A_password_within_the_bounds_is_acceptable()
    {
        Assert.True(BasicAuthPasswordHash.IsAcceptablePassword(new string('a', SiteOptionsLimits.MinimumPasswordLength)));
        Assert.True(BasicAuthPasswordHash.IsAcceptablePassword(new string('a', SiteOptionsLimits.MaximumPasswordBytes)));
    }

    /// <summary>Too short, too long in bytes and missing passwords are not acceptable.</summary>
    [Fact]
    public void Too_short_too_long_in_bytes_and_missing_passwords_are_not_acceptable()
    {
        Assert.False(BasicAuthPasswordHash.IsAcceptablePassword(null));
        Assert.False(BasicAuthPasswordHash.IsAcceptablePassword(new string('a', SiteOptionsLimits.MinimumPasswordLength - 1)));
        Assert.False(BasicAuthPasswordHash.IsAcceptablePassword(new string('a', SiteOptionsLimits.MaximumPasswordBytes + 1)));

        // 40 two-byte characters are 80 bytes: short in characters, over the limit in what bcrypt reads.
        Assert.False(BasicAuthPasswordHash.IsAcceptablePassword(new string('ä', 40)));
    }
}
