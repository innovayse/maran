//! The account's SFTP jail, brought to the state a login needs — whether or not
//! it was there.

use std::path::Path;

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;
use maran_templates::systemd::unit::MountUnit;

use crate::safe_write::model::{Reload, Validator};
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The mode the jail and its mount point are created with.
///
/// `0755`, root-owned: OpenSSH refuses to chroot into a directory that is
/// group- or world-writable, and it refuses one that is not owned by root. The
/// login still has to be able to list the jail to find `home` in it, which is
/// what the read and execute bits are for. This is the only mode that satisfies
/// both, so it is a constant rather than a parameter.
///
/// The mount point is given it through [`SftpHost::ensure_mount_point`] and never
/// through `create_directory`: once the unit is up the mount point IS the
/// account's home, and a `chmod` by path goes through the mount onto it
/// (issue #483).
const JAIL_MODE: u32 = 0o755;

/// The subcommand that makes the service manager re-read its unit files.
const DAEMON_RELOAD: &str = "daemon-reload";

/// The subcommand that turns a unit on at boot.
const ENABLE: &str = "enable";

/// The flag that also starts the unit now, rather than at the next boot only.
const START_NOW: &str = "--now";

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
/// Runs under the hosting account's lock, held by its callers
/// (`create_sftp_user` and `refresh_account_jail`), and takes none of its own.
///
/// # Errors
///
/// Returns [`SftpError::JailFailed`] when a directory cannot be created, the
/// unit cannot be rendered, or the protocol refuses it. Returns
/// [`SftpError::Bound`] when the service manager outruns its deadline.
pub(crate) fn ensure_account_jail(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    jail: &AccountJail,
) -> Result<(), SftpError> {
    host.create_directory(Path::new(jail.directory()), JAIL_MODE)?;
    // The mount point is not the jail: once the unit is up it IS the account's
    // home, and this runs on every login creation (issue #483). The home's mode
    // is not this function's to set.
    host.ensure_mount_point(
        Path::new(jail.mount_point()),
        Path::new(jail.source_directory()),
        JAIL_MODE,
    )?;

    let contents = MountUnit {
        account: jail.account(),
        source_directory: jail.source_directory(),
        mount_point: jail.mount_point(),
    }
    .render_config()
    .map_err(|_| SftpError::JailFailed)?;

    // The absolute path of the service manager, from the adapter. `ops` names no
    // binary path of its own, and a bare `"systemctl"` would be worse than the
    // literal it replaced: a root process would resolve the program through
    // `PATH`.
    let service_manager = distro.service_manager();
    let validator = Validator {
        program: &service_manager,
        arguments: &[DAEMON_RELOAD],
        // `daemon-reload` mutates the service manager's state, so it is a
        // `Reload` and not a pure validation.
        class: CallClass::Reload,
    };
    // `systemctl` honours the end-of-options marker (`getopt_long`), so the unit
    // name after it can never be read as an option.
    let reload_arguments = [ENABLE, START_NOW, END_OF_OPTIONS, jail.unit_name()];
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };

    host.write_config(Path::new(jail.unit_path()), &contents, &validator, &reload)
}
