//! Asking the password database which credentials into an account's home exist.

use maran_agent_core::utils::system_account::SystemAccount;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::DistroAdapter;

use crate::ftps::FtpsJail;
use crate::logins::account_passwd_row::account_passwd_row;
use crate::logins::logins_error::LoginsError;
use crate::logins::logins_host::LoginsHost;
use crate::logins::model::account_login::AccountLogin;
use crate::logins::model::account_login_set::AccountLoginSet;
use crate::logins::model::login_protocol::LoginProtocol;
use crate::sftp::AccountJail;

/// The argument that makes `passwd` report a login's state instead of changing
/// it.
///
/// Named so that the one place this area runs `passwd` cannot be read as the
/// place it sets a password: `-S` is the whole difference.
const PASSWORD_STATUS_ARGUMENT: &str = "-S";

/// What `passwd -S` prints in its second field for a locked password — the
/// FIRST LETTER of it, because the two families do not spell the rest the same.
///
/// Measured on both polygon images rather than assumed, because assuming cost
/// the RHEL family a working suspension:
///
/// ```text
/// ubuntu24:  u1 P  …   u1 L  …        (Debian shadow-utils)
/// alma9:     u1 PS …   u1 LK …        (RHEL's passwd)
/// ```
///
/// An exact comparison against `"L"` is therefore true on Debian and FALSE on
/// every RHEL host, which made the login half of the suspension attestation
/// report every account as unlocked there and refuse every suspension. Matching
/// the first letter is unambiguous on both: the other states are `P`/`PS` for a
/// usable password and `NP` for none at all, and neither begins with `L`.
const LOCKED_PASSWORD_PREFIX: char = 'L';

/// Reports every file-transfer login `account` holds, whichever daemon serves
/// it, and whether each is locked.
///
/// Read-only, and asked of the HOST's password database rather than of a list
/// the panel supplies: a list can only describe what the panel remembers
/// creating, and a login it has forgotten is exactly the one still letting a
/// suspended customer in.
///
/// # How a row is classified
///
/// By the passwd home, which is a value this agent itself wrote, and never by
/// the name, which cannot answer it. Account names may contain the separator,
/// so the ACCOUNT `alice_bob` and the login `bob` of account `alice` are the
/// same eleven characters; a name-prefix scan would take a neighbouring
/// account's system user for this account's login. So a row is
///
/// - a [`LoginProtocol::Sftp`] login when its home is this account's SFTP jail,
/// - a [`LoginProtocol::Ftps`] login when its home is this account's FTPS jail,
///
/// and in both cases only when the name is one this agent could itself have
/// created — each is rebuilt through the constructor that would have built it,
/// which is what stops `alice_bob_deploy` being reported as `alice`'s — and
/// only when the row carries the ACCOUNT'S OWN uid. A row homed in the jail on
/// a foreign uid is not a login of this account: it cannot read a home owned
/// `<account>:<web server group>` at `0750`, and locking it would turn off a
/// stranger's credential on this account's suspension.
///
/// **Both jails, and that is the whole reason this function is here.** Its
/// predecessor selected on the SFTP jail alone, so an FTPS login of the same
/// account was invisible to it — and therefore to the suspension that calls it.
/// On the day the first FTPS login existed, a suspended customer would have kept
/// a working write credential into their own home while the attestation reported
/// every login locked.
///
/// # What it does not cover, and why that is a number
///
/// A passwd entry sharing the account's uid whose home is neither jail is NOT a
/// login of this account by the rule above, and this agent will not lock it: it
/// did not create it, and turning off a credential somebody deliberately
/// arranged is not its to do. Those entries are COUNTED —
/// [`AccountLoginSet::unmanaged`] — rather than silently left out, because an
/// attestation that said nothing about them would be claiming a completeness it
/// did not have. It is the same answer the cron half gives for the lines it did
/// not write.
///
/// A row that shares NEITHER — a jailed row on a foreign uid — is excluded from
/// both, and deliberately. It is not one of the account's logins, and it is not
/// what [`AccountLoginSet::unmanaged`] counts either: that number means "shares
/// this account's uid without the panel having created it", which is the
/// sentence an operator reads on a suspension, and a row that writes as
/// somebody else would make that sentence false. Giving it a number of its own
/// would be a contract change and is not made here.
///
/// # What an empty answer means
///
/// That the account holds no file-transfer login, which is a suspended state
/// and not a failure to look. The blind answer this could otherwise degenerate
/// into cannot happen: a password database that cannot be read returns the error
/// below rather than an empty list, so does one holding no row for the account,
/// and so does a `passwd` that refuses or prints something unreadable.
///
/// # Errors
///
/// - [`LoginsError::AccountMissing`] when the password database cannot be read,
///   or holds no row for the account itself.
/// - [`LoginsError::SpawnFailed`] when `passwd -S` refuses or could not be run.
/// - [`LoginsError::StatusUnreadable`] when it printed something this agent
///   cannot read as the state of the login it asked about.
pub fn account_logins(
    host: &dyn LoginsHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
) -> Result<AccountLoginSet, LoginsError> {
    let rows = host.read_passwd(distro.passwd_database())?;
    let account_uid = account_passwd_row(&rows, account)?.uid;
    let sftp_jail = AccountJail::for_account(account, distro.systemd_unit_directory());
    let ftps_jail = FtpsJail::for_account(account, distro.systemd_unit_directory());

    let mut named: Vec<(String, LoginProtocol)> = Vec::new();
    let mut unmanaged: u32 = 0;
    for row in &rows {
        // The account's own entry is not one of its logins, on two independent
        // counts: its home is under `/home` and it carries no login suffix. It
        // is skipped explicitly all the same, so that the unmanaged count can
        // never grow to include the very account it is counting against.
        if row.name == account.as_str() {
            continue;
        }

        match classified(
            account,
            account_uid,
            row,
            sftp_jail.directory(),
            ftps_jail.directory(),
        ) {
            Some(login) => named.push(login),
            // Not ours, but on our uid: it writes as this account and this
            // agent will not touch it. Counted, never acted on.
            None if row.uid == account_uid => unmanaged = unmanaged.saturating_add(1),
            None => {}
        }
    }

    // Sorted so that two calls against an unchanged host answer in the same
    // order, whatever order the file happened to hold the rows in, and so that
    // a duplicated row cannot produce a duplicated login.
    named.sort_by(|left, right| left.0.cmp(&right.0));
    named.dedup();

    let mut logins = Vec::with_capacity(named.len());
    for (name, protocol) in named {
        let locked = login_locked(host, distro, &name)?;
        logins.push(AccountLogin {
            name,
            protocol,
            locked,
        });
    }

    Ok(AccountLoginSet { logins, unmanaged })
}

