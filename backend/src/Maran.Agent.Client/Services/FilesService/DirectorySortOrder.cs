namespace Maran.Agent.Client.Services.FilesService;

/// <summary>The order the agent lists a directory in, within directories and within other entries.</summary>
/// <remarks>
/// Directories are always listed before other entries; this orders each group. The panel's own sort
/// names are mapped onto these three by the module, and a sort the agent cannot do is not offered.
/// </remarks>
public enum DirectorySortOrder
{
    /// <summary>By name, byte-wise ascending.</summary>
    Name,

    /// <summary>By size in bytes.</summary>
    Size,

    /// <summary>By modification time.</summary>
    Modified,
}
