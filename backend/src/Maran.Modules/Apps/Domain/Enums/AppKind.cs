namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>What an app is for.</summary>
public enum AppKind
{
    /// <summary>The process that owns ports 80 and 443 for hosted sites: nginx or Apache.</summary>
    WebServer = 1,

    /// <summary>A language runtime sites run on: a PHP version or a Node.js major version.</summary>
    Runtime = 2,
}