/// Which of `account`'s logins `row` is, or `None` when it is not one.
///
/// Three conditions, all required: the row writes as this account, its home is
/// one of this account's jails, and its name rebuilds through that protocol's
/// own constructor — so nothing outside this agent's naming convention is ever
/// reported and one account's name can never alias another's.
///
/// # Why the uid is asked first, and here rather than at the call site
///
/// A passwd row homed in this account's jail on SOMEBODY ELSE's uid is not a
/// credential into this account: the jail presents a home owned
/// `<account>:<web server group>` at `0750`, so a foreign uid outside that
/// group reads nothing there. Reporting it would put a name the panel has no
/// business learning into the attestation, and — because the only other
/// consumer is the suspension — would turn a stranger's login off with
/// `usermod --lock` on this account's suspension and back on at its
/// reactivation.
///
/// The check is the first statement of this function and not a fourth arm of
/// the caller's `match` because this function is the only place the answer is
/// decided, and a condition placed here is inherited by a third protocol added
/// below rather than having to be remembered a third time. It is the same
/// reason the home comparison lives here: both are what MAKES a row one of
/// this account's logins, not something compared about one afterwards.
///
/// A row that fails only the uid test falls out of the caller's unmanaged
/// count as well, which is correct — that count means "shares this account's
/// uid", and it is the sentence an operator reads on suspension.
fn classified(
    account: &AccountName,
    account_uid: u32,
    row: &SystemAccount,
    sftp_directory: &str,
    ftps_directory: &str,
) -> Option<(String, LoginProtocol)> {
    if row.uid != account_uid {
        return None;
    }

    if row.home == sftp_directory {
        return SftpUserName::decode(account, &row.name)
            .map(|user| (user.as_str().to_owned(), LoginProtocol::Sftp));
    }

    if row.home == ftps_directory {
        return FtpsUserName::decode(account, &row.name)
            .map(|user| (user.as_str().to_owned(), LoginProtocol::Ftps));
    }

    None
}

/// Whether `username`'s password is locked, as `passwd -S` reports it.
///
/// The name on the line is compared with the one that was asked about before
/// the state beside it is believed. `passwd -S` with no argument prints the
/// CALLER's own account, so a line about somebody else is an answer to a
/// different question, and reading its second letter would report root's state
/// as a customer's.
fn login_locked(
    host: &dyn LoginsHost,
    distro: &dyn DistroAdapter,
    username: &str,
) -> Result<bool, LoginsError> {
    let outcome = host.run(
        distro.passwd_binary(),
        &[PASSWORD_STATUS_ARGUMENT, username],
    )?;
    if outcome.status != 0 {
        return Err(LoginsError::SpawnFailed {
            code: outcome.status,
        });
    }

    let mut fields = outcome
        .stdout
        .lines()
        .next()
        .unwrap_or_default()
        .split_whitespace();

    match (fields.next(), fields.next()) {
        (Some(name), Some(state)) if name == username => {
            Ok(state.starts_with(LOCKED_PASSWORD_PREFIX))
        }
        _ => Err(LoginsError::StatusUnreadable),
    }
}

#[cfg(test)]
#[path = "../tests/logins/account_logins_tests.rs"]
mod tests;
