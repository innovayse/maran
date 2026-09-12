//! SetSftpPassword: one `user:password` line, over standard input — and the
//! lock that line would otherwise take off.

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::DistroAdapter;

use crate::accounts::{StoredPassword, take_account_lock};
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The separator `chpasswd` expects between the login and its password.
const FIELD_SEPARATOR: char = ':';

/// The `getent` database that holds password fields.
const SHADOW_DATABASE: &str = "shadow";

/// The separator between the fields of a shadow entry.
const SHADOW_FIELD_SEPARATOR: char = ':';

/// The position of the password field in a shadow entry, counting the name as
/// field 0.
///
/// Named rather than written as a literal, because a bare index next to `split`
/// is the kind of thing a later edit moves by one without anything noticing: the
/// entry would still parse, and the agent would classify a DATE as a password.
const SHADOW_PASSWORD_FIELD: usize = 1;

/// `getent`'s exit status for a key it does not hold.
///
/// Documented in `getent(1)` and measured as 2 on both polygon families. Told
/// apart from every other refusal because it means the LOGIN is not on this
/// host, which is a different thing from a name service that would not answer.
const KEY_NOT_FOUND: i32 = 2;

/// The `usermod` flag that prefixes a password hash so nothing can match it.
const LOCK: &str = "--lock";

