namespace Maran.Modules.Monitoring.Domain.Enums;

/// <summary>How the panel reaches a server it manages (issue #319).</summary>
/// <remarks>
/// A closed set that today holds one value, because the panel manages only the machine it runs on.
/// Fleet mode (issue #180) adds the kind of a server reached over the network when it lands; adding
/// a value here is that issue's work, not a speculative member now (rules/architecture.md YAGNI).
/// </remarks>
public enum ServerKind
{
    /// <summary>The machine the panel itself runs on, driven through the local agent.</summary>
    Local = 1,
}
