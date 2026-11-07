using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of Identity's personal-data provider: what it answers for a user and for an
/// account, and that no secret-bearing column can appear in either answer (issue #146).
/// </summary>
public sealed class IdentityPersonalDataProviderTests : IDisposable
{
    /// <summary>The user's password hash, planted so the probe has something to find.</summary>
    private const string PlantedPasswordHash = "PLANTED-PASSWORD-HASH-7f3a";

    /// <summary>The user's TOTP secret, planted for the same reason.</summary>
    private const string PlantedTotpSecret = "PLANTED-TOTP-SECRET-9c1e";

    /// <summary>The session's refresh-token digest, planted for the same reason.</summary>
    private const string PlantedTokenHash = "PLANTED-TOKEN-HASH-b42d";

    /// <summary>The instant everything is stored at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The user the tests are about.</summary>
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>The account the tests are about.</summary>
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>The module's context over its own in-memory database.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Stores a user holding every planted secret, a seat in the account, a session, a pin and an offer.</summary>
    private async Task SeedAsync()
    {
        var user = new User(UserId, "olive", "olive@example.com", PlantedPasswordHash, UserRole.Customer, Now);
        user.EnableTotp(PlantedTotpSecret);
        _context.Users.Add(user);
        _context.Users.Add(new User(
            Guid.NewGuid(), "stranger", "stranger@example.com", "other-hash", UserRole.Customer, Now));
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), UserId, AccountId, Now));
        _context.Sessions.Add(new Session(
            Guid.NewGuid(), UserId, Guid.NewGuid(), PlantedTokenHash, Now, Now.AddDays(7), "203.0.113.9", "Firefox"));
        _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), UserId, "sites", 0, Now));
        _context.AccountMembershipOffers.Add(AccountMembershipOffer.Offer(
            Guid.NewGuid(), UserId, AccountId, "acme", "olive@example.com", AccountFeature.None, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Flattens sections into one searchable string of every name and value they carry.</summary>
    /// <param name="sections">The sections to flatten.</param>
    private static string Flatten(IEnumerable<PersonalDataSection> sections)
    {
        return string.Join(
            "|",
            sections.SelectMany(section =>
            {
                return section.Records.SelectMany(record =>
                {
                    return record.Fields.Select(field =>
                    {
                        return $"{section.Name}.{field.Key}={field.Value}";
                    });
                });
            }));
    }

    /// <summary>Asks the provider about one subject.</summary>
    /// <param name="kind">The subject's kind.</param>
    /// <param name="id">The subject's id.</param>
    private async Task<IReadOnlyList<PersonalDataSection>> AskAsync(PersonalDataSubjectKind kind, Guid id)
    {
        return await new IdentityPersonalDataProvider(_context)
            .ExportAsync(new PersonalDataSubject(kind, id), CancellationToken.None);
    }

    /// <summary>A user's export holds the login, the seats, the offers, the sessions and the pins, and only theirs.</summary>
    [Fact]
    public async Task A_user_export_holds_the_login_seats_offers_sessions_and_pins_of_that_user_only()
    {
        await SeedAsync();

        var sections = await AskAsync(PersonalDataSubjectKind.User, UserId);

        Assert.Equal(
            ["user", "memberships", "membershipOffers", "sessions", "pins"],
            sections.Select(section =>
            {
                return section.Name;
            }));
        Assert.All(sections, section =>
        {
            Assert.Equal("identity", section.Module);
        });
        var flat = Flatten(sections);
        Assert.Contains("user.username=olive", flat, StringComparison.Ordinal);
        Assert.Contains("user.email=olive@example.com", flat, StringComparison.Ordinal);
        Assert.Contains("sessions.ipAddress=203.0.113.9", flat, StringComparison.Ordinal);
        Assert.Contains("sessions.userAgent=Firefox", flat, StringComparison.Ordinal);
        Assert.Contains("pins.itemKey=sites", flat, StringComparison.Ordinal);
        Assert.DoesNotContain("stranger", flat, StringComparison.Ordinal);
    }

    /// <summary>No password hash, TOTP secret or token digest reaches a user export.</summary>
    /// <remarks>
    /// The probe is proved able to see a planted value first: the same flattening is run over a
    /// section that deliberately carries each secret, and must find all three.
    /// </remarks>
    [Fact]
    public async Task A_user_export_carries_none_of_the_planted_secrets_and_the_probe_can_see_them()
    {
        await SeedAsync();
        var leaking = new PersonalDataSection("identity", "leaking",
        [
            new PersonalDataRecord(new Dictionary<string, string?>
            {
                ["a"] = PlantedPasswordHash,
                ["b"] = PlantedTotpSecret,
                ["c"] = PlantedTokenHash,
            }),
        ]);
        var control = Flatten([leaking]);
        Assert.Contains(PlantedPasswordHash, control, StringComparison.Ordinal);
        Assert.Contains(PlantedTotpSecret, control, StringComparison.Ordinal);
        Assert.Contains(PlantedTokenHash, control, StringComparison.Ordinal);

        var flat = Flatten(await AskAsync(PersonalDataSubjectKind.User, UserId));

        Assert.DoesNotContain(PlantedPasswordHash, flat, StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedTotpSecret, flat, StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedTokenHash, flat, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", flat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", flat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("totp", flat, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An account's export holds its team and the offers made for it, and no secret.</summary>
    [Fact]
    public async Task An_account_export_holds_the_team_and_the_offers_and_no_secret()
    {
        await SeedAsync();

        var sections = await AskAsync(PersonalDataSubjectKind.Account, AccountId);

        Assert.Equal(["team", "membershipOffers"], sections.Select(section =>
        {
            return section.Name;
        }));
        var flat = Flatten(sections);
        Assert.Contains("team.username=olive", flat, StringComparison.Ordinal);
        Assert.Contains("team.email=olive@example.com", flat, StringComparison.Ordinal);
        Assert.Contains("team.role=Owner", flat, StringComparison.Ordinal);
        Assert.Contains("membershipOffers.identifier=olive@example.com", flat, StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedPasswordHash, flat, StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedTotpSecret, flat, StringComparison.Ordinal);
        Assert.DoesNotContain("stranger", flat, StringComparison.Ordinal);
    }

    /// <summary>A subject kind the provider holds nothing for is answered with an empty list.</summary>
    [Fact]
    public async Task An_unknown_subject_kind_is_answered_with_nothing()
    {
        await SeedAsync();

        Assert.Empty(await AskAsync(default, UserId));
    }

    /// <summary>Timestamps are ISO 8601 in UTC and a missing value stays missing.</summary>
    [Fact]
    public async Task Timestamps_are_iso_8601_in_utc_and_a_missing_value_is_null()
    {
        await SeedAsync();

        var user = (await AskAsync(PersonalDataSubjectKind.User, UserId))[0].Records[0].Fields;

        Assert.Equal("2026-11-07T09:30:00.0000000+00:00", user["createdAt"]);
        Assert.Null(user["lastLoginAt"]);
    }
}
