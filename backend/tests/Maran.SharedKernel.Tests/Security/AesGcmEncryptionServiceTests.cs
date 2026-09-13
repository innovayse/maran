using System.Security.Cryptography;
using System.Text;
using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>Behavioral contract of <see cref="AesGcmEncryptionService"/>.</summary>
public sealed class AesGcmEncryptionServiceTests
{
    /// <summary>A throwaway base64-encoded 256-bit key, valid only for these tests.</summary>
    private const string ValidKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Encrypted value decrypts back to the original plain text.</summary>
    [Fact]
    public void Encrypted_value_decrypts_back_to_the_original_plain_text()
    {
        var service = new AesGcmEncryptionService(ValidKey);

        var cipherText = service.Encrypt("super-secret-api-key");
        var plainText = service.Decrypt(cipherText);

        Assert.Equal("super-secret-api-key", plainText);
    }

    /// <summary>Tampered ciphertext fails to decrypt.</summary>
    [Fact]
    public void Tampered_ciphertext_fails_to_decrypt()
    {
        var service = new AesGcmEncryptionService(ValidKey);
        var cipherBytes = Convert.FromBase64String(service.Encrypt("super-secret-api-key"));

        // Flip a byte inside the ciphertext portion (past the 12-byte nonce and 16-byte tag),
        // so GCM's authentication check must reject it.
        cipherBytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(cipherBytes);

        Assert.Throws<AuthenticationTagMismatchException>(() =>
        {
            return service.Decrypt(tampered);
        });
    }

    /// <summary>Construction with a short key is rejected.</summary>
    [Fact]
    public void Construction_with_a_short_key_is_rejected()
    {
        var shortKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("too-short"));

        Assert.Throws<ArgumentException>(() =>
        {
            return new AesGcmEncryptionService(shortKey);
        });
    }

    /// <summary>Construction with malformed base64 is rejected.</summary>
    [Fact]
    public void Construction_with_malformed_base64_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new AesGcmEncryptionService("not-valid-base64!!!");
        });
    }

    /// <summary>Construction with an empty key is rejected.</summary>
    [Fact]
    public void Construction_with_an_empty_key_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new AesGcmEncryptionService(string.Empty);
        });
    }
}
