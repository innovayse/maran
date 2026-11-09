namespace Maran.Agent.Client.Services.PhpService;

/// <summary>One catalogue PHP extension as one installed version's php-fpm sees it.</summary>
/// <param name="Name">The catalogue name, lowercase, e.g. <c>gd</c>; never a package name.</param>
/// <param name="Enabled">True when the version's php-fpm has the extension loaded.</param>
/// <param name="Availability">
/// What this host's OS family can do with the extension; <see cref="PhpExtensionAvailability.Unknown"/>
/// when the agent did not state it, which offers no action and is not the same as
/// <see cref="PhpExtensionAvailability.NotPackaged"/>.
/// </param>
public sealed record PhpExtensionDto(string Name, bool Enabled, PhpExtensionAvailability Availability);
