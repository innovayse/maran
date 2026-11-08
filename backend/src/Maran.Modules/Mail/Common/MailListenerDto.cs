namespace Maran.Modules.Mail.Common;

/// <summary>One address and port a mail daemon was observed listening on.</summary>
/// <param name="Address">The address the daemon is bound to.</param>
/// <param name="Port">The TCP port.</param>
public sealed record MailListenerDto(string Address, uint Port);
