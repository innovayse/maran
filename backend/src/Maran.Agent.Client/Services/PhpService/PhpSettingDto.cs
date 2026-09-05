namespace Maran.Agent.Client.Services.PhpService;

/// <summary>One php.ini override the customer asked for, as the panel stores it.</summary>
/// <remarks>
/// The panel owns these values; the agent re-validates every name against its own whitelist and
/// every value against that name's bounds, and refuses a name it does not know rather than
/// dropping it.
/// </remarks>
/// <param name="Name">The setting name, e.g. <c>memory_limit</c>.</param>
/// <param name="Value">The value as php.ini writes it, e.g. <c>256M</c>.</param>
public sealed record PhpSettingDto(string Name, string Value);
