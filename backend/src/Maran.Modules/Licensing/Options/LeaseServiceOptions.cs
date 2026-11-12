using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Licensing.Options;

/// <summary>
/// Where the panel asks for its licence lease and how long it waits for the answer. The default is the
/// documented host (rules/security.md item 10); an operator can point the panel at another host, and the panel
/// then talks to that one host only. Bound from the same <c>Licensing</c> section as the file paths.
/// </summary>
public sealed class LeaseServiceOptions
{
    /// <summary>The configuration section the options bind from; shared with the licence and lease paths.</summary>
    public const string SectionName = "Licensing";

    /// <summary>The name of the <see cref="HttpClient"/> the renewal sends through; the Host attaches the outbound pipeline to it.</summary>
    public const string HttpClientName = "licence-lease";

    /// <summary>The documented licence service host.</summary>
    public const string DefaultBaseUrl = "https://api-marketplace-maran.innovayse.com";

    /// <summary>The path of the lease request below the host; it is part of the contract in <c>docs/marketplace/api-v1.md</c>.</summary>
    public const string RequestPath = "/api/v1/lease";

    /// <summary>The shortest request deadline an operator can set, in seconds.</summary>
    public const int MinRequestTimeoutSeconds = 5;

    /// <summary>The longest request deadline an operator can set, in seconds.</summary>
    public const int MaxRequestTimeoutSeconds = 120;

    /// <summary>The longest install deadline an operator can set, in seconds; it must stay under a reverse proxy's read timeout.</summary>
    public const int MaxInstallRequestDeadlineSeconds = 30;

    /// <summary>
    /// The licence service's origin: <c>https://host[:port]</c> and nothing after it. Checked by
    /// <c>PublicUrlRule</c> when a request is made, not at start-up, so a typo can never stop the panel booting
    /// (the core never depends on the licence); a value that fails the rule makes the renewal skip its pass.
    /// </summary>
    public string LeaseServiceBaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>How long one request may take before it is given up, in seconds.</summary>
    [Range(MinRequestTimeoutSeconds, MaxRequestTimeoutSeconds)]
    public int LeaseRequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How long installing a licence waits for its own lease request, in seconds, counting any wait behind a
    /// scheduled renewal. Shorter than the proxy's read timeout, so the install always answers; a request that
    /// does not finish in time is reported as such and the schedule tries again.
    /// </summary>
    [Range(MinRequestTimeoutSeconds, MaxInstallRequestDeadlineSeconds)]
    public int InstallRequestDeadlineSeconds { get; set; } = 12;
}
