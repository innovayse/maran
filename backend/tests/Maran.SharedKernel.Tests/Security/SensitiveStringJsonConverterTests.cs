using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>
/// The one sanctioned way a minted secret becomes wire text: the JSON converter that delivers a
/// database or SFTP password to the customer who asked for it.
/// </summary>
/// <remarks>
/// The value this carries is unrecoverable — the panel mints the password, hands it over once and
/// keeps no plaintext — so the failure has no second chance: a converter that wrote the mask would
/// leave the customer holding <c>[redacted]</c> as their password, and a converter that lost its
/// registration would do the same silently, because <see cref="SensitiveString"/>'s own
/// <c>ToString</c> is the mask by design. Nothing asserted any of this before this file.
/// </remarks>
public sealed class SensitiveStringJsonConverterTests
{
    /// <summary>What a secret renders as everywhere the converter has not been asked.</summary>
    private const string Mask = "[redacted]";

    /// <summary>The serializer options a response is written with, converter included.</summary>
    private static readonly JsonSerializerOptions Options = BuildOptions();

    /// <summary>A wrapped secret is written to json as its real value and not as the mask.</summary>
    [Fact]
    public void A_wrapped_secret_is_written_to_json_as_its_real_value_and_not_as_the_mask()
    {
        var written = JsonSerializer.Serialize(new SensitiveString("s3cret-value"), Options);

        Assert.Equal("\"s3cret-value\"", written);
        Assert.DoesNotContain(Mask, written, StringComparison.Ordinal);
    }

    /// <summary>The mask is what the same value renders as without the converter asked explicitly.</summary>
    [Fact]
    public void The_mask_is_what_the_same_value_renders_as_without_the_converter_asked_explicitly()
    {
        // The inverse control. The assertion above only means something if the mask is what this
        // value would otherwise become — otherwise "the output is not the mask" is true of every
        // possible implementation, including a broken one.
        Assert.Equal(Mask, new SensitiveString("s3cret-value").ToString());
    }

    /// <summary>A secret carried inside a response record reaches the wire whole.</summary>
    [Fact]
    public void A_secret_carried_inside_a_response_record_reaches_the_wire_whole()
    {
        // How it is actually used: the secret is one property of a created-resource DTO, not a
        // top-level value, so the converter has to be reached through the object writer too.
        const string minted = "p@ss word/+=";

        var written = JsonSerializer.Serialize(new CreatedThing("app_db", new SensitiveString(minted)), Options);
        var read = JsonSerializer.Deserialize<CreatedThing>(written, Options);

        Assert.DoesNotContain(Mask, written, StringComparison.Ordinal);
        Assert.NotNull(read);
        Assert.Equal(minted, read.Password.Reveal());
    }

    /// <summary>A secret full of json metacharacters is escaped rather than corrupted.</summary>
    [Fact]
    public void A_secret_full_of_json_metacharacters_is_escaped_rather_than_corrupted()
    {
        // A generated password is arbitrary text, and text that does not survive its own escaping
        // is a password the customer cannot use. Round-tripping is the only honest check here.
        var secret = "a\"b\\c\nd\te fég\U0001F600h";

        var written = JsonSerializer.Serialize(new SensitiveString(secret), Options);
        var read = JsonSerializer.Deserialize<SensitiveString>(written, Options);

        Assert.NotNull(read);
        Assert.Equal(secret, read.Reveal());
    }

    /// <summary>An empty secret round trips as an empty string rather than as null.</summary>
    [Fact]
    public void An_empty_secret_round_trips_as_an_empty_string_rather_than_as_null()
    {
        var read = JsonSerializer.Deserialize<SensitiveString>("\"\"", Options);

        Assert.NotNull(read);
        Assert.Equal(string.Empty, read.Reveal());
    }

    /// <summary>A json null is read as a missing secret rather than as a secret spelled null.</summary>
    [Fact]
    public void A_json_null_is_read_as_a_missing_secret_rather_than_as_a_secret_spelled_null()
    {
        Assert.Null(JsonSerializer.Deserialize<SensitiveString>("null", Options));
    }

    /// <summary>A response carrying one is a resource description and one secret.</summary>
    /// <param name="Name">The resource the panel created.</param>
    /// <param name="Password">The password it minted, shown exactly once.</param>
    private sealed record CreatedThing(string Name, SensitiveString Password);

    /// <summary>Builds the options a response is written with.</summary>
    /// <returns>Serializer options carrying the converter under test.</returns>
    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions();

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new SensitiveStringJsonConverter());

        return options;
    }
}
