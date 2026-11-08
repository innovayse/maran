using System.Text.RegularExpressions;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Resources;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Mail.Domain.Policies;

/// <summary>
/// The one place the destinations of an alias, forwarder or catch-all are normalised and judged: what
/// spelling is stored, which lists are refused, which targets leave the account, and which kind a list of
/// targets makes the alias.
/// </summary>
/// <remarks>
/// <para>
/// <b>The grammar is the agent's <c>MailAliasTargets</c> / <c>MailAddress</c>, restated.</b> A target is
/// written into a whitespace-separated map of the MTA, so the agent refuses a quoted local part, a second
/// <c>@</c>, an address literal, <c>localhost</c> and the special-use suffixes, and accepts the extension
/// delimiter (<c>name+tag@example.org</c>) that a local part of a MAILBOX may not carry. The BCL parser
/// behind <see cref="EmailAddressRule"/> accepts several of the refused forms (<c>user@[127.0.0.1]</c>,
/// <c>"a b"@example.org</c>), so it is asked first and the agent's own shape is asked second: a list
/// that passes here is one the agent will not refuse, and the customer is told so with a typed code
/// instead of the agent's. The agent re-validates regardless, so a drift between the two costs a worse
/// message and never a weaker check.
/// </para>
/// <para>
/// <b>The list is judged in the agent's order.</b> The count is checked before any address is read, so an
/// enormous list costs nothing; then each address by position, the first refused one naming the failure;
/// a repeat is refused whatever its case, because two spellings of one destination would deliver twice.
/// </para>
/// <para>
/// <b>Whether a target is external is a fact about the ACCOUNT, not about the address.</b> A target is
/// internal when its domain is one of the account's own mail domains and external otherwise; whether an
/// external target is allowed at all is a host setting the handler reads and passes in. The kind follows
/// from that: a list that stays inside the account's domains is an alias, a list that leaves it is a
/// forwarder, and a caller who states the other one is refused rather than corrected.
/// </para>
/// </remarks>
public static class MailAliasTargetPolicy
{
    /// <summary>The longest whole address, in characters, from RFC 5321's forward-path.</summary>
    private const int MaxAddressLength = 254;

    /// <summary>The longest host name, in characters, the agent's domain grammar accepts.</summary>
    private const int MaxHostNameLength = 253;

    /// <summary>The longest label of a host name, in characters.</summary>
    private const int MaxLabelLength = 63;

    /// <summary>Names the agent reserves: the final label of a host name that can never be a public mail domain.</summary>
    private static readonly string[] SpecialUseSuffixes =
    [
        "local",
        "localdomain",
        "internal",
        "lan",
        "home.arpa",
        "invalid",
    ];

    /// <summary>
    /// The local part of a target: the mailbox grammar plus the extension delimiter, anchored with
    /// <c>\A</c>/<c>\z</c> because <c>$</c> also matches before a trailing newline.
    /// </summary>
    private static readonly Regex TargetLocalPartShape = new(
        @"\A[a-z0-9]([a-z0-9._+-]*[a-z0-9])?\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Folds a list of typed destinations to the one spelling the panel stores, refusing a list the agent would.</summary>
    /// <param name="targets">What the caller typed; <c>null</c> is read as an empty list.</param>
    /// <returns>
    /// The trimmed, lower-cased addresses in the order given, or <c>MailAliasTargetLimit</c> beyond
    /// <see cref="MailDefaults.MaxAliasTargets"/>, or <c>MailAliasTargetInvalid</c> for an empty list, a
    /// refused address or a repeat.
    /// </returns>
    public static Result<IReadOnlyList<string>> Normalise(IReadOnlyList<string>? targets)
    {
        if (targets is null || targets.Count == 0)
        {
            return Result<IReadOnlyList<string>>.Fail(
                Error.Of(nameof(ErrorMessages.MailAliasTargetInvalid), ErrorType.Validation));
        }

        // Before any address is read, as the agent does: the ceiling bounds a forwarder as an amplifier.
        if (targets.Count > MailDefaults.MaxAliasTargets)
        {
            return Result<IReadOnlyList<string>>.Fail(
                Error.Of(nameof(ErrorMessages.MailAliasTargetLimit), ErrorType.Validation));
        }

        var normalised = new List<string>(targets.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            var address = (target ?? string.Empty).Trim().ToLowerInvariant();

            if (!IsAcceptableAddress(address) || !seen.Add(address))
            {
                return Result<IReadOnlyList<string>>.Fail(
                    Error.Of(nameof(ErrorMessages.MailAliasTargetInvalid), ErrorType.Validation));
            }

            normalised.Add(address);
        }

        return Result<IReadOnlyList<string>>.Ok(normalised);
    }

    /// <summary>Whether a destination leaves the account: its domain is none of the account's own mail domains.</summary>
    /// <param name="address">A normalised destination.</param>
    /// <param name="accountDomainNames">The names of the account's own mail domains.</param>
    /// <returns>True when the destination is on a domain the account does not host.</returns>
    public static bool IsExternal(string address, IReadOnlyCollection<string> accountDomainNames)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(accountDomainNames);

        var domain = MailboxAddressPolicy.DomainOf(address);

        return !accountDomainNames.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The kind a list of destinations makes an alias: a forwarder when any of them leaves the account.</summary>
    /// <param name="targets">The normalised destinations.</param>
    /// <param name="accountDomainNames">The names of the account's own mail domains.</param>
    /// <returns><see cref="MailAliasKind.Forwarder"/> or <see cref="MailAliasKind.Alias"/>; never the catch-all, which is a place and not a derivation.</returns>
    public static MailAliasKind KindFor(IReadOnlyList<string> targets, IReadOnlyCollection<string> accountDomainNames)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(accountDomainNames);

        var leaves = targets.Any(target =>
        {
            return IsExternal(target, accountDomainNames);
        });

        return leaves ? MailAliasKind.Forwarder : MailAliasKind.Alias;
    }

