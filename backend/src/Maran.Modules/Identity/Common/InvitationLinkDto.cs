using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// The one-time link an administrator hands to a hosting account's owner so they can set their
/// first password.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a credential, and it is the only place one is ever returned.</b> The panel stores only
/// a digest of the token inside the link, so this answer is the sole copy that will ever exist;
/// asking again mints a different token and retires this one. Nothing in the module writes this
/// value to a table, a log line or an audit entry.
/// </para>
/// <para>
/// <see cref="Link"/> is a <see cref="SensitiveString"/> for the reason every secret-carrying record
/// in this panel is: a record's generated <c>ToString()</c> prints its properties, and a plain
/// string would be printed by the first log call that mentioned the response. The panel-wide JSON
/// converter writes the real value to the wire.
/// </para>
/// </remarks>
/// <param name="Link">
/// The invitation link. Absolute (built from the configured panel address) when the installation has
/// a public address; otherwise a root-relative path that the SPA prefixes with its own origin.
/// </param>
/// <param name="ExpiresAt">When the link stops working.</param>
public sealed record InvitationLinkDto(SensitiveString Link, DateTimeOffset ExpiresAt);
