//! The account's FTPS jail, brought to the state a login needs — whether or not
//! it was there.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;
use maran_templates::systemd::unit::MountUnit;

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::ftps_jail::FtpsJail;
use crate::safe_write::model::{Reload, Validator};

/// The mode the jail and its mount point are created with.
///
/// `0755`, root-owned. vsftpd refuses to serve a login whose chroot root that
/// login can write to — `500 OOPS: vsftpd: refusing to run with writable root
/// inside chroot()` — so the login must not own the jail and must not be able
/// to write it. It must still be able to list the jail and traverse into
/// `home`, which is what the read and execute bits are for. This is the only
/// mode that satisfies both, so it is a constant rather than a parameter.
///
/// It is applied to the mount point too, and that is the load-bearing half: the
/// mount point's mode is only ever VISIBLE while the mount is down, because a
/// live bind mount covers it with the account's own `0750` home. So a client
/// that lands in a root-owned `home` it cannot write is looking at the
/// signature of a failed mount, rather than at a mystery.
const JAIL_MODE: u32 = 0o755;

/// The mode the jail BASE — the directory every account's jail hangs under —
/// is created with.
///
/// `0711`, root-owned: traversable by everyone, listable by nobody but root, so
/// no account learns the name of another account's jail from it. It is the mode
/// [`AgentPaths::FTPS_JAIL_ROOT`] specifies and the mode
/// `installer/lib/89-ftps.sh` creates it with, and it is repeated here because
/// the installer is not the only thing that can bring the directory into
/// existence.
///
/// `0700` does not work, and the reason is vsftpd-specific: it `chdir()`s into
/// the login's home AFTER dropping to the account's uid, so every component of
/// the jail path must be traversable by an unprivileged uid. `0755` — what
/// `create_dir_all` would leave behind on a host where the base is absent —
/// works, and silently gives up the not-listable half.
const JAIL_BASE_MODE: u32 = 0o711;

/// The subcommand that makes the service manager re-read its unit files.
const DAEMON_RELOAD: &str = "daemon-reload";

/// The subcommand that turns a unit on at boot.
const ENABLE: &str = "enable";

/// The flag that also starts the unit now, rather than at the next boot only.
const START_NOW: &str = "--now";

