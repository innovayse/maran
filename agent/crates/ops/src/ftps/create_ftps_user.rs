//! CreateFtpsUser: the account's jail, then the login that lands in it.

use maran_distro::DistroAdapter;

use crate::accounts::take_account_lock;
use crate::ftps::ensure_account_jail::ensure_account_jail;
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::ftps_jail::FtpsJail;
use crate::ftps::model::ftps_user_request::FtpsUserRequest;
use crate::ftps::set_ftps_password::write_password;

/// Tells `useradd` not to create or touch the home directory it is given.
///
/// Load-bearing, not tidiness. The passwd home of an FTPS login is its jail, and
/// `useradd`'s default behaviour for a missing home is to create it AND chown it
/// to the new user — which would hand the chroot itself to the customer. vsftpd
/// would then refuse every login into it (`refusing to run with writable root
/// inside chroot()`), and a customer who could write the chroot's own directory
/// is the starting point of every chroot escape there is. The jail is made by
/// this operation, as root, and `useradd` must leave it alone.
const NO_CREATE_HOME: &str = "--no-create-home";

/// Sets the login's passwd home directory.
const HOME_DIRECTORY: &str = "--home-dir";

/// Sets the login's shell.
const SHELL: &str = "--shell";

/// Adds the login to supplementary groups.
const GROUPS: &str = "--groups";

/// Sets the login's numeric user id.
const UID: &str = "--uid";

/// Sets the login's primary group by id.
const GID: &str = "--gid";

/// Permits a user id another login already holds.
///
/// Required, and the whole point rather than a workaround: the account itself
/// already holds this uid, and `useradd` refuses a duplicate without being told
/// the duplication is deliberate. See
/// [`AccountOwnership`](crate::sftp::AccountOwnership) for why the login is
/// given the account's identity instead of one of its own.
const NON_UNIQUE: &str = "--non-unique";

/// Creates `request`'s FTPS login, chrooted into its account's jail.
///
/// # What is built, and why in this order
///
/// 1. The account's jail — `/var/lib/maran-ftps/<account>`, root-owned `0755`,
///    with the account's real home bind-mounted at `home` inside it by an
///    enabled systemd unit. Ensured on every call and idempotent, because an
///    account's second FTPS login must not fail on the first one's work.
/// 2. The system login, with a `nologin` shell, the ACCOUNT's user and group
///    ids, membership of the FTPS group and of nothing else, and its passwd
///    home set to the jail — which is what `chroot_local_user` chroots to.
/// 3. The password, over `chpasswd`'s standard input.
///
/// **The jail comes first, and the ordering was decided rather than inherited.**
/// It is the same ruling `sftp::create_sftp_user` carries, and it holds here for
/// the same reason plus one that is sharper for FTPS. Reversing the two inverts
/// the direction of the failure: if the login is created first and the jail then
/// fails, what is left on the host is a LIVE CREDENTIAL — a passwd entry with a
/// password, in the group the PAM stack authorises, whose home either does not
/// exist or is an empty directory. If the jail is created first and the login
/// then fails, what is left is a root-owned empty directory and a mount unit,
/// which authenticate nobody and which the account's deletion takes away. One of
/// those two states is a security failure and the other is litter, so the order
/// that can only produce the litter is the correct one.
///
/// The FTPS-specific sharpening: vsftpd `chdir()`s into the home AFTER dropping
/// to the account's uid, so a login whose jail is missing does not fail
/// closed — it fails wherever the daemon's `chroot_local_user` handling takes
/// it, and on a misconfigured host that is a shell of a directory the customer
/// did not expect to see. Building the chroot before the credential exists means
/// the daemon is never asked that question.
///
/// The window that ordering leaves open — the jail exists, the account is
/// deleted, the login is then created — is closed by the lock plus the identity
/// re-read below, exactly as it is for SFTP.
///
/// **The account's home is never touched.** It stays
/// `<account>:<web server group> 0750`, exactly as account creation and every
/// nginx and php-fpm path expect — the jail is a separate root-owned directory,
/// and the home only appears inside it through the bind mount.
///
/// **There is no caller-supplied chroot path.** The jail is derived from the
/// account, so the chroot-escape class of bug has nothing to aim at: no request
/// can name the directory it will be confined to.
///
/// **The group is the entire authorization.** The login joins `maran-ftps` and
/// nothing else — never `maran-sftp` — because the PAM stack this panel
/// installs authorises FTPS by that one group membership. A login in both
/// groups is a credential that opens both daemons; a `nologin` shell and the
/// absence of the SFTP group are what keep this one to file transfer over FTPS.
///
/// # The lock, and the fact that is re-read inside it
///
/// The hosting account's lock (`crate::accounts::account_lock`) is taken at this
/// entry point and held until it returns. It is the SAME lock the account's
/// deletion, `sftp::create_sftp_user`, `php::write_pool` and the four backup
/// operations take — **no new lock is introduced** (rules/rust.md, "What this
/// agent serialises") — and it never waits, so it adds no edge to the wait-for
/// graph. It is process-local: it serialises this binary against itself and
/// nothing else, not a second agent and not an operator's own `userdel`.
///
/// The account's identity is then read, the jail is built, and the identity is
/// read AGAIN immediately before `useradd`, with a mismatch refusing. A uid
/// resolved before the jail work belongs to whoever `useradd` has since been
/// given it, and `useradd --non-unique --uid` does not care that the number is
/// now somebody else's — it is the one tool in this path that cannot be trusted
/// to notice. The lock excludes this agent's own deletion; the re-read is what
/// catches what the lock cannot see.
///
/// # Idempotency
///
/// A login that already exists is reported as [`FtpsError::AlreadyExists`] and
/// **its password is not changed** — the operation returns before `chpasswd` is
/// reached. That is what makes retrying a creation whose response was lost safe:
/// the caller cannot tell a lost request from a lost reply, and a second attempt
/// must not reset the credential the customer was already shown.
///
/// The decision is `useradd`'s own exit status rather than a lookup first: a
/// check followed by a create is two operations with a gap between them, and the
/// tool answers the same question atomically.
///
/// # Errors
///
/// - [`FtpsError::AccountBusy`] when another operation for the hosting account
///   is already running. Nothing was created.
/// - [`FtpsError::AccountMissing`] when the hosting account is not on this host.
///   Checked first, before anything is created.
/// - [`FtpsError::JailFailed`] when the jail's directories cannot be created, or
///   the mount unit cannot be written, validated or started. No login is created
///   in that case.
/// - [`FtpsError::AccountIdentityChanged`] when the account's uid or gid moved,
///   or the account went away, between the first read and `useradd`. No login is
///   created. The jail is left in place: it is a root-owned directory and a
///   mount unit with no credential in it, and `remove_account_ftps` takes both
///   away the next time an account of this name is deleted.
/// - [`FtpsError::AlreadyExists`] when the login is already on this host.
/// - [`FtpsError::PasswordRejected`] when `chpasswd` refuses the password.
/// - [`FtpsError::SpawnFailed`] when `useradd` refuses for any other reason, or
///   could not be run at all.
pub fn create_ftps_user(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    request: &FtpsUserRequest,
) -> Result<(), FtpsError> {
    // Taken before the account's identity is read, and held until this function
    // returns: everything between the read and `useradd` is the window the
    // concurrency audit measured on the SFTP path, and the account's deletion is
    // what runs inside it. Owned guard, so the lock is released by every path
    // out of the call below.
    let _guard = take_account_lock(&request.account).ok_or(FtpsError::AccountBusy)?;

    create_ftps_user_under_lock(host, distro, request)
}

