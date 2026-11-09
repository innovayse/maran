using System.ComponentModel.DataAnnotations;

namespace Maran.Host.Demo.Reset;

/// <summary>
/// Where the moment of the last reset is kept. How often the demonstration resets is
/// <see cref="Options.DemoOptions.ResetIntervalHours"/>, stated once.
/// </summary>
public sealed class DemoResetOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Demo:Reset";

    /// <summary>The file holding the instant of the last reset.</summary>
    [Required]
    public string MarkerPath { get; set; } = "/var/lib/maran-demo/last-reset";
}
