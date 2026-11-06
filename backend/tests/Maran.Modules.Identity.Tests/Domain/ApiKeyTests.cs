using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Domain;

/// <summary>Behavioural contract of <see cref="ApiKey"/>: issue validation, revocation, use throttling and derived status.</summary>
public sealed class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Issues a valid key, letting a test replace one input.</summary>
    private static ApiKey Issue(
        string name = "CI",
        string prefix = "0a1b2c3d",
        string tokenHash = "digest",
        string[]? scopes = null,
        string[]? allowedCidrs = null,
        DateTimeOffset? expiresAt = null)
    {
        return ApiKey.Issue(
            Guid.NewGuid(),
            name,
            prefix,
            tokenHash,
            scopes ?? [ApiKeyScopes.SelfRead],
            allowedCidrs,
            Guid.NewGuid(),
            Now,
            expiresAt);
    }

    /// <summary>A valid issue keeps the inputs, trims the name and starts active and unused.</summary>
    [Fact]
    public void Issuing_a_valid_key_keeps_the_inputs_and_starts_it_active_and_unused()
    {
        var key = Issue(name: "  CI  ", allowedCidrs: ["203.0.113.0/24"], expiresAt: Now.AddDays(30));

        Assert.Equal("CI", key.Name);
        Assert.Equal("0a1b2c3d", key.Prefix);
        Assert.Equal([ApiKeyScopes.SelfRead], key.Scopes);
        Assert.Equal(["203.0.113.0/24"], key.AllowedCidrs!);
        Assert.Equal(Now.AddDays(30), key.ExpiresAt);
        Assert.Null(key.LastUsedAt);
        Assert.Null(key.RevokedAt);
        Assert.Equal(ApiKeyStatus.Active, key.StatusAt(Now));
    }

    /// <summary>A key with no address list is not bound to any.</summary>
    [Fact]
    public void A_key_issued_without_address_ranges_is_not_bound()
    {
        Assert.Null(Issue().AllowedCidrs);
    }

    /// <summary>A name that is blank or longer than the limit is refused; one at the limit is accepted.</summary>
    [Fact]
    public void A_blank_or_overlong_name_is_refused_and_one_at_the_limit_is_accepted()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(name: "   ");
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(name: new string('a', ApiKeyLimits.MaxNameLength + 1));
        });
        Assert.Equal(ApiKeyLimits.MaxNameLength, Issue(name: new string('a', ApiKeyLimits.MaxNameLength)).Name.Length);
    }

    /// <summary>A prefix that is not eight lower-case hexadecimal characters is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("0a1b2c3")]
    [InlineData("0a1b2c3d4")]
    [InlineData("0A1B2C3D")]
    [InlineData("0a1b2c3g")]
    public void A_prefix_of_the_wrong_shape_is_refused(string prefix)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(prefix: prefix);
        });
    }

    /// <summary>An empty or oversized digest is refused.</summary>
    [Fact]
    public void An_empty_or_oversized_digest_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(tokenHash: string.Empty);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(tokenHash: new string('a', ApiKeyLimits.MaxDigestLength + 1));
        });
    }

    /// <summary>A scope outside the vocabulary, or a repeated one, is refused.</summary>
    [Fact]
    public void An_unknown_or_repeated_scope_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(scopes: ["no.such.scope"]);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(scopes: [ApiKeyScopes.SelfRead, ApiKeyScopes.SelfRead]);
        });
    }

    /// <summary>An address list is 1 to the limit of distinct, non-blank entries; an empty one is refused, not read as none.</summary>
    [Fact]
    public void An_address_list_over_the_limit_empty_blank_or_repeated_is_refused()
    {
        var atLimit = Enumerable.Range(0, ApiKeyLimits.MaxCidrs).Select(i =>
        {
            return $"10.0.{i}.0/24";
        }).ToArray();
        var overLimit = Enumerable.Range(0, ApiKeyLimits.MaxCidrs + 1).Select(i =>
        {
            return $"10.0.{i}.0/24";
        }).ToArray();

        Assert.Equal(ApiKeyLimits.MaxCidrs, Issue(allowedCidrs: atLimit).AllowedCidrs!.Length);
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(allowedCidrs: overLimit);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(allowedCidrs: []);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(allowedCidrs: [" "]);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(allowedCidrs: ["10.0.0.0/8", "10.0.0.0/8"]);
        });
    }

    /// <summary>An expiry at or before the issue instant is refused.</summary>
    [Fact]
    public void An_expiry_not_after_the_issue_instant_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(expiresAt: Now);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Issue(expiresAt: Now.AddSeconds(-1));
        });
        Assert.NotNull(Issue(expiresAt: Now.AddSeconds(1)).ExpiresAt);
    }

    /// <summary>Revoking records the instant and makes the key unusable.</summary>
    [Fact]
    public void Revoking_records_the_instant_and_makes_the_key_unusable()
    {
        var key = Issue();

        key.Revoke(Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), key.RevokedAt);
        Assert.False(key.IsUsable(Now.AddHours(1)));
    }

    /// <summary>A second revoke keeps the first instant.</summary>
    [Fact]
    public void Revoking_twice_keeps_the_first_revocation()
    {
        var key = Issue();

        key.Revoke(Now.AddHours(1));
        key.Revoke(Now.AddHours(5));

        Assert.Equal(Now.AddHours(1), key.RevokedAt);
    }

    /// <summary>The first use is always recorded.</summary>
    [Fact]
    public void The_first_use_is_recorded()
    {
        var key = Issue();

        Assert.True(key.RecordUse(Now, "203.0.113.7"));
        Assert.Equal(Now, key.LastUsedAt);
        Assert.Equal("203.0.113.7", key.LastUsedIp);
    }

    /// <summary>A use inside the throttle window changes nothing; at exactly the interval it is recorded.</summary>
    [Fact]
    public void A_use_inside_the_window_is_not_recorded_and_one_at_the_boundary_is()
    {
        var key = Issue();
        key.RecordUse(Now, "203.0.113.7");

        var justInside = Now + ApiKeyLimits.LastUseRecordingInterval - TimeSpan.FromTicks(1);
        Assert.False(key.RecordUse(justInside, "198.51.100.9"));
        Assert.Equal(Now, key.LastUsedAt);
        Assert.Equal("203.0.113.7", key.LastUsedIp);

        var boundary = Now + ApiKeyLimits.LastUseRecordingInterval;
        Assert.True(key.RecordUse(boundary, "198.51.100.9"));
        Assert.Equal(boundary, key.LastUsedAt);
        Assert.Equal("198.51.100.9", key.LastUsedIp);
    }

    /// <summary>A use stamped before the last recorded one is not recorded either.</summary>
    [Fact]
    public void A_use_stamped_before_the_last_recorded_one_is_not_recorded()
    {
        var key = Issue();
        key.RecordUse(Now, "203.0.113.7");

        Assert.False(key.RecordUse(Now.AddSeconds(-30), "198.51.100.9"));
        Assert.Equal(Now, key.LastUsedAt);
    }

    /// <summary>A key with no expiry stays active until it is revoked.</summary>
    [Fact]
    public void A_key_without_expiry_is_active_until_revoked()
    {
        var key = Issue();

        Assert.Equal(ApiKeyStatus.Active, key.StatusAt(Now.AddYears(10)));
        Assert.True(key.IsUsable(Now.AddYears(10)));
    }

    /// <summary>The key is active before its expiry and expired at and after it.</summary>
    [Fact]
    public void A_key_is_active_before_its_expiry_and_expired_from_it()
    {
        var key = Issue(expiresAt: Now.AddDays(1));

        Assert.Equal(ApiKeyStatus.Active, key.StatusAt(Now.AddDays(1).AddTicks(-1)));
        Assert.Equal(ApiKeyStatus.Expired, key.StatusAt(Now.AddDays(1)));
        Assert.False(key.IsUsable(Now.AddDays(2)));
    }

    /// <summary>A revoked key reads as revoked even after its expiry has also passed.</summary>
    [Fact]
    public void A_revoked_key_reads_as_revoked_even_when_it_has_also_expired()
    {
        var key = Issue(expiresAt: Now.AddDays(1));
        key.Revoke(Now.AddHours(1));

        Assert.Equal(ApiKeyStatus.Revoked, key.StatusAt(Now.AddDays(2)));
    }
}
