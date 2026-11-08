namespace Maran.Agent.Client.Services.FilesService;

/// <summary>The archive container formats the agent can write and read; a closed set, never a command line.</summary>
public enum AgentArchiveFormat
{
    /// <summary>A zip archive.</summary>
    Zip,

    /// <summary>A gzip-compressed tar archive.</summary>
    TarGz,
}
