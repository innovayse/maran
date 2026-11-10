using System.Security.Cryptography;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// The catalogue's signature and freshness checks. Each refusal is paired with the acceptance of the same
/// bytes under the right key or the right age, so a checker that refused everything could not pass.
/// </summary>
public sealed class CatalogueSignatureCheckerTests
{
    /// <summary>Some bytes standing in for a response body.</summary>
    private static readonly byte[] Body = "{\"items\":[]}"u8.ToArray();

    /// <summary>A catalogue signed by the pinned key is accepted.</summary>
    [Fact]
    public async Task A_catalogue_signed_by_the_pinned_key_is_accepted()
    {
        using var world = new MarketplaceTestWorld();

        var result = await world.Checker.VerifyAsync(Body, world.Signer.Sign(Body), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A catalogue signed by a key that is not pinned is refused, and the same bytes under the pinned key are accepted.</summary>
    [Fact]
    public async Task A_catalogue_signed_by_another_key_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        using var other = new MarketplaceSigner();

        var refused = await world.Checker.VerifyAsync(Body, other.Sign(Body), CancellationToken.None);
        var accepted = await world.Checker.VerifyAsync(Body, world.Signer.Sign(Body), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, refused.Error);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>A catalogue with no signature header, an empty one or one that is not base64 is refused as unsigned.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public async Task An_unsigned_catalogue_is_refused(string? header)
    {
        using var world = new MarketplaceTestWorld();

        var result = await world.Checker.VerifyAsync(Body, header, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, result.Error);
    }

    /// <summary>A body changed after signing is refused, and the unchanged body under the same signature is accepted.</summary>
    [Fact]
    public async Task A_body_changed_after_signing_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        var signature = world.Signer.Sign(Body);
        var tampered = "{\"items\":[1]}"u8.ToArray();

        var refused = await world.Checker.VerifyAsync(tampered, signature, CancellationToken.None);
        var accepted = await world.Checker.VerifyAsync(Body, signature, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, refused.Error);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>With no pinned key file nothing can be verified, so the checker fails closed, and with the file it verifies.</summary>
    [Fact]
    public async Task A_missing_key_file_fails_closed()
    {
        using var world = new MarketplaceTestWorld(writeKeyFile: false);
        var signature = world.Signer.Sign(Body);

        var missing = await world.Checker.VerifyAsync(Body, signature, CancellationToken.None);
        await File.WriteAllTextAsync(world.KeyFile, world.Signer.PublicKeyPem);
        var present = await world.Checker.VerifyAsync(Body, signature, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueKeyMissing, missing.Error);
        Assert.True(present.IsSuccess);
    }

    /// <summary>A key file that is a directory, empty, a P-256 key, a private key or has a stray block is unusable.</summary>
    [Fact]
    public async Task A_key_file_that_is_not_a_p384_public_key_is_unusable()
    {
        using var world = new MarketplaceTestWorld();
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var signature = world.Signer.Sign(Body);

        var unusable = new[]
        {
            string.Empty,
            "garbage",
            p256.ExportSubjectPublicKeyInfoPem(),
            p384.ExportPkcs8PrivateKeyPem(),
            world.Signer.PublicKeyPem + p256.ExportSubjectPublicKeyInfoPem(),
        };

        foreach (var content in unusable)
        {
            await File.WriteAllTextAsync(world.KeyFile, content);
            var result = await world.Checker.VerifyAsync(Body, signature, CancellationToken.None);
            Assert.Equal(MarketplaceClientRefusals.CatalogueKeyMissing, result.Error);
        }

        File.Delete(world.KeyFile);
        Directory.CreateDirectory(world.KeyFile);
        var directory = await world.Checker.VerifyAsync(Body, signature, CancellationToken.None);
        Assert.Equal(MarketplaceClientRefusals.CatalogueKeyMissing, directory.Error);

        Directory.Delete(world.KeyFile);
        await File.WriteAllTextAsync(world.KeyFile, world.Signer.PublicKeyPem);
        Assert.True((await world.Checker.VerifyAsync(Body, signature, CancellationToken.None)).IsSuccess);
    }

    /// <summary>A pinned file holding two keys accepts a signature by either.</summary>
    [Fact]
    public async Task A_pinned_file_holding_two_keys_accepts_either()
    {
        using var world = new MarketplaceTestWorld();
        using var second = new MarketplaceSigner();
        // A key file is lines: PEM export ends without a line break, so the second block starts on a new line.
        await File.WriteAllTextAsync(world.KeyFile, world.Signer.PublicKeyPem + "\n" + second.PublicKeyPem);

        var byFirst = await world.Checker.VerifyAsync(Body, world.Signer.Sign(Body), CancellationToken.None);
        var bySecond = await world.Checker.VerifyAsync(Body, second.Sign(Body), CancellationToken.None);

        Assert.True(byFirst.IsSuccess);
        Assert.True(bySecond.IsSuccess);
    }

    /// <summary>A document signed less than 24 hours ago is fresh; signed more than 24 hours ago it is stale.</summary>
    [Fact]
    public void A_catalogue_older_than_24h_is_stale()
    {
        using var world = new MarketplaceTestWorld();
        var now = world.Clock.UtcNow;

        var fresh = world.Checker.CheckFreshness(Page(now.AddHours(-23), now.AddHours(1)));
        var stale = world.Checker.CheckFreshness(Page(now.AddHours(-25), now.AddHours(1)));

        Assert.True(fresh.IsSuccess);
        Assert.Equal(MarketplaceClientRefusals.CatalogueStale, stale.Error);
    }

    /// <summary>A document past its own validUntil is stale, and one still inside it is fresh.</summary>
    [Fact]
    public void A_catalogue_past_its_validity_is_stale()
    {
        using var world = new MarketplaceTestWorld();
        var now = world.Clock.UtcNow;

        var inside = world.Checker.CheckFreshness(Page(now.AddHours(-1), now.AddMinutes(1)));
        var past = world.Checker.CheckFreshness(Page(now.AddHours(-1), now.AddMinutes(-1)));

        Assert.True(inside.IsSuccess);
        Assert.Equal(MarketplaceClientRefusals.CatalogueStale, past.Error);
    }

    /// <summary>A document signed in the future would defeat the age check, so it is stale; one signed a moment ahead of a skewed clock is fresh.</summary>
    [Fact]
    public void A_catalogue_signed_in_the_future_is_stale()
    {
        using var world = new MarketplaceTestWorld();
        var now = world.Clock.UtcNow;

        var skewed = world.Checker.CheckFreshness(Page(now.AddMinutes(2), now.AddHours(1)));
        var future = world.Checker.CheckFreshness(Page(now.AddHours(3), now.AddHours(30)));

        Assert.True(skewed.IsSuccess);
        Assert.Equal(MarketplaceClientRefusals.CatalogueStale, future.Error);
    }

    /// <summary>A key list signed only by the key it announces is refused, because that key is not yet trusted.</summary>
    [Fact]
    public async Task A_key_rotation_signed_only_by_the_new_key_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        using var newKey = new MarketplaceSigner();
        var now = world.Clock.UtcNow;
        var keys = MarketplaceDocuments.Keys(now, now.AddHours(1), newKey.PublicKeyPem);
        var reader = new SignedResponseReader(world.Checker);

        var bySelf = await Announce(world, reader, newKey, keys);
        var byPinned = await Announce(world, reader, world.Signer, keys);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, bySelf.Error);
        Assert.True(byPinned.IsSuccess);
    }

