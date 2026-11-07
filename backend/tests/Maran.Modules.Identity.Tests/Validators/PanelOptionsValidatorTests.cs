using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Validators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Identity.Tests.Validators;

/// <summary>
/// Behavioral contract of <see cref="PanelOptionsValidator"/>: a malformed public address stops the
/// boot, a good one passes, and an empty one passes with a warning that says what is lost.
/// </summary>
public sealed class PanelOptionsValidatorTests
{
    /// <summary>A well-formed https origin passes and logs nothing.</summary>
    [Theory]
    [InlineData("https://panel.example.com:8443")]
    [InlineData("https://203.0.113.7:8443")]
    [InlineData("https://localhost:5173")]
    public void A_well_formed_origin_passes_without_a_warning(string value)
    {
        var logger = new RecordingLogger();

        var result = new PanelOptionsValidator(logger).Validate(name: null, new PanelOptions { PublicUrl = value });

        Assert.True(result.Succeeded);
        Assert.Empty(logger.Warnings);
    }

    /// <summary>An empty value passes and logs one warning naming the setting.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_value_passes_and_logs_one_warning_naming_the_setting(string value)
    {
        var logger = new RecordingLogger();

        var result = new PanelOptionsValidator(logger).Validate(name: null, new PanelOptions { PublicUrl = value });

        Assert.True(result.Succeeded);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Panel:PublicUrl", warning, StringComparison.Ordinal);
    }

    /// <summary>A malformed value fails, names the setting and never repeats the value.</summary>
    [Theory]
    [InlineData("http://panel.example.com")]
    [InlineData("https://user@panel.example.com")]
    [InlineData("https://panel.example.com/accept")]
    [InlineData("https://panel.example.com\nPanel__Other=1")]
    public void A_malformed_value_fails_names_the_setting_and_never_repeats_the_value(string value)
    {
        var logger = new RecordingLogger();

        var result = new PanelOptionsValidator(logger).Validate(name: null, new PanelOptions { PublicUrl = value });

        Assert.True(result.Failed);
        Assert.Contains("Panel:PublicUrl", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(value, result.FailureMessage, StringComparison.Ordinal);
        Assert.Empty(logger.Warnings);
    }

    /// <summary>A logger that keeps the formatted text of every warning it was given.</summary>
    private sealed class RecordingLogger : ILogger<PanelOptionsValidator>
    {
        /// <summary>The formatted warnings, in the order they were written.</summary>
        public List<string> Warnings { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullLogger.Instance.BeginScope(state);
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