/// Sets `user`'s password to `password`, without giving away a suspension.
///
/// # Why standard input, and why that is enough
///
/// `chpasswd` reads `user:password` lines from standard input, and this
/// operation gives it exactly one. The argument vector carries nothing but the
/// program itself, deliberately: a command line is readable through `/proc` by
/// every local user on the host — including every other tenant's SFTP login and
/// every php-fpm pool — so a password passed as an argument is a password that
/// has already leaked to the people it is meant to be kept from. A pipe is
/// readable only by the two processes at its ends.
///
/// One line reaches the tool because a [`Password`] **cannot hold** a newline or
/// a colon. That is not a detail of this function; it is what makes the design
/// safe. A newline would end the line early and start a second one, and a second
/// `user:password` line is a password set for a login the caller does not own —
/// `root:` included. A colon would move the boundary between the two fields of
/// the first line. Neither character has a `Password` value, so neither can
/// arrive here to be escaped or missed: the value is validated, not escaped
/// (rules/security.md §4).
///
/// Read that before widening the password alphabet. Accepting more characters
/// and quoting them here would replace a guarantee the type system enforces with
/// an escaping routine nobody has reviewed.
///
/// # The suspension this used to hand back
///
/// `chpasswd` does not edit the shadow password field, it **replaces** it — and
/// a suspension is a `!` written in front of that same field by
/// `logins::set_account_logins_locked`. So a
/// suspended customer who changed their own SFTP password through the panel got
/// a fresh unmarked hash, an authenticating login, and a panel that went on
/// reporting the account as suspended. No race and no second actor were
/// involved: the two operations simply had no shared invariant.
///
/// This operation therefore reads the login's raw shadow field before it writes,
/// classifies it with [`StoredPassword`] — the same four states the account
/// reactivation path decides from — sets the password, and **re-asserts what it
/// observed**: a login that could not authenticate before cannot authenticate
/// after. The password the customer chose is the password the login will have
/// when the suspension is lifted, and lifting it stays the panel's decision.
///
/// **Restore rather than refuse, and the difference is a promise.** Refusing
/// while the field is locked has no window at all, and it was rejected: the
/// agent cannot tell a suspension from an operator's own `usermod --lock` or
/// from a hand-made passwordless login, because all three are one locked field —
/// "suspended" is the panel's concept and lives in the panel's record. Refusing
/// would also strand the realistic case, which is an operator rotating a leaked
/// credential *before* reactivating; forcing reactivate-then-rotate leaves the
/// leaked credential live for that window.
///
/// **What a crash between the two steps leaves behind**, stated rather than
/// hidden: `chpasswd` has committed, the re-lock has not run, and the login
/// authenticates with the new password — today's defect, reached only by an
/// agent killed inside one process spawn, and only for a login that was locked
/// to begin with. Nothing rolls it back, and nothing can: the previous hash is
/// gone and this agent never held it. What makes it different from the defect is
/// that it is **observable** —
/// `logins::account_logins` reads the host
/// rather than the panel's record, so the account shows as not fully suspended
/// and re-issuing the (idempotent) suspension closes it. The alternative,
/// writing the shadow field ourselves in one edit, means hand-rolled hashing and
/// a hand-written shadow edit in a root process, and is deliberately not done.
///
/// The [`StoredPassword::Empty`] state is the one thing NOT restored, and that
/// is not an oversight: an empty field is a login that authenticates with the
/// empty password, so restoring it would be restoring an open door. It counts as
/// "could authenticate", the password is set, and nothing is locked.
///
/// # The lock
///
/// The hosting account's lock (`crate::accounts::account_lock`) is taken at this
/// entry point and held until it returns, because everything above is a
/// read-modify-write of one shadow field: without it, a suspension landing
/// between the read and `chpasswd` is unlocked by this operation on the strength
/// of a fact that has expired. It is the SAME lock `create_sftp_user`, the
/// account's deletion and the four backup operations take — no new lock is
/// introduced (rules/rust.md, "What this agent serialises") — and it never
/// waits, so it adds no edge to the wait-for graph. It is process-local, and
/// serialises this binary against itself and nothing else: not an operator's own
/// `chpasswd`, not a second agent binary.
///
/// The account is a parameter rather than something decoded from `user`. A login
/// name is `<account>_<name>` and account names may contain the separator, so
/// the account `alice_bob` and the login `bob` of account `alice` are the same
/// eleven characters and no decode can tell them apart. The panel's own
/// authorisation is what the caller passes here, and it stays the source of
/// truth.
///
/// # The login has to BELONG to the account
///
/// Checked here, under the lock, through the same enumeration
/// `remove_account_sftp` uses — a candidate is
/// this account's login only when its passwd home is exactly this account's
/// jail, which is a value the agent itself wrote rather than a guess about a
/// name.
///
/// Without it the collision above is not merely ambiguous, it is writable: a
/// request authorised for account `alice`, naming login `b`, addresses the
/// system user `alice_b` — which may be the SFTP login `b` of the NEIGHBOURING
/// account `alice`, or of account `alice_` … the decomposition is not unique
/// when account names may carry the separator. `create_sftp_user` cannot make
/// that mistake, because `useradd` refuses a name already in use; setting a
/// password had no such gate, and would have written a credential onto another
/// tenant's login. A login the account does not hold is
/// [`SftpError::NotFound`].
///
/// # Errors
///
/// - [`SftpError::AccountBusy`] when another operation for the hosting account
///   is already running. Nothing was read and nothing was written.
/// - [`SftpError::PasswordRejected`] when `chpasswd` refuses the line — the
///   login exists and its password is unchanged.
/// - [`SftpError::NotFound`] when the host holds no login of that name, and
///   [`SftpError::StatusUnreadable`] when `getent` printed something this agent
///   cannot read as the shadow entry of the login it asked about. Both are
///   raised before `chpasswd` runs, so the password is unchanged.
/// - [`SftpError::SuspensionNotRestored`] when the password was set but the
///   login can authenticate although it could not before. The password IS set in
///   that case, and the login is open: it is the one condition here an operator
///   has to act on.
/// - [`SftpError::SpawnFailed`] when `getent` or `usermod` refuses, or when a
///   tool could not be run at all, or its standard input could not be written.
pub fn set_sftp_password(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    user: &SftpUserName,
    password: &Password,
) -> Result<(), SftpError> {
    // Owned guard, so the lock is released by every path out of the call below,
    // including every error path and a panic.
    let _guard = take_account_lock(account).ok_or(SftpError::AccountBusy)?;

    let jail = AccountJail::for_account(account, distro.systemd_unit_directory());
    let logins = host.account_logins(distro.passwd_database(), account, jail.directory())?;
    if !logins.iter().any(|held| held.as_str() == user.as_str()) {
        return Err(SftpError::NotFound);
    }

    set_sftp_password_under_lock(host, distro, user, password)
}

/// The password change itself, with the hosting account's lock ALREADY held.
///
/// Split from [`set_sftp_password`] for two reasons, and the second is the one
/// that matters. `create_sftp_user` holds the account's lock and sets the new
/// login's password inside it: an entry point that took the lock again would be
/// refused by it, since the lock never waits — so the lock stays taken exactly
/// once, at an operation's entry, which is the property the agent's no-cycle
/// argument rests on. And the lock is a process-wide static, so a suite driving
/// the public entry for one account name on the harness's own threads would
/// refuse itself, a flake that says nothing about the code.
///
/// # Errors
///
/// Every variant [`set_sftp_password`] documents except
/// [`SftpError::AccountBusy`], which is the entry point's own answer.
pub(crate) fn set_sftp_password_under_lock(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    user: &SftpUserName,
    password: &Password,
) -> Result<(), SftpError> {
    // Read BEFORE the write, because the write destroys the answer: `chpasswd`
    // replaces the whole field, marker and all.
    let before = stored_password(host, distro, user)?;

    write_password(host, distro, user, password)?;

    if before.can_authenticate() {
        return Ok(());
    }

    restore_the_lock(host, distro, user)
}

