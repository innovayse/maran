//! CreateSftpUser: the account's jail, then the login that lands in it.

use std::path::Path;

use maran_distro::DistroAdapter;
use maran_templates::systemd::unit::MountUnit;

use crate::accounts::take_account_lock;
use crate::safe_write::model::{Reload, Validator};
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::model::sftp_user_request::SftpUserRequest;
use crate::sftp::set_sftp_password::write_password;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The mode the jail and its mount point are created with.
///
/// `0755`, root-owned: OpenSSH refuses to chroot into a directory that is
/// group- or world-writable, and it refuses one that is not owned by root. The
/// login still has to be able to list the jail to find `home` in it, which is
/// what the read and execute bits are for. This is the only mode that satisfies
/// both, so it is a constant rather than a parameter.
const JAIL_MODE: u32 = 0o755;

/// Tells `useradd` not to create or touch the home directory it is given.
///
/// Load-bearing, not tidiness. The passwd home of an SFTP user is its jail, and
/// `useradd`'s default behaviour for a missing home is to create it AND chown it
/// to the new user — which would hand the chroot itself to the customer.
/// OpenSSH would then refuse every login into it, and a customer who could write
/// the chroot's own directory is the starting point of every chroot escape there
/// is. The jail is made by this operation, as root, and `useradd` must leave it
/// alone.
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
/// [`AccountOwnership`](crate::sftp::model::account_ownership::AccountOwnership)
/// for why the login is given the account's identity instead of one of its own.
const NON_UNIQUE: &str = "--non-unique";

/// The subcommand that makes the service manager re-read its unit files.
const DAEMON_RELOAD: &str = "daemon-reload";

/// The subcommand that turns a unit on at boot.
const ENABLE: &str = "enable";

/// The flag that also starts the unit now, rather than at the next boot only.
const START_NOW: &str = "--now";

/// Creates `request`'s SFTP login, jailed to its account's home.
///
/// # What is built, and why in this order
///
/// 1. The account's jail — `/var/lib/maran-sftp/<account>`, root-owned `0755`,
///    with the account's real home bind-mounted at `home` inside it by a
///    systemd unit. Ensured on every call and idempotent, because an account's
///    second SFTP login must not fail on the first one's work.
/// 2. The system login, with a `nologin` shell, the ACCOUNT's user and group
///    ids, membership of the chroot group sshd's `Match Group` block names, and
///    its passwd home set to the jail — which is what `ChrootDirectory %h` in
///    that block resolves to.
/// 3. The password, over `chpasswd`'s standard input.
///
/// The login is created with the account's own identity, not one of its own.
/// That is the difference between a working file-transfer credential and a
/// login that reaches its jail, finds the account's home mounted inside it, and
/// cannot read a single file in it: the home is `<account>:<web server group>
/// 0750`, so a separate identity falls into the "other" bits and is refused
/// everything. The full argument is on
/// [`AccountOwnership`](crate::sftp::model::account_ownership::AccountOwnership);
/// the short version is that widening the home would break the isolation the
/// whole layout rests on, and joining the web server's group would grant one
/// customer read access to every other customer's home.
///
/// The jail comes first because a login that exists before its jail does is a
/// login that can be used before its home is mounted, and what it would find
/// then is an empty directory where the customer's files should be. Building it
/// first makes the incomplete state the harmless one: a jail with no login in it
/// yet.
///
/// **The account's home is never touched.** It stays
/// `<account>:<web server group> 0750`, exactly as account creation and every
/// nginx and php-fpm path expect — the jail is a separate root-owned directory,
/// and the home only appears inside it through the bind mount. That is the
/// whole reason the jail exists rather than the home being chrooted into
/// directly.
///
/// **There is no caller-supplied chroot path.** The jail is derived from the
/// account, so the chroot-escape class of bug has nothing to aim at: no request
/// can name the directory it will be confined to.
///
/// # Idempotency
///
/// A login that already exists is reported as [`SftpError::AlreadyExists`] and
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
/// - [`SftpError::AccountMissing`] when the hosting account is not on this
///   host. Checked first, before anything is created: a jail for an account
///   that does not exist is a root-owned directory nothing will ever mount into.
/// - [`SftpError::JailFailed`] when the jail's directories cannot be created,
///   or the mount unit cannot be written, validated or started. The login is
///   not created in that case.
/// - [`SftpError::AlreadyExists`] when the login is already on this host.
/// - [`SftpError::PasswordRejected`] when `chpasswd` refuses the password.
/// - [`SftpError::SpawnFailed`] when `useradd` refuses for any other reason, or
///   could not be run at all.
/// - [`SftpError::AccountBusy`] when another operation for the hosting account
///   — a deletion, a backup, a restore or a pool write — is already running.
/// - [`SftpError::AccountIdentityChanged`] when the account's uid or gid moved,
///   or the account went away, between the first read and `useradd`. The jail
///   built by this call is left in place in that case: it is a root-owned
///   directory and a mount unit, with no login and no credential in it, and
///   `remove_account_sftp` takes both away the next time an account of this
///   name is deleted.
pub fn create_sftp_user(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    request: &SftpUserRequest,
) -> Result<(), SftpError> {
    // Taken before the account's identity is read, and held until this function
    // returns: everything between the read and `useradd` is the window the
    // concurrency audit measured, and the account's deletion is what used to be
    // able to run inside it. Owned guard, so the lock is released by every path
    // out of the call below.
    let _guard = take_account_lock(&request.account).ok_or(SftpError::AccountBusy)?;

    create_sftp_user_under_lock(host, distro, request)
}