    /// <summary>Judges a normalised list against the host's switch and the kind the caller stated.</summary>
    /// <param name="requestedKind">
    /// The kind the caller stated, or <c>null</c> when the caller states none (an update, which keeps the
    /// address and changes only where it goes). A catch-all is never contradicted by its targets.
    /// </param>
    /// <param name="targets">The normalised destinations.</param>
    /// <param name="accountDomainNames">The names of the account's own mail domains.</param>
    /// <param name="allowExternalForwarding">The host's switch for destinations outside the account's domains.</param>
    /// <returns>
    /// <c>null</c> when the list is acceptable; <c>MailAliasExternalTargetNotAllowed</c> when a target leaves
    /// the account and the host forbids it; <c>MailAliasKindMismatch</c> when the stated alias or forwarder
    /// is not what the targets make. The external refusal comes first, because it is the one that does not
    /// depend on how the caller described the request.
    /// </returns>
    public static Error? Judge(
        MailAliasKind? requestedKind,
        IReadOnlyList<string> targets,
        IReadOnlyCollection<string> accountDomainNames,
        bool allowExternalForwarding)
    {
        var derived = KindFor(targets, accountDomainNames);

        if (derived == MailAliasKind.Forwarder && !allowExternalForwarding)
        {
            return Error.Of(nameof(ErrorMessages.MailAliasExternalTargetNotAllowed), ErrorType.Validation);
        }

        if ((requestedKind is MailAliasKind.Alias or MailAliasKind.Forwarder) && requestedKind != derived)
        {
            return Error.Of(nameof(ErrorMessages.MailAliasKindMismatch), ErrorType.Validation);
        }

        return null;
    }

    /// <summary>Whether a trimmed, lower-cased address is one both the BCL parser and the agent's grammar accept.</summary>
    /// <param name="address">The candidate.</param>
    /// <returns>True when the agent would parse it.</returns>
    private static bool IsAcceptableAddress(string address)
    {
        if (address.Length > MaxAddressLength || !EmailAddressRule.IsAddress(address))
        {
            return false;
        }

        // Exactly one at sign: the agent refuses a second one rather than read it as a quoted local part.
        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at < 0 || address.IndexOf('@', at + 1) >= 0)
        {
            return false;
        }

        var localPart = address[..at];
        var domain = address[(at + 1)..];

        return localPart.Length <= MailDefaults.LocalPartMaxLength
            && TargetLocalPartShape.IsMatch(localPart)
            && !localPart.Contains("..", StringComparison.Ordinal)
            && IsAcceptableDomain(domain);
    }

    /// <summary>Whether a lower-cased domain is one the agent's mail-domain grammar accepts.</summary>
    /// <param name="domain">The domain half of a target.</param>
    /// <returns>True for a dotted host name of valid labels that is not reserved.</returns>
    private static bool IsAcceptableDomain(string domain)
    {
        if (domain.Length == 0
            || domain.Length > MaxHostNameLength
            || domain.Length > MailDefaults.DomainMaxLength
            || !domain.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var label in domain.Split('.'))
        {
            if (!IsAcceptableLabel(label))
            {
                return false;
            }
        }

        return !IsReserved(domain);
    }

    /// <summary>Whether one label of a domain is a non-empty run of letters, digits and inner hyphens.</summary>
    /// <param name="label">The label.</param>
    /// <returns>True when the label is well formed.</returns>
    private static bool IsAcceptableLabel(string label)
    {
        if (label.Length == 0 || label.Length > MaxLabelLength || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        return label.All(character =>
        {
            return character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
        });
    }

    /// <summary>Whether a well-formed domain is one the agent reserves: <c>localhost</c>, a special-use suffix or a numeric final label.</summary>
    /// <param name="domain">The domain.</param>
    /// <returns>True when the agent would refuse it as reserved.</returns>
    private static bool IsReserved(string domain)
    {
        var finalLabel = domain[(domain.LastIndexOf('.') + 1)..];
        if (finalLabel.Length > 0 && finalLabel.All(char.IsAsciiDigit))
        {
            return true;
        }

        if (domain == "localhost" || domain.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return true;
        }

        return SpecialUseSuffixes.Any(suffix =>
        {
            return domain == suffix || domain.EndsWith("." + suffix, StringComparison.Ordinal);
        });
    }
}
