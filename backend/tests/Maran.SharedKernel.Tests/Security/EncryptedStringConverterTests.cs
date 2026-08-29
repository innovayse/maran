using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>Behavioral contract of <see cref="EncryptedStringConverter"/>.</summary>
public sealed class EncryptedStringConverterTests
{
    /// <summary>A throwaway base64-encoded 256-bit key, valid only for these tests.</summary>
    private const string ValidKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Fact]
    public void Value_survives_a_conversion_round_trip()
    {
        var converter = new EncryptedStringConverter(new AesGcmEncryptionService(ValidKey));

        var stored = converter.ConvertToProvider("plain-secret") as string;
        var read = converter.ConvertFromProvider(stored!) as string;

        Assert.NotEqual("plain-secret", stored);
        Assert.Equal("plain-secret", read);
    }
}
