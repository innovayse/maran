//! Every path and name one account's FTPS jail is made of.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

use crate::logins::systemd_escape::systemd_escape;

/// The directory inside the jail the account's real home is mounted at.
///
/// One segment, fixed: an FTPS client that lands in the jail sees `home` and
/// nothing else, so there is no listing that tells one tenant anything about
/// another.
const MOUNT_POINT_SEGMENT: &str = "home";

/// The suffix systemd requires of a unit that mounts something.
const MOUNT_UNIT_SUFFIX: &str = ".mount";

/// The root-owned chroot one account's FTPS logins land in, and the bind mount
/// that puts the account's real home inside it.
///
/// The whole point of the type is that these five values are derived from one
/// account in one place. The unit's file name and the unit's `Where=` are
/// otherwise two independent spellings of the same path, and systemd refuses to
/// load a mount unit whose name is not the escaping of its own mount point — a
/// failure that appears only on a real host, as a login that lands in an empty
/// directory.
///
/// # Why this is a second type and not a parameter on `AccountJail`
///
/// The two jails are the same shape and have different lifetimes: an account
/// can hold SFTP logins and no FTPS ones, or the reverse, and deleting the last
/// login of one protocol must not unmount the other. A single type parameterised
/// by root would make "which jail am I tearing down" a value that has to be
/// right at every call site, including the account-deletion cascade, where
/// getting it wrong unmounts a live customer home. Two types make the wrong one
/// a compile error rather than a runtime one.
///
/// What the two DO share is systemd's escaping rule, and that is shared as a
/// function both call
/// (`logins::systemd_escape`, crate-private), not as a shared type: it is the
/// one part where a second copy could drift into a unit name systemd will not
/// load.
///
/// There is **no caller-supplied path here at all.** Every value is derived
/// from a validated [`AccountName`] and two agent-owned roots, so the
/// chroot-escape class is gone by construction rather than by a check that has
/// to be right every time.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FtpsJail {
    /// The account the jail belongs to.
    account: String,
    /// Absolute path of the jail directory, which is the chroot itself.
    directory: String,
    /// Absolute path of the mount point inside the jail.
    mount_point: String,
    /// Absolute path of the account's real home, which is what gets mounted.
    source_directory: String,
    /// The mount unit's file name, escaped as systemd escapes a path.
    unit_name: String,
    /// Absolute path the unit file is written to.
    unit_path: String,
}

impl FtpsJail {
    /// Derives every path of `account`'s FTPS jail, with the unit written to
    /// `systemd_unit_directory`.
    ///
    /// `systemd_unit_directory` comes from the `DistroAdapter`: where a unit
    /// file must live is a fact of the service manager, and `ops` names no
    /// absolute system path of its own (rules/rust.md "Distro adapter"). The
    /// jail root and the home root are `AgentPaths` constants instead, because
    /// both are the agent's own decision and identical on every family.
    #[must_use]
    pub fn for_account(account: &AccountName, systemd_unit_directory: &str) -> Self {
        let directory = format!("{}/{}", AgentPaths::FTPS_JAIL_ROOT, account.as_str());
        let mount_point = format!("{directory}/{MOUNT_POINT_SEGMENT}");
        let unit_name = format!("{}{MOUNT_UNIT_SUFFIX}", systemd_escape(&mount_point));

        Self {
            account: account.as_str().to_owned(),
            source_directory: format!("{}/{}", AgentPaths::ACCOUNT_HOME_ROOT, account.as_str()),
            unit_path: format!("{systemd_unit_directory}/{unit_name}"),
            unit_name,
            mount_point,
            directory,
        }
    }

    /// The account the jail belongs to.
    #[must_use]
    pub fn account(&self) -> &str {
        &self.account
    }

    /// The jail directory: root-owned, and the directory vsftpd chroots into.
    #[must_use]
    pub fn directory(&self) -> &str {
        &self.directory
    }

    /// The mount point inside the jail, where the real home appears.
    #[must_use]
    pub fn mount_point(&self) -> &str {
        &self.mount_point
    }

    /// The account's real home — the directory that is bind-mounted, and that
    /// this area never modifies.
    #[must_use]
    pub fn source_directory(&self) -> &str {
        &self.source_directory
    }

    /// The mount unit's name, which systemd derives from the mount point and
    /// will not accept in any other spelling.
    ///
    /// A `.mount` unit is not free to be called what its author likes: systemd
    /// escapes `Where=` into a name and refuses to load a unit whose file name
    /// is not exactly that. `/var/lib/maran-ftps/alice/home` is therefore
    /// `var-lib-maran\x2dftps-alice-home.mount` — the `-` of the jail root's
    /// own name is NOT a path separator and is escaped — and a friendlier
    /// `maran-ftps-alice.mount` would be rejected at load time, on the host,
    /// never in a build.
    #[must_use]
    pub fn unit_name(&self) -> &str {
        &self.unit_name
    }

    /// Absolute path the unit file is written to.
    #[must_use]
    pub fn unit_path(&self) -> &str {
        &self.unit_path
    }
}

#[cfg(test)]
#[path = "../../tests/ftps/ftps_jail_tests.rs"]
mod tests;