/// Creates `jail`'s directories and installs the enabled mount unit that fills
/// it.
///
/// # The base is ensured too, at its own mode
///
/// The first directory this creates is the jail BASE, at `0711`, and not the
/// account's jail. `create_directory` applies its mode to the leaf only, so on
/// a host where the base is absent — an install step that did not run, a
/// directory somebody removed, an agent older than that step — creating the
/// account's jail would bring the base into existence at `create_dir_all`'s
/// 0755. That is traversable AND listable, and the base is `0711` precisely so
/// that no account can list it and learn the names of its neighbours' jails.
/// Ensuring it here means the property holds whatever put the directory there,
/// and a base whose mode drifted is brought back on the next login creation.
///
/// Idempotent from end to end, because it runs on EVERY login creation: an
/// account's second FTPS login must not fail on the first one's work. The
/// directories are created with `create_dir_all`, which is success for a
/// directory that is already there, and the unit is written through the
/// config-write protocol, which replaces the file whole rather than appending
/// to it (rules/rust.md "Config writes").
///
/// The live unit is READ BACK and compared before anything is written, and the
/// write is skipped when the bytes already match. That is not tidiness: this
/// protocol's reload step is `enable --now`, so rewriting an identical unit
/// restarts a live bind mount underneath whatever logins the account already
/// has — a customer's transfer session losing its files mid-upload because
/// another login was being created. The `enable --now` still runs on the
/// unchanged path, because it is idempotent and is the one call that can bring
/// back a mount somebody stopped by hand.
///
/// # The mount is a unit, not a `mount` call
///
/// The one decision here worth arguing. A mount made imperatively is gone at
/// the next boot: every FTPS login for the account would then land in an empty
/// directory, with nothing in the panel's records to say why and nothing short
/// of re-creating a user that would put it back. An enabled unit is
/// re-established by the service manager on every boot, so the jail is correct
/// by construction rather than for as long as the host stays up.
///
/// The unit goes through the config-write protocol like every other
/// configuration this agent writes, with `daemon-reload` as the validator and
/// `enable --now` as the reload. If the unit cannot be parsed or the mount
/// refuses to start, the previous unit file is restored and this fails — so
/// there is no half-installed mount to find afterwards.
///
/// # The unit is shared with the SFTP jail's, and that is safe
///
/// [`MountUnit`] renders `What=`/`Where=` from the values it is given, and
/// [`FtpsJail`] derives both from the FTPS root. The two protocols' units
/// therefore have different `Where=` values, different escaped names and
/// different file paths, so enabling one can never disturb the other — an
/// account may hold logins of both kinds at once, and both mounts point at the
/// same home from two different jails.
///
/// # Its read-modify-write IS inside a critical section
///
/// This function reads the live unit, decides, and only then writes — the shape
/// rules/rust.md requires be one critical section. It is one here, and the
/// section is not this function's: `ensure_account_jail` is `pub(crate)` and is
/// reached only from `create_ftps_user`, which takes the hosting account's lock
/// as its first statement and holds it across this call. The unit this reads and
/// writes is derived from that same account and from nothing else, so the only
/// operations that could interleave on it are other operations for that account,
/// and they are exactly what the lock refuses.
///
/// It takes no lock of its own for that reason, and it must not: the account
/// lock never waits, so taking it again here would refuse `create_ftps_user` its
/// own jail.
///
/// # Errors
///
/// Returns [`FtpsError::JailFailed`] when a directory cannot be created, the
/// unit cannot be rendered, or the config-write protocol refuses it — which
/// includes the mount itself refusing to come up.
pub(crate) fn ensure_account_jail(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    jail: &FtpsJail,
) -> Result<(), FtpsError> {
    // The BASE first, at its own mode, and before anything that would create it
    // as a by-product. `create_directory` applies its mode to the leaf only, so
    // a jail created under an absent base would leave the base at
    // `create_dir_all`'s 0755 — traversable AND listable, which is the one
    // property the base exists to deny. This call is what makes the base's mode
    // an assertion rather than an assumption about which installer ran.
    host.create_directory(Path::new(AgentPaths::FTPS_JAIL_ROOT), JAIL_BASE_MODE)?;
    host.create_directory(Path::new(jail.directory()), JAIL_MODE)?;
    host.create_directory(Path::new(jail.mount_point()), JAIL_MODE)?;

    let contents = MountUnit {
        account: jail.account(),
        source_directory: jail.source_directory(),
        mount_point: jail.mount_point(),
    }
    .render_config()
    .map_err(|_| FtpsError::JailFailed)?;

    let unit_path = Path::new(jail.unit_path());
    if host.read_config(unit_path)?.as_deref() == Some(contents.as_str()) {
        // The unit is already exactly this, so it is NOT written again — the
        // config-write protocol's reload step is `enable --now`, and re-running
        // it over an identical file would restart a live bind mount underneath
        // an account's existing logins. Reading the live bytes back before
        // deciding is the same discipline the daemon's own configuration write
        // follows (rules/rust.md "Config writes").
        //
        // The unit is still enabled and started, because that call is idempotent
        // and is the only thing that can bring up a mount somebody stopped by
        // hand. Skipping the write is not the same as skipping the mount.
        let started = host.run(
            distro.service_manager(),
            &[ENABLE, START_NOW, jail.unit_name()],
        )?;
        if started.status != 0 {
            return Err(FtpsError::JailFailed);
        }

        return Ok(());
    }

    let validator = Validator {
        // The absolute path of the service manager, from the adapter. `ops`
        // names no binary path of its own, and a bare `"systemctl"` would be
        // worse than a literal: a root process would resolve the program
        // through `PATH`.
        program: distro.service_manager(),
        arguments: &[DAEMON_RELOAD],
    };
    let reload_arguments = [ENABLE, START_NOW, jail.unit_name()];
    let reload = Reload {
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    host.write_config(unit_path, &contents, &validator, &reload)
        .map_err(|_| FtpsError::JailFailed)
}
