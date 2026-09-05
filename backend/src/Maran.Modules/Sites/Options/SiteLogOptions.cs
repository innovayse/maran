using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Sites.Options;

/// <summary>Settings of the site-log stream, validated at startup.</summary>
public sealed class SiteLogOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Sites:Logs";

    /// <summary>
    /// How long the stream may go without writing anything before it sends a keep-alive comment.
    /// </summary>
    /// <remarks>
    /// It must stay comfortably below the read timeout of every proxy in front of the panel. The
    /// installer's nginx vhost sets <c>proxy_read_timeout</c> explicitly for this reason, but a
    /// customer's own proxy, or a load balancer, may impose one we never see — so the default is
    /// short enough (15 s) to survive the common 60 s default with room to spare, and long enough
    /// that a quiet log costs four small writes a minute.
    ///
    /// The consequence of getting this wrong is not a slow log: it is the proxy closing the
    /// connection with no <c>end</c> event, which the browser can only read as a stream that
    /// stopped saying nothing — the exact silent truncation the six endings exist to prevent.
    ///
    /// The upper bound is 30 seconds and that number is derived, not chosen. A heartbeat is only
    /// worth anything while it is shorter than the shortest read timeout in front of the panel, and
    /// the shortest one the panel can name is nginx's own 60-second default — so 30 is the largest
    /// value that still survives an unconfigured proxy, with one missed beat of margin. The bound
    /// used to be 600, which permitted a configuration twice as long as the read timeout the
    /// panel's OWN installed vhost sets (<c>installer/nginx/maran.conf</c>, 300 s): an operator
    /// raising this setting could reintroduce, by configuration, precisely the defect the heartbeat
    /// was added to close. A validated range whose ceiling is above the thing it must stay below is
    /// not a validation.
    /// </remarks>
    [Range(1, 30)]
    public int HeartbeatSeconds { get; set; } = 15;
}
