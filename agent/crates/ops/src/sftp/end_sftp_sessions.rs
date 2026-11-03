//! EndSftpSessions: cut the sessions one SFTP login has open right now
//! (issue #339).

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::pkill_answer::PkillAnswer;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::DistroAdapter;

use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::model::login_session_matcher::LoginSessionMatcher;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The signal.
///
/// `KILL` and not `TERM`, for the reason the account suspension's session cull
/// states (`ops::logins`): a session being ended by a revocation is a transfer
/// being cut either way, and a session that survives the revocation is the
/// whole defect this operation exists to close. A graceful signal would add a
/// wait inside a root daemon's call and the chance that the process ignores it.
const KILL_SIGNAL: &str = "KILL";

/// Signals every process of `user`'s open sessions, and answers with how many
/// processes there were.
///
/// This is what makes replacing or revoking a login's keys end access rather
/// than only refuse the next login. sshd consults the key file when a
/// connection authenticates and never again for a session it has admitted, so a
/// session opened with a key the customer has since revoked keeps reading and
/// writing the account's home until the customer chooses to leave.
///
/// # Which processes
///
/// Those whose sshd title names this login (`LoginSessionMatcher`). Not a
/// uid: the login shares its account's uid, so a uid-wide signal would end the
/// account's other logins and the agent's own account-side children.
///
/// # The login has to BELONG to the account
///
/// For the reason `delete_sftp_user` states at length — `<account>_<name>` has
/// no unique decomposition, so the account the panel authorised is what settles
/// whose login this is — the name is looked up in the account's own login
/// enumeration (passwd home exactly this account's jail) and a login the
/// account does not hold is [`SftpError::NotFound`]. A neighbouring tenant's
/// session is therefore out of reach of a request authorised for another
/// account, and the answer does not confirm that the neighbour exists.
///
/// # No lock
///
/// Unlike every other operation in this area, this one does NOT take the
/// hosting account's lock, and that is deliberate. The lock never waits, so a
/// held lock refuses: a backup or a restore of the account holds it for minutes,
/// and a revocation that was refused for that long would leave the revoked key's
/// sessions open for as long — the opposite of what the operation is for. The
/// lock protects against `fork_as_account` children being killed by a uid-wide
/// signal; this signal is not uid-wide. The ownership check is a read, and a
/// login deleted between it and the signal leaves nothing for the signal to
/// match. Idempotent for the same reason: a second call finds no session.
///
/// # What it costs the customer
///
/// An upload in flight is cut at whatever byte it had reached, and the partial
/// file stays; the agent cannot tell it from a file the customer meant to write.
///
/// # Errors
///
/// - [`SftpError::NotFound`] when the account holds no login of that name.
/// - [`SftpError::AccountMissing`] when the password database cannot be read.
/// - [`SftpError::SpawnFailed`] when `pkill` cannot be started.
/// - [`SftpError::Bound`] when `pkill` outran its deadline; how many processes
///   were signalled is then unknown.
/// - [`SftpError::SessionsNotEnded`] when `pkill` answered anything but
///   "signalled" or "nothing matched".
pub fn end_sftp_sessions(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    user: &SftpUserName,
) -> Result<u32, SftpError> {
    let jail = AccountJail::for_account(account, distro.systemd_unit_directory());
    let logins = host.account_logins(distro.passwd_database(), account, jail.directory())?;
    if !logins.iter().any(|held| held.as_str() == user.as_str()) {
        return Err(SftpError::NotFound);
    }

    let arguments = LoginSessionMatcher::for_login(user).arguments(KILL_SIGNAL);
    let arguments: Vec<&str> = arguments.iter().map(String::as_str).collect();
    let outcome = host.run(
        &distro.pkill_binary(),
        &arguments,
        None,
        CallClass::Mutation,
    )?;

    match PkillAnswer::from_status(outcome.status) {
        PkillAnswer::Matched => Ok(signalled_count(&outcome.stdout)),
        PkillAnswer::NothingMatched => Ok(0),
        PkillAnswer::Refused(code) => Err(SftpError::SessionsNotEnded { code }),
    }
}

/// How many processes `pkill --count` said it matched.
///
/// An unreadable number is zero rather than an error: the processes HAVE been
/// signalled by the time this is parsed, so failing here would fail a
/// revocation whose signal was sent. The figure is for the operator's record;
/// nothing branches on it.
fn signalled_count(stdout: &str) -> u32 {
    stdout
        .lines()
        .next()
        .unwrap_or_default()
        .trim()
        .parse()
        .unwrap_or(0)
}

#[cfg(test)]
#[path = "../tests/sftp/end_sftp_sessions_tests.rs"]
mod tests;
