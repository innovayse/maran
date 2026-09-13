using System.Net;
using System.Net.Sockets;
using FluentValidation;
using Maran.Modules.Ftp.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Ftp.Commands.EnableFtps;

/// <summary>
/// Validates <see cref="EnableFtpsCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every rule here is re-validated inside the agent as well: the API's validation never
/// substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// <para>
/// Both values are written into <c>vsftpd.conf</c>, which is line-oriented. One embedded newline in
/// either turns a single directive into several, which is this panel's equivalent of SQL injection
/// (rules/security.md item 4) — and the directive an attacker would most want to append is
/// <c>force_local_logins_ssl=NO</c>, which leaves a configuration that still parses, still starts
/// and still answers a greeting while taking passwords in the clear. So the values are VALIDATED,
/// never escaped, and both rules are anchored with <c>\z</c> rather than <c>$</c>:
/// <see cref="HostNameRule"/> documents why, and <c>IPAddress.TryParse</c> refuses a trailing
/// newline on its own.
/// </para>
/// <para>
/// Each message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the caller gets the
/// generic failure instead.
/// </para>
/// </remarks>
public sealed class EnableFtpsCommandValidator : AbstractValidator<EnableFtpsCommand>
{
    /// <summary>Configures the field rules for <see cref="EnableFtpsCommand"/>.</summary>
    public EnableFtpsCommandValidator()
    {
        RuleFor(command => command.Hostname)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .WithMessage(nameof(ErrorMessages.FtpsHostnameTooLong))
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.FtpsHostnameInvalidFormat));

        // Optional, and the absence is spelled EMPTY — the one spelling the agent contract already
        // defines for "do not write the key at all". So the rule applies to a value that is there
        // and says nothing about one that is not; an operator not behind NAT leaves the field blank
        // and is not told their blank is an invalid address.
        RuleFor(command => command.PassiveAddress)
            .Must(BeAnIpv4AddressInItsOwnSpelling)
            .WithMessage(nameof(ErrorMessages.FtpsPassiveAddressInvalid))
            .When(command =>
            {
                return !string.IsNullOrEmpty(command.PassiveAddress);
            });
    }

    /// <summary>Whether a value is an IPv4 address written the one way this panel would write it.</summary>
    /// <param name="candidate">The address as the operator typed it.</param>
    /// <returns>True when the whole value parses as IPv4 and renders back byte-identically.</returns>
    /// <remarks>
    /// <para>
    /// A LITERAL, never a name: the value goes into <c>pasv_address</c>, and vsftpd would have to
    /// resolve a name at start-up — which makes whether the daemon comes back after a reboot depend
    /// on whether DNS answered first. An empty string would fail here rather than being treated as
    /// absence, so the two spellings of "no address" cannot both be accepted; the rule's
    /// <c>When</c> is what keeps the empty string from ever reaching this method.
    /// </para>
    /// <para>
    /// <b>IPv4 and not merely "an IP address", which is the narrowing this method exists for.</b>
    /// It used to accept anything <c>IPAddress.TryParse</c> parses, and the agent's
    /// <c>PassiveAddress</c> refuses the wrong family through its own error variant, because
    /// <c>pasv_address</c> is IPv4-only: the PASV reply of RFC 959 carries four decimal octets and
    /// has no IPv6 form, and vsftpd's IPv6 answer is EPSV, which advertises no address at all. So an
    /// operator who typed an IPv6 literal was accepted at the form and refused one process later,
    /// which reaches them as a server failure rather than the 400 that names the field. The family
    /// is read off the PARSED address rather than guessed from a colon, which is what also refuses
    /// the IPv4-mapped spelling <c>::ffff:203.0.113.7</c> — the same answer the agent's IPv6 parser
    /// gives it.
    /// </para>
    /// <para>
    /// <b>And the spelling is compared, which is the second half of the same seam.</b> .NET's parser
    /// accepts forms the agent's does not: <c>010.0.0.1</c> parses here and is refused there, and a
    /// leading-zero octet is read as octal by some resolvers and as decimal by others — two
    /// different hosts behind one spelling. Rather than enumerate those forms, the parsed address is
    /// rendered back and compared byte-for-byte, so the accepted set is exactly the addresses this
    /// panel would itself have written, which is the set the agent's <c>Ipv4Addr</c> grammar
    /// accepts. It is also what refuses surrounding whitespace and a trailing newline without a
    /// character sweep anybody has to keep complete.
    /// </para>
    /// </remarks>
    private static bool BeAnIpv4AddressInItsOwnSpelling(string candidate)
    {
        return IPAddress.TryParse(candidate, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork
            && string.Equals(address.ToString(), candidate, StringComparison.Ordinal);
    }
}
