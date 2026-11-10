namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The web server found on a source.</summary>
/// <remarks>
/// "None found, or the probe could not run" has no value here; <see cref="AgentTestedSource.WebServer"/>
/// is <c>null</c> for it.
/// </remarks>
public enum SourceWebServer
{
    /// <summary>nginx.</summary>
    Nginx = 0,

    /// <summary>Apache.</summary>
    Apache = 1,
}
