using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Builds the wire mail requests whose fields are more than a plain copy.</summary>
/// <remarks>Secrets are unwrapped here, straight onto the wire, and optional parts are mapped.</remarks>
internal static class AgentMailRequestMapper
{
    /// <summary>Names the wire spam action for a panel one.</summary>
    /// <param name="action">The panel's action.</param>
    /// <returns>The wire value; the panel always states the action, so the wire's UNSPECIFIED is never sent.</returns>
    internal static MailSpamAction ToWire(AgentMailSpamAction action)
    {
        return action switch
        {
            AgentMailSpamAction.KeepInInbox => MailSpamAction.KeepInInbox,
            _ => MailSpamAction.FileToJunk,
        };
    }

    /// <summary>Builds the wire out-of-office reply.</summary>
    /// <param name="autoresponder">The panel's reply.</param>
    /// <returns>
    /// The wire message; an absent start, end or interval is the wire's zero, which the contract defines
    /// as "from now", "until cleared" and "the agent's default" respectively.
    /// </returns>
    internal static MailAutoresponder ToWire(AgentMailAutoresponder autoresponder)
    {
        return new MailAutoresponder
        {
            Subject = autoresponder.Subject,
            Body = autoresponder.Body,
            StartsAtUnix = autoresponder.StartsAt?.ToUnixTimeSeconds() ?? 0,
            EndsAtUnix = autoresponder.EndsAt?.ToUnixTimeSeconds() ?? 0,
            ReplyIntervalDays = autoresponder.ReplyIntervalDays ?? 0,
        };
    }

    /// <summary>Builds the request that replaces a domain's whole alias set.</summary>
    /// <param name="accountUsername">System username of the account that owns the domain.</param>
    /// <param name="domain">The mail domain whose aliases are replaced.</param>
    /// <param name="aliases">Every alias of the domain.</param>
    /// <param name="catchAllTargets">The catch-all's targets, or <c>null</c> for none; an unset message is how the wire says "none".</param>
    /// <returns>The wire request.</returns>
    internal static SetMailAliasesRequest ToAliasRequest(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets)
    {
        var request = new SetMailAliasesRequest { AccountUsername = accountUsername, Domain = domain };
        foreach (var alias in aliases)
        {
            var entry = new MailAliasEntry { LocalPart = alias.LocalPart };
            entry.Targets.AddRange(alias.Targets);
            request.Aliases.Add(entry);
        }

        if (catchAllTargets is not null)
        {
            var catchAll = new MailCatchAll();
            catchAll.Targets.AddRange(catchAllTargets);
            request.CatchAll = catchAll;
        }

        return request;
    }

    /// <summary>Builds the request that turns the mail stack on.</summary>
    /// <param name="settings">The panel's settings.</param>
    /// <returns>The wire request; every secret in it is unwrapped here and nowhere else.</returns>
    internal static EnableMailRequest ToEnableRequest(AgentMailServerSettings settings)
    {
        return new EnableMailRequest
        {
            Hostname = settings.Hostname,
            Pop3Enabled = settings.Pop3Enabled,
            AntivirusEnabled = settings.AntivirusEnabled,
            MessageSizeMib = settings.MessageSizeMib,
            SpamGreylistScore = settings.SpamGreylistScore,
            SpamAddHeaderScore = settings.SpamAddHeaderScore,
            SpamRejectScore = settings.SpamRejectScore,
            AllowExternalForwarding = settings.AllowExternalForwarding,

            // The one place the value is unwrapped, straight onto the wire; empty means no webmail.
            WebmailMasterHash = settings.WebmailMasterHash?.Reveal() ?? string.Empty,
        };
    }

    /// <summary>Builds the request that configures one mail domain.</summary>
    /// <param name="arguments">The panel's arguments.</param>
    /// <returns>The wire request; every secret in it is unwrapped here and nowhere else.</returns>
    internal static ConfigureMailDomainRequest ToDomainRequest(AgentMailDomainArguments arguments)
    {
        return new ConfigureMailDomainRequest
        {
            AccountUsername = arguments.AccountUsername,
            Domain = arguments.Domain,
            DkimSelector = arguments.DkimSelector,

            // The one place the key is unwrapped, straight onto the wire.
            DkimPrivateKeyPem = arguments.DkimPrivateKeyPem.Reveal(),
        };
    }

    /// <summary>Builds the request that replaces an account's local submission binding.</summary>
    /// <param name="submission">The panel's binding.</param>
    /// <returns>The wire request; the lists are copied whole because the call replaces the host's sets.</returns>
    internal static SetLocalSubmissionRequest ToLocalSubmissionRequest(AgentLocalSubmission submission)
    {
        var request = new SetLocalSubmissionRequest
        {
            AccountUsername = submission.AccountUsername,
            SendLimitPerHour = submission.SendLimitPerHour,
        };
        request.Domains.AddRange(submission.Domains);
        request.MailboxLogins.AddRange(submission.MailboxLogins);

        return request;
    }

    /// <summary>Builds the request that creates or changes one mailbox.</summary>
    /// <param name="arguments">The panel's arguments.</param>
    /// <returns>The wire request; every secret in it is unwrapped here and nowhere else.</returns>
    internal static SetMailboxRequest ToMailboxRequest(AgentMailboxArguments arguments)
    {
        return new SetMailboxRequest
        {
            AccountUsername = arguments.AccountUsername,
            Domain = arguments.Domain,
            LocalPart = arguments.LocalPart,

            // The one place the hash is unwrapped, straight onto the wire; empty keeps the current hash.
            PasswordHash = arguments.PasswordHash?.Reveal() ?? string.Empty,
            QuotaMib = arguments.QuotaMib,
            Disabled = arguments.Disabled,
            SendLimitPerHour = arguments.SendLimitPerHour,
            SpamAction = AgentMailRequestMapper.ToWire(arguments.SpamAction),
        };
    }
}
