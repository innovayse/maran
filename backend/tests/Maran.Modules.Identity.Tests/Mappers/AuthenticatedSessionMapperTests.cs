using System.Text.Json;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;

namespace Maran.Modules.Identity.Tests.Mappers;

/// <summary>
/// Behavioural contract of <see cref="AuthenticatedSessionMapper"/>: the body the SPA reads carries the
/// first-password gate exactly as it carries the two-factor one, copied from the issued token.
/// </summary>
public sealed class AuthenticatedSessionMapperTests
{
    /// <summary>The instant everything is created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The serializer settings the API's web defaults give a body, built once (CA1869).</summary>
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the outcome of a sign-in for the given gate state.</summary>
    /// <param name="requiresPasswordChange">Whether the issued token is confined to the password change.</param>
    /// <param name="requiresTwoFactorSetup">Whether the issued token is confined to enrolment.</param>
    /// <returns>The outcome the mapper renders.</returns>
    private static AuthenticatedOutcome Outcome(bool requiresPasswordChange, bool requiresTwoFactorSetup)
    {
        var user = User.CreateWithPassword(Guid.NewGuid(), "owner", "owner@example.com", "hash", Now);
        return new AuthenticatedOutcome(
            new AccessToken("signed", Now.AddMinutes(15), requiresTwoFactorSetup, requiresPasswordChange),
            user,
            new IssuedSession(Guid.NewGuid(), "refresh", Now.AddDays(14), null));
    }

    /// <summary>The body carries the password-change flag copied from the token.</summary>
    [Fact]
    public void The_body_carries_the_password_change_flag_from_the_token()
    {
        var dto = AuthenticatedSessionMapper.From(Outcome(requiresPasswordChange: true, requiresTwoFactorSetup: false));

        Assert.True(dto.RequiresPasswordChange);
        Assert.False(dto.RequiresTwoFactorSetup);
    }

    /// <summary>The flag is false when the token is not confined, and independent of the two-factor flag.</summary>
    /// <remarks>The accepting control: a mapper that always said true would pass the test above.</remarks>
    [Fact]
    public void The_flag_is_false_when_the_token_is_free_and_independent_of_the_other_gate()
    {
        var free = AuthenticatedSessionMapper.From(Outcome(requiresPasswordChange: false, requiresTwoFactorSetup: false));
        var enrolling = AuthenticatedSessionMapper.From(Outcome(requiresPasswordChange: false, requiresTwoFactorSetup: true));

        Assert.False(free.RequiresPasswordChange);
        Assert.False(enrolling.RequiresPasswordChange);
        Assert.True(enrolling.RequiresTwoFactorSetup);
    }

    /// <summary>On the wire the flag is named <c>requiresPasswordChange</c>, beside <c>requiresTwoFactorSetup</c>.</summary>
    [Fact]
    public void On_the_wire_the_flag_is_named_requiresPasswordChange()
    {
        var dto = AuthenticatedSessionMapper.From(Outcome(requiresPasswordChange: true, requiresTwoFactorSetup: false));

        var json = JsonSerializer.Serialize(dto, WebOptions);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.GetProperty("requiresPasswordChange").GetBoolean());
        Assert.False(document.RootElement.GetProperty("requiresTwoFactorSetup").GetBoolean());
    }
}
