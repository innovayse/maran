using System.ComponentModel.DataAnnotations;

namespace Maran.Host.Demo.World;

/// <summary>
/// The two published sign-ins of the demonstration. Both are public by design and appear on the demo's
/// login page; they are generated per deployment and read from configuration, never written in source.
/// </summary>
public sealed class DemoIdentityOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Demo:Identities";

    /// <summary>The administrator's login name.</summary>
    [Required]
    [RegularExpression(@"\A[a-z][a-z0-9_]{2,29}\z")]
    public string AdministratorUsername { get; set; } = "demoadmin";

    /// <summary>The administrator's address; nothing is sent to it in the demo.</summary>
    [Required]
    [EmailAddress]
    public string AdministratorEmail { get; set; } = "admin@demo.example.com";

    /// <summary>The administrator's password, at least as long as the panel's default minimum.</summary>
    [Required]
    [MinLength(12)]
    public string AdministratorPassword { get; set; } = string.Empty;

    /// <summary>The password of the featured customer's login; the login name is the featured account's name.</summary>
    [Required]
    [MinLength(12)]
    public string CustomerPassword { get; set; } = string.Empty;
}
