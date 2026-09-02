//! Every path and name one account's SFTP jail is made of.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

/// The directory inside the jail the account's real home is mounted at.
///
/// One segment, fixed: an SFTP client that lands in the jail sees `home` and
/// nothing else, so there is no listing that tells one tenant anything about
/// another.
const MOUNT_POINT_SEGMENT: &str = "home";

/// The suffix systemd requires of a unit that mounts something.
const MOUNT_UNIT_SUFFIX: &str = ".mount";

/// The characters systemd leaves alone when it escapes a path into a unit name.
///
/// Everything else becomes `\xNN`, and `/` becomes `-`. Taken from systemd's own
/// rule rather than from what this agent's paths happen to contain, because the
/// name it produces has to be byte-for-byte the one systemd derives from
/// `Where=` — see [`AccountJail::unit_name`].
const UNESCAPED_SYMBOLS: &str = "_.";

/// The root-owned chroot one account's SFTP users log in to, and the bind mount
/// that puts the account's real home inside it.
///
/// The whole point of the type is that these five values are derived from one
/// account in one place. The unit's file name and the unit's `Where=` are
/// otherwise two independent spellings of the same path, and systemd refuses to
/// load a mount unit whose name is not the escaping of its own mount point — a
/// failure that appears only on a real host, as a login that lands in an empty
/// directory.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AccountJail {
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

impl AccountJail {
    /// Derives every path of `account`'s jail, with the unit written to
    /// `unit_directory`.
    ///
    /// `unit_directory` comes from the `DistroAdapter`: where a unit file must
    /// live is a fact of the service manager, and `ops` names no absolute
    /// system path of its own (rules/rust.md "Distro adapter"). The jail root
    /// and the home root are `AgentPaths` constants instead, because both are
    /// the agent's own decision and identical on every family.
    #[must_use]
    pub fn for_account(account: &AccountName, unit_directory: &str) -> Self {
        let directory = format!("{}/{}", AgentPaths::SFTP_JAIL_ROOT, account.as_str());
        let mount_point = format!("{directory}/{MOUNT_POINT_SEGMENT}");
        let unit_name = format!("{}{MOUNT_UNIT_SUFFIX}", escape_path(&mount_point));

        Self {
            account: account.as_str().to_owned(),
            source_directory: format!("{}/{}", AgentPaths::ACCOUNT_HOME_ROOT, account.as_str()),
            unit_path: format!("{unit_directory}/{unit_name}"),
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

    /// The jail directory: root-owned, and the directory sshd chroots into.
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
    /// A `.mount` unit is not free to be called what its author likes:
    /// systemd escapes `Where=` into a name and refuses to load a unit whose
    /// file name is not exactly that. `/var/lib/maran/sftp/alice/home` is
    /// therefore `var-lib-maran-sftp-alice-home.mount`, and a friendlier
    /// `maran-sftp-alice.mount` would be rejected at load time — on the host,
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

/// Escapes an absolute path the way systemd escapes one into a unit name.
///
/// The leading separator is dropped, every remaining `/` becomes `-`, ASCII
/// letters, digits and [`UNESCAPED_SYMBOLS`] are kept, and every other byte
/// becomes `\xNN` in lowercase hexadecimal.
///
/// The full rule is implemented although the paths this area builds — an
/// `AccountName` under two fixed roots — contain nothing that needs escaping
/// today. That is deliberate: writing only the substitution those paths need
/// would make the correctness of the unit name depend on the alphabet of a
/// validator in another crate, silently, and the next widening of that alphabet
/// would break SFTP on the host rather than in a test.
fn escape_path(path: &str) -> String {
    let mut escaped = String::with_capacity(path.len());

    for byte in path.trim_start_matches('/').bytes() {
        let character = char::from(byte);
        if byte == b'/' {
            escaped.push('-');
        } else if character.is_ascii_alphanumeric() || UNESCAPED_SYMBOLS.contains(character) {
            escaped.push(character);
        } else {
            escaped.push_str(&format!("\\x{byte:02x}"));
        }
    }

    escaped
}

#[cfg(test)]
#[path = "../../tests/sftp/account_jail_tests.rs"]
mod tests;
