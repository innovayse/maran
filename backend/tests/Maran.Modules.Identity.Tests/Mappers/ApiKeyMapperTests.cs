using System.Text.Json;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Tests.Mappers;

/// <summary>Behavioural contract of <see cref="ApiKeyMapper"/>.</summary>
public sealed class ApiKeyMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A key is restated with its derived status at the given instant and an empty range list when unbound.</summary>
    [Fact]
    public async Task A_key_is_restated_with_its_status_at_the_instant_and_empty_ranges_when_unbound()
    {
        using var context = IdentityTestContext.Create();
        var (key, _) = await ApiKeySeed.AddAsync(context, Now.AddDays(-1), expiresAt: Now.AddHours(1));

        var active = ApiKeyMapper.From(key, Now);
        var expired = ApiKeyMapper.From(key, Now.AddHours(1));

        Assert.Equal(ApiKeyStatus.Active, active.Status);
        Assert.Equal(ApiKeyStatus.Expired, expired.Status);
        Assert.Empty(active.AllowedCidrs);
        Assert.Equal(key.Prefix, active.Prefix);
        Assert.Equal(key.Id, active.Id);
    }

    /// <summary>A revoked key reads as revoked even when it is also past its expiry.</summary>
    [Fact]
    public async Task A_revoked_key_reads_as_revoked_even_past_its_expiry()
    {
        using var context = IdentityTestContext.Create();
        var (key, _) = await ApiKeySeed.AddAsync(context, Now.AddDays(-2), expiresAt: Now.AddDays(-1), revoked: true);

        Assert.Equal(ApiKeyStatus.Revoked, ApiKeyMapper.From(key, Now).Status);
    }

    /// <summary>The created shape carries the plaintext once beside the key's own shape.</summary>
    [Fact]
    public async Task The_created_shape_carries_the_secret_beside_the_keys_shape()
    {
        using var context = IdentityTestContext.Create();
        var (key, plaintext) = await ApiKeySeed.AddAsync(context, Now);

        var created = ApiKeyMapper.Created(key, plaintext, Now);

        Assert.Equal(plaintext, created.Secret.Reveal());
        Assert.Equal(key.Id, created.Key.Id);
        Assert.DoesNotContain(plaintext, string.Join('|', created.Key.Name, created.Key.Prefix));
    }

    /// <summary>Printing the created shape never shows the secret, and the JSON writes the real value as a plain string.</summary>
    [Fact]
    public async Task Printing_the_created_shape_never_shows_the_secret()
    {
        using var context = IdentityTestContext.Create();
        var (key, plaintext) = await ApiKeySeed.AddAsync(context, Now);

        var created = ApiKeyMapper.Created(key, plaintext, Now);

        Assert.DoesNotContain(plaintext, created.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(plaintext[^43..], $"{created}", StringComparison.Ordinal);
        Assert.Contains("[redacted]", created.ToString(), StringComparison.Ordinal);

        var options = new JsonSerializerOptions();
        options.Converters.Add(new SensitiveStringJsonConverter());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(created, options));
        Assert.Equal(plaintext, json.RootElement.GetProperty("Secret").GetString());
    }

    /// <summary>The self shape lists the effective scopes, which include the implicit self scope, and no address or digest.</summary>
    [Fact]
    public async Task The_self_shape_lists_the_effective_scopes()
    {
        using var context = IdentityTestContext.Create();
        var (key, _) = await ApiKeySeed.AddAsync(context, Now, scopes: [ApiKeyScopes.ServerRead], allowedCidrs: ["203.0.113.0/24"]);

        var self = ApiKeyMapper.Self(key);

        Assert.Equal([ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead], self.Scopes);
        Assert.Equal(key.Name, self.Name);
        Assert.DoesNotContain(nameof(key.AllowedCidrs), self.GetType().GetProperties().Select(property => { return property.Name; }));
    }

    /// <summary>A null key is refused by each mapping that reads one.</summary>
    [Fact]
    public void A_null_key_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => { return ApiKeyMapper.From(null!, Now); });
        Assert.Throws<ArgumentNullException>(() => { return ApiKeyMapper.Self(null!); });
    }
}
