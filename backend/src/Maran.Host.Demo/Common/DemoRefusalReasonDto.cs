namespace Maran.Host.Demo.Common;

/// <summary>Why one area of the panel is read-only in the demonstration.</summary>
/// <param name="Area">
/// The area id: <c>securityPolicy</c>, <c>smtp</c>, <c>panelCertificate</c>, <c>panelUpdate</c>,
/// <c>twoFactor</c>, <c>apiKeys</c> or <c>licence</c>.
/// </param>
/// <param name="Reason">The sentence shown beside the blocked controls.</param>
public sealed record DemoRefusalReasonDto(string Area, string Reason);
