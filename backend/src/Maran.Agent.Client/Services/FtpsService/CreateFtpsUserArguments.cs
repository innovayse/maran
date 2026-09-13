using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>
/// Everything one FTPS login creation carries: the owning account, the customer's chosen suffix,
/// and the password the panel has just minted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a type and not three parameters.</b> A <c>record</c>'s compiler-generated
/// <c>ToString()</c> prints every property, so a carrier holding a <see cref="string"/> password
/// would leak it the first time anything interpolated the request into a log line — and nothing at
/// that call site would look wrong. The defence is therefore a property of the TYPE:
/// <see cref="SensitiveString.ToString"/> returns <c>[redacted]</c>, so the generated
/// <c>ToString()</c> prints <c>Password = [redacted]</c> with nothing overridden here.
/// </para>
/// <para>
/// A member added to this record later inherits that protection only if it is wrapped the same way.
/// A bare <c>string</c> added beside these three is a leak the compiler will not mention.
/// </para>
/// <para>
/// <b>Why creation takes a carrier while <c>SetPasswordAsync</c> takes loose parameters.</b> The
/// hazard is the generated printer, and only a record has one: a method signature carrying a
/// <see cref="SensitiveString"/> has nothing that prints it. So the carrier exists where the three
/// values must travel together, and the password-change call keeps the shipped SFTP client's shape
/// rather than growing a second record for symmetry's sake.
/// </para>
/// </remarks>
/// <param name="AccountUsername">
/// System username of the hosting account this login belongs to. The jail, the mount point and the
/// bind-mount unit's name are all derived from it by the agent.
/// </param>
/// <param name="FtpsUsername">
/// Login name suffix chosen by the customer. The agent namespaces it under the account, so the
/// created login is <c>&lt;account&gt;_&lt;suffix&gt;</c> and a suffix naming another tenant is
/// impossible rather than merely refused.
/// </param>
/// <param name="Password">
/// The password the panel minted for this login. Held in a non-printing wrapper, unwrapped exactly
/// once — onto the wire — and handed to the error translator so that the agent quoting it back
/// cannot put it in the panel's log.
/// </param>
public sealed record CreateFtpsUserArguments(
    string AccountUsername,
    string FtpsUsername,
    SensitiveString Password);