/// The creation itself, with the hosting account's lock ALREADY held.
///
/// Split from [`create_sftp_user`] for the reason `restore_backup`'s body is
/// split from its entry point: the lock is a process-wide static, and a dozen
/// unit tests driving the public entry for one account name on the harness's
/// own threads would refuse each other — a flaky suite whose flake says nothing
/// about the code. The exclusion is tested for what it is, once; everything
/// else about a creation is exercised here.
///
/// # Errors
///
/// Every variant [`create_sftp_user`] documents except
/// [`SftpError::AccountBusy`], which is the entry point's own answer.
pub(crate) fn create_sftp_user_under_lock(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    request: &SftpUserRequest,
) -> Result<(), SftpError> {
    let ownership = host.account_ownership(&request.account)?;

    let jail = AccountJail::for_account(&request.account, distro.systemd_unit_directory());
    ensure_jail(host, distro, &jail)?;

    // Asked AGAIN, immediately before the login is created, and compared with
    // what was read before the jail work. The lock above excludes this agent's
    // own deletion, so what this catches is what the lock cannot see: a
    // `userdel` an operator ran by hand, or a second agent binary. Without it,
    // `useradd --non-unique --uid` would create a login carrying a uid the host
    // may have handed to the next account — `--non-unique` does not care that
    // the number is now somebody else's, which is exactly why it cannot be the
    // thing that decides.
    if host.account_ownership(&request.account)? != ownership {
        return Err(SftpError::AccountIdentityChanged);
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
            distro.sftp_group(),
            NON_UNIQUE,
            UID,
            &uid,
            GID,
            &gid,
            request.user.as_str(),
        ],
        None,
    )?;

    if outcome.status != 0 {
        return Err(SftpError::from_useradd(outcome.status));
    }

    // `write_password` and not `set_sftp_password`: the entry point takes this
    // account's lock, which is already held here and never waits, and it
    // re-asserts the state it finds — which for a login `useradd` made a moment
    // ago is `!`, so it would lock the credential this operation exists to hand
    // out. A login that did not exist has no prior state worth restoring.
    write_password(host, distro, &request.user, &request.password)
}

/// Brings `jail` to the state an SFTP login needs, whether or not it was there.
///
/// The mount is a systemd unit rather than a `mount` call, and that is the one
/// decision in this function worth arguing. A mount made imperatively is gone at
/// the next boot: every SFTP login for the account would then land in an empty
/// jail, with nothing in the panel's records to say why and nothing that would
/// put it back short of re-creating a user. An enabled unit is re-established by
/// the service manager on every boot, so the jail is correct by construction
/// rather than for as long as the host stays up.
///
/// The unit goes through the config-write protocol like every other
/// configuration this agent writes, with `daemon-reload` as the validator and
/// `enable --now` as the reload. If the unit cannot be parsed or the mount
/// refuses to start, the previous unit file is restored and this fails —
/// leaving no half-installed mount behind.
///
/// # Errors
///
/// Returns [`SftpError::JailFailed`] when a directory cannot be created, the
/// unit cannot be rendered, or the protocol refuses it.
fn ensure_jail(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    jail: &AccountJail,
) -> Result<(), SftpError> {
    host.create_directory(Path::new(jail.directory()), JAIL_MODE)?;
    host.create_directory(Path::new(jail.mount_point()), JAIL_MODE)?;

    let contents = MountUnit {
        account: jail.account(),
        source_directory: jail.source_directory(),
        mount_point: jail.mount_point(),
    }
    .render_config()
    .map_err(|_| SftpError::JailFailed)?;

    let validator = Validator {
        // The absolute path of the service manager, from the adapter. `ops`
        // names no binary path of its own, and a bare `"systemctl"` would be
        // worse than the literal it replaced: a root process would resolve the
        // program through `PATH`.
        program: distro.service_manager(),
        arguments: &[DAEMON_RELOAD],
    };
    let reload_arguments = [ENABLE, START_NOW, jail.unit_name()];
    let reload = Reload {
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    host.write_config(Path::new(jail.unit_path()), &contents, &validator, &reload)
}

#[cfg(test)]
#[path = "../tests/sftp/create_sftp_user_tests.rs"]
mod tests;