    /// <summary>After a rotation signed by a pinned key, an answer signed by the new key is accepted, and the status counts it.</summary>
    [Fact]
    public async Task A_rotation_signed_by_a_pinned_key_makes_the_new_key_trusted()
    {
        using var world = new MarketplaceTestWorld();
        using var newKey = new MarketplaceSigner();
        var now = world.Clock.UtcNow;
        var keys = MarketplaceDocuments.Keys(now, now.AddHours(1), newKey.PublicKeyPem);
        var before = await world.Checker.VerifyAsync(Body, newKey.Sign(Body), CancellationToken.None);

        await Announce(world, new SignedResponseReader(world.Checker), world.Signer, keys);
        var after = await world.Checker.VerifyAsync(Body, newKey.Sign(Body), CancellationToken.None);
        var status = await world.Checker.GetStatusAsync(CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, before.Error);
        Assert.True(after.IsSuccess);
        Assert.Equal(new MarketplaceKeyStatus(1, 1), status.Value);
    }

    /// <summary>An announced key that is not a P-384 public key refuses the whole list.</summary>
    [Fact]
    public void An_announced_key_that_is_not_p384_refuses_the_list()
    {
        using var world = new MarketplaceTestWorld();
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = world.Clock.UtcNow;
        var bad = new MarketplaceKeySet([new MarketplaceKeyEntry("k", p256.ExportSubjectPublicKeyInfoPem())], now, now.AddHours(1));
        var good = new MarketplaceKeySet([new MarketplaceKeyEntry("k", world.Signer.PublicKeyPem)], now, now.AddHours(1));

        Assert.Equal(MarketplaceClientRefusals.ResponseInvalid, world.Checker.AcceptRotation(bad).Error);
        Assert.True(world.Checker.AcceptRotation(good).IsSuccess);
    }

    /// <summary>Builds a catalogue page with the given dates.</summary>
    /// <param name="signedAt">When it was signed.</param>
    /// <param name="validUntil">Until when it is good.</param>
    /// <returns>An empty page.</returns>
    private static CataloguePage Page(DateTimeOffset signedAt, DateTimeOffset validUntil)
    {
        return new CataloguePage([], signedAt, validUntil);
    }

    /// <summary>Signs a key list with a key, reads it as the client does, and hands the keys to the checker.</summary>
    /// <param name="world">The test world.</param>
    /// <param name="reader">The signed-response reader.</param>
    /// <param name="signer">The key signing the list.</param>
    /// <param name="keys">The list document.</param>
    /// <returns>Success when the list verified and was accepted; the verification failure otherwise.</returns>
    private static async Task<Result> Announce(
        MarketplaceTestWorld world,
        SignedResponseReader reader,
        MarketplaceSigner signer,
        object keys)
    {
        var bytes = MarketplaceTestWorld.BytesOf(keys);
        var answer = new MarketplaceResponse(200, bytes, signer.Sign(bytes), null);

        var read = await reader.ReadDocumentAsync<MarketplaceKeySet>(answer, CancellationToken.None);
        return read.IsSuccess ? world.Checker.AcceptRotation(read.Value) : Result.Fail(read.Error!);
    }
}
