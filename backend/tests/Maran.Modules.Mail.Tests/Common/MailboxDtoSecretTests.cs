using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;

namespace Maran.Modules.Mail.Tests.Common;

/// <summary>
/// A mailbox's password has exactly one home — the hash on the row — and no wire shape can carry it.
/// </summary>
public sealed class MailboxDtoSecretTests
{
    /// <summary>Fragments that name a secret; a member containing any of them is a candidate for a leak.</summary>
    private static readonly string[] SecretFragments = ["password", "passwd", "secret", "hash", "credential", "token"];

    /// <summary>The only member of <see cref="Mailbox"/> allowed to look like a secret is the stored hash.</summary>
    [Fact]
    public void Only_the_stored_hash_on_the_mailbox_looks_like_a_secret()
    {
        var secretLooking = SecretMembersOf(typeof(Mailbox));

        Assert.Equal(new[] { nameof(Mailbox.PasswordHash) }, secretLooking);
    }

    /// <summary>The mailbox wire shape has no secret-looking member at all.</summary>
    [Fact]
    public void The_mailbox_wire_shape_has_no_secret_looking_member()
    {
        Assert.Empty(SecretMembersOf(typeof(MailboxDto)));
    }

    /// <summary>The autoresponder wire shape has no secret-looking member at all.</summary>
    [Fact]
    public void The_autoresponder_wire_shape_has_no_secret_looking_member()
    {
        Assert.Empty(SecretMembersOf(typeof(MailAutoresponderDto)));
    }

    /// <summary>The scan can see a secret-looking member when one exists, so the empty answers above mean something.</summary>
    [Fact]
    public void The_scan_finds_a_secret_looking_member_when_one_exists()
    {
        var leaking = new { PlainPassword = "x", Name = "y" };

        Assert.Equal(["PlainPassword"], SecretMembersOf(leaking.GetType()));
    }

    /// <summary>The public instance property names of a type that contain a secret-looking fragment.</summary>
    /// <param name="type">The type to scan.</param>
    /// <returns>The offending names, in alphabetical order.</returns>
    private static string[] SecretMembersOf(Type type)
    {
        return type
            .GetProperties()
            .Select(property =>
            {
                return property.Name;
            })
            .Where(name =>
            {
                return SecretFragments.Any(fragment =>
                {
                    return name.Contains(fragment, StringComparison.OrdinalIgnoreCase);
                });
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
