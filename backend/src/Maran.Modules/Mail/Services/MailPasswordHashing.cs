using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// Turns a mailbox password into the hash the HOST'S IMAP server verifies, or says why that cannot be done.
/// </summary>
/// <remarks>
/// <para>
/// The scheme is a fact of the host's distribution, never a choice of the panel: a hash in a scheme the
/// IMAP server was not built with is accepted, stored, and then refuses the customer's every login. So the
/// host is asked which scheme it verified (<see cref="AgentMailStatus.PasswordScheme"/> together with
/// <see cref="AgentMailStatus.PasswordSchemeVerified"/>) before anything is hashed.
/// </para>
/// <para>
/// Only Argon2id is produced here, by the panel's one password hasher (rules/security.md item 9). A host
/// whose IMAP server verifies only bcrypt gets a typed refusal rather than a weaker hash: producing bcrypt
/// needs a new dependency and an explicit ruling that item 9's Argon2id requirement may be relaxed, which is
/// the owner's decision and not this module's.
/// </para>
/// </remarks>
public sealed class MailPasswordHashing
{
    /// <summary>The panel's Argon2id hasher; the hash it makes is the encoded form the host's IMAP server reads.</summary>
    private readonly IPasswordHasher _hasher;

    /// <summary>The agent, asked which scheme the host verifies.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Creates the service.</summary>
    /// <param name="hasher">The panel's password hasher.</param>
    /// <param name="agent">The agent client that reports the host's scheme.</param>
    public MailPasswordHashing(IPasswordHasher hasher, IAgentMailClient agent)
    {
        _hasher = hasher;
        _agent = agent;
    }

    /// <summary>Hashes a password in the scheme the host verifies.</summary>
    /// <param name="password">The password the owner chose.</param>
    /// <param name="cancellationToken">Cancels the call to the agent.</param>
    /// <returns>
    /// The hash, or the agent's typed failure, or <c>MailPasswordSchemeUnsupported</c> when the host verifies
    /// no scheme the panel produces, or has not verified the one it names.
    /// </returns>
    public async Task<Result<string>> HashAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);

        var status = await _agent.GetStatusAsync(cancellationToken);
        if (!status.IsSuccess)
        {
            return Result<string>.Fail(status.Error!);
        }

        if (status.Value!.PasswordScheme != AgentMailPasswordScheme.Argon2id
            || status.Value.PasswordSchemeVerified != AgentMailObservation.Yes)
        {
            return Result<string>.Fail(
                Error.Of(nameof(ErrorMessages.MailPasswordSchemeUnsupported), ErrorType.Conflict));
        }

        return Result<string>.Ok(_hasher.Hash(password));
    }
}
