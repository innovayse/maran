using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Sdk.Contracts;

namespace Maran.Sdk.Tests.Contracts;

/// <summary>
/// The owner-login choice on the wire: the SPA is written against the camelCase strings
/// <c>invitation</c> and <c>initialPassword</c>, which the panel-wide string enum converter produces
/// without any attribute on the type.
/// </summary>
public sealed class OwnerLoginModeTests
{
    /// <summary>The converter the Host registers for every JSON surface.</summary>
    private static readonly JsonSerializerOptions PanelJson = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Each mode is written as its camelCase name.</summary>
    [Theory]
    [InlineData(OwnerLoginMode.Invitation, "\"invitation\"")]
    [InlineData(OwnerLoginMode.InitialPassword, "\"initialPassword\"")]
    public void Each_mode_is_written_as_its_camel_case_name(OwnerLoginMode mode, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(mode, PanelJson));
    }

    /// <summary>Each camelCase name reads back as its mode.</summary>
    [Theory]
    [InlineData("\"invitation\"", OwnerLoginMode.Invitation)]
    [InlineData("\"initialPassword\"", OwnerLoginMode.InitialPassword)]
    public void Each_camel_case_name_reads_back_as_its_mode(string json, OwnerLoginMode expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<OwnerLoginMode>(json, PanelJson));
    }

    /// <summary>A name that is not a mode is refused rather than read as the default.</summary>
    /// <remarks>The refusing half: a typo must not silently become an invitation.</remarks>
    [Fact]
    public void A_name_that_is_not_a_mode_is_refused()
    {
        Assert.Throws<JsonException>(() => { JsonSerializer.Deserialize<OwnerLoginMode>("\"password\"", PanelJson); });
    }

    /// <summary>The invitation is the zero value, so an absent field means the default.</summary>
    [Fact]
    public void The_invitation_is_the_zero_value()
    {
        Assert.Equal(OwnerLoginMode.Invitation, default);
    }
}
