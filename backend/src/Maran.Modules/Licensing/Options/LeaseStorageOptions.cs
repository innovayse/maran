using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Licensing.Options;

/// <summary>
/// Where the stored lease lives. A server's own value is never set: the default is the lease file next
/// to the licence file, and the seam exists so a test can point it at an isolated temporary directory
/// (the same shape as <see cref="LicenceStorageOptions"/>).
/// </summary>
public sealed class LeaseStorageOptions
{
    /// <summary>The configuration section the options bind from; shared with the licence path.</summary>
    public const string SectionName = "Licensing";

    /// <summary>The largest lease file the source will read, in bytes. A lease is a few hundred bytes.</summary>
    public const int MaxLeaseFileBytes = 16 * 1024;

    /// <summary>The absolute path of the lease file.</summary>
    [Required]
    [MinLength(1)]
    public string LeaseFilePath { get; set; } = "/var/lib/maran/lease.json";
}
