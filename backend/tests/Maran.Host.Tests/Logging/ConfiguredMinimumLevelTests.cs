using Maran.Host.Logging;
using Microsoft.Extensions.Configuration;
using Serilog.Events;

namespace Maran.Host.Tests.Logging;

/// <summary>The runtime switch starts at the level the configuration asks for.</summary>
public sealed class ConfiguredMinimumLevelTests
{
    /// <summary>The nested Default form is read.</summary>
    [Fact]
    public void The_nested_default_form_is_read()
    {
        Assert.Equal(LogEventLevel.Debug, Read(("Serilog:MinimumLevel:Default", "Debug")));
    }

    /// <summary>The bare form is read when there is no nested one.</summary>
    [Fact]
    public void The_bare_form_is_read_when_there_is_no_nested_one()
    {
        Assert.Equal(LogEventLevel.Warning, Read(("Serilog:MinimumLevel", "warning")));
    }

    /// <summary>Nothing configured means Information.</summary>
    [Fact]
    public void Nothing_configured_means_information()
    {
        Assert.Equal(LogEventLevel.Information, Read());
    }

    /// <summary>A name or number that is no level falls back to Information rather than refusing to start.</summary>
    /// <param name="configured">The configured value.</param>
    [Theory]
    [InlineData("Loud")]
    [InlineData("99")]
    public void A_value_that_is_no_level_falls_back_to_information(string configured)
    {
        Assert.Equal(LogEventLevel.Information, Read(("Serilog:MinimumLevel:Default", configured)));
    }

    /// <summary>Reads the level from an in-memory configuration of the given pairs.</summary>
    /// <param name="pairs">The configuration entries.</param>
    /// <returns>The level read.</returns>
    private static LogEventLevel Read(params (string Key, string Value)[] pairs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(pair =>
            {
                return new KeyValuePair<string, string?>(pair.Key, pair.Value);
            }))
            .Build();

        return ConfiguredMinimumLevel.Read(configuration);
    }
}