/// The creation itself, with the hosting account's lock ALREADY held.
///
/// Split from [`create_ftps_user`] for the reason `create_sftp_user`'s body is
/// split from its entry point: the lock is a process-wide static, and a dozen
/// unit tests driving the public entry for one account name on the harness's own
/// threads would refuse each other — a flaky suite whose flake says nothing
/// about the code. The exclusion is tested for what it is, once; everything else
/// about a creation is exercised here.
///
/// # Errors
///
/// Every variant [`create_ftps_user`] documents except
/// [`FtpsError::AccountBusy`], which is the entry point's own answer.
pub(crate) fn create_ftps_user_under_lock(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    request: &FtpsUserRequest,
) -> Result<(), FtpsError> {
    let ownership = host.account_ownership(&request.account)?;

    let jail = FtpsJail::for_account(&request.account, distro.systemd_unit_directory());
    ensure_account_jail(host, distro, &jail)?;

    // Asked AGAIN, immediately before the login is created, and compared with
    // what was read before the jail work. See the entry point's doc: the lock
    // excludes this agent's own deletion, and this catches what it cannot —
    // a `userdel` an operator ran by hand, or a second agent binary.
    if host.account_ownership(&request.account)? != ownership {
        return Err(FtpsError::AccountIdentityChanged);
    }

    // Formatted into owned strings that outlive the argv slice below. They are
    // numbers this process read out of the password database, never anything a
    // request carried, so there is nothing here for a caller to influence.
    let (uid, gid) = (ownership.uid.to_string(), ownership.gid.to_string());

    let outcome = host.run(
        distro.useradd_binary(),
        &[
            HOME_DIRECTORY,
            jail.directory(),
            NO_CREATE_HOME,
            SHELL,
            distro.nologin_shell(),
            GROUPS,
            distro.ftps_group(),
            NON_UNIQUE,
            UID,
            &uid,
            GID,
            &gid,
            request.user.as_str(),
        ],
    )?;

    if outcome.status != 0 {
        return Err(FtpsError::from_useradd(outcome.status));
    }

    // `write_password` and not `set_ftps_password_under_lock`: a login `useradd`
    // made a moment ago carries `!` (Debian) or `!!` (RHEL), so a creation that
    // re-asserted the state it found would lock the credential it was created to
    // hand out, for every account, suspended or not.
    write_password(host, distro, &request.user, &request.password)
}

#[cfg(test)]
#[path = "../tests/ftps/create_ftps_user_tests.rs"]
mod tests;
