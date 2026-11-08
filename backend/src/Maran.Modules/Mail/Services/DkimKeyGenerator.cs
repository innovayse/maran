using System.Security.Cryptography;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Models;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// Generates DKIM key pairs with the framework's own RSA, and nothing else: no dependency, no home-grown
/// cryptography (rules/security.md item 9).
/// </summary>
/// <remarks>
/// The size is <see cref="MailDefaults.DkimKeyBits"/>. Smaller keys are refused by large receivers and
/// larger ones do not fit a single DNS TXT string without splitting, which several resolvers handle
/// badly. The key is exported as PKCS#8, the form the agent's validated key type parses, and the public
/// half as <c>SubjectPublicKeyInfo</c>, the form a DKIM record's <c>p=</c> tag carries.
/// </remarks>
public sealed class DkimKeyGenerator : IDkimKeyGenerator
{
    /// <inheritdoc />
    public GeneratedDkimKey Generate()
    {
        using var rsa = RSA.Create(MailDefaults.DkimKeyBits);

        return new GeneratedDkimKey(
            new SensitiveString(rsa.ExportPkcs8PrivateKeyPem()),
            Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()));
    }
}
