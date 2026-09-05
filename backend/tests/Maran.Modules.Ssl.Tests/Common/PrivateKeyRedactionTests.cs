using Maran.Modules.Ssl.Commands.InstallCustomCertificate;
using Maran.Modules.Ssl.Controllers.Requests;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Models;

namespace Maran.Modules.Ssl.Tests.Common;

/// <summary>
/// The one property every type that carries key material must have: rendering it as text does not
/// reveal the material (rules/security.md item 8).
/// </summary>
/// <remarks>
/// These are not paranoia about a call site nobody would write. A record's synthesised
/// <c>ToString</c> prints every property, and the places that call <c>ToString</c> on an object are
/// exactly the places nobody writes on purpose: a structured logger given an object, an interpolated
/// exception message, a message-bus diagnostic, a debugger's watch window. The types below are all
/// records or entities holding a key, so each one overrides <c>ToString</c> and each override is
/// pinned here.
/// </remarks>
public sealed class PrivateKeyRedactionTests
{
    /// <summary>A key that is unmistakable if it ever appears in a rendered string.</summary>
    private const string Key = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0CANARY\n-----END PRIVATE KEY-----";

    /// <summary>The issued material does not render its private key.</summary>
    [Fact]
    public void The_issued_material_does_not_render_its_private_key()
    {
        var material = new IssuedCertificate("cert", Key, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var rendered = $"{material}";

        Assert.DoesNotContain("CANARY", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", rendered, StringComparison.Ordinal);
    }

    /// <summary>The custom install command does not render its private key.</summary>
    [Fact]
    public void The_custom_install_command_does_not_render_its_private_key()
    {
        var command = new InstallCustomCertificateCommand("example.com", "cert", Key, "203.0.113.7", "tests");

        var rendered = $"{command}";

        Assert.DoesNotContain("CANARY", rendered, StringComparison.Ordinal);
        Assert.Contains("example.com", rendered, StringComparison.Ordinal);
    }

    /// <summary>The custom install request does not render its private key.</summary>
    [Fact]
    public void The_custom_install_request_does_not_render_its_private_key()
    {
        var request = new InstallCustomCertificateRequest("example.com", "cert", Key);

        var rendered = $"{request}";

        Assert.DoesNotContain("CANARY", rendered, StringComparison.Ordinal);
    }

    /// <summary>The acme account does not render its account key.</summary>
    [Fact]
    public void The_acme_account_does_not_render_its_account_key()
    {
        var account = new AcmeAccount(
            Guid.NewGuid(),
            "https://acme.example.com/directory",
            "https://acme.example.com/acct/1",
            Key,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var rendered = $"{account}";

        Assert.DoesNotContain("CANARY", rendered, StringComparison.Ordinal);
    }

    /// <summary>The stored certificate carries no material to render at all.</summary>
    [Fact]
    public void The_stored_certificate_carries_no_material_to_render_at_all()
    {
        // Not a ToString test: the entity has no property that could hold a key, which is the
        // stronger guarantee. If one is ever added, this assertion is what fails.
        var properties = typeof(Certificate).GetProperties().Select(property =>
        {
            return property.Name;
        });

        Assert.DoesNotContain("PrivateKeyPem", properties, StringComparer.Ordinal);
        Assert.DoesNotContain("CertificatePem", properties, StringComparer.Ordinal);
    }
}