/// Hands `chpasswd` its one line and reads its answer, and does nothing else.
///
/// The whole of what this operation used to be, kept as its own unit because
/// **creation must not restore anything**. A login `useradd` has just made
/// carries `!` or `!!` — [`StoredPassword::Absent`] — on both families, so a
/// creation that re-asserted the state it found would lock the credential it was
/// created to hand out, for every account, suspended or not. The pre-state of a
/// login that did not exist a moment ago carries no information, and this is the
/// entry `create_sftp_user` uses for that reason.
///
/// # Errors
///
/// - [`SftpError::PasswordRejected`] when `chpasswd` refuses the line.
/// - [`SftpError::SpawnFailed`] when it could not be run at all, or its standard
///   input could not be written.
pub(crate) fn write_password(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    user: &SftpUserName,
    password: &Password,
) -> Result<(), SftpError> {
    let line = format!("{}{FIELD_SEPARATOR}{}\n", user.as_str(), password.as_str());

    let outcome = host.run(distro.chpasswd_binary(), &[], Some(&line))?;
    if outcome.status != 0 {
        return Err(SftpError::PasswordRejected);
    }

    Ok(())
}

/// Puts the lock marker back over the freshly written hash, and confirms it.
///
/// `usermod --lock` exits zero on a login it did nothing to — that is the
/// measured behaviour on a passwordless entry, and it is why an exit status is
/// not evidence here. The field is read again and classified, so what this
/// function reports is what the host holds rather than what the tool said about
/// it.
///
/// # Errors
///
/// - [`SftpError::SpawnFailed`] when `usermod` refuses or could not be run.
/// - [`SftpError::StatusUnreadable`] when the entry cannot be read back.
/// - [`SftpError::SuspensionNotRestored`] when the login can authenticate
///   anyway.
fn restore_the_lock(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    user: &SftpUserName,
) -> Result<(), SftpError> {
    let outcome = host.run(distro.usermod_binary(), &[LOCK, user.as_str()], None)?;
    if outcome.status != 0 {
        return Err(SftpError::SpawnFailed {
            code: outcome.status,
        });
    }

    if stored_password(host, distro, user)?.can_authenticate() {
        return Err(SftpError::SuspensionNotRestored);
    }

    Ok(())
}

/// What `user`'s shadow password field holds, as one of four states.
///
/// `getent shadow <login>`, and never a read of `/etc/shadow` itself: the
/// question is about ONE name, `getent` answers through the host's configured
/// name service rather than only the local file, and asking for one key returns
/// one line instead of pulling every hash on the host into a root process
/// (rules/security.md item 8). The field is classified here and the bytes are
/// dropped — [`StoredPassword`] holds a hash in no variant, and neither does any
/// error raised on this path.
///
/// The NAME on the line is matched before the field beside it is believed, for
/// the reason `logins::account_logins` matches it: an edit that dropped the
/// argument would otherwise classify some other login's entry as this one's.
///
/// A login the host does not hold is [`SftpError::NotFound`] and never a state:
/// "there is no such login" and "its field carries no hash" are different facts,
/// and reporting the first as the second would have this operation lock a login
/// that does not exist. It is told from every other refusal by `getent`'s own
/// exit status, so a name service that will not answer is a failure rather than
/// an answer — which matters here, because an unreadable field read as `Absent`
/// would make an ordinary password change LOCK a working login.
///
/// # Errors
///
/// - [`SftpError::NotFound`] when the host holds no such login.
/// - [`SftpError::SpawnFailed`] when `getent` refuses for any other reason or
///   could not be run at all.
/// - [`SftpError::StatusUnreadable`] when the entry it printed is not
///   `<login>:<password>:…` for the login that was asked about.
fn stored_password(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    user: &SftpUserName,
) -> Result<StoredPassword, SftpError> {
    let outcome = host.run(
        distro.getent_binary(),
        &[SHADOW_DATABASE, user.as_str()],
        None,
    )?;
    if outcome.status == KEY_NOT_FOUND {
        return Err(SftpError::NotFound);
    }
    if outcome.status != 0 {
        return Err(SftpError::SpawnFailed {
            code: outcome.status,
        });
    }

    let mut fields = outcome
        .stdout
        .lines()
        .next()
        .unwrap_or_default()
        .split(SHADOW_FIELD_SEPARATOR);

    match (fields.next(), fields.nth(SHADOW_PASSWORD_FIELD - 1)) {
        (Some(name), Some(field)) if name == user.as_str() => Ok(StoredPassword::classify(field)),
        _ => Err(SftpError::StatusUnreadable),
    }
}

#[cfg(test)]
#[path = "../tests/sftp/set_sftp_password_tests.rs"]
mod tests;
