using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Models;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>A key generator that hands out distinguishable fake keys instantly, so no test pays for a real RSA key.</summary>
public sealed class StubDkimKeyGenerator : IDkimKeyGenerator
{
    /// <summary>How many keys have been made, which numbers the next one.</summary>
    private int _made;

    /// <inheritdoc />
    public GeneratedDkimKey Generate()
    {
        _made++;

        return new GeneratedDkimKey(
            new SensitiveString($"-----BEGIN PRIVATE KEY-----\nfake-{_made}\n-----END PRIVATE KEY-----\n"),
            $"PUBLICKEY{_made}");
    }
}
