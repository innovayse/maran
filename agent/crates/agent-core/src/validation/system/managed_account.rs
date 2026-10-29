//! The proof that a host user behind a name is a hosting account.

use crate::agent_paths::AgentPaths;
use crate::privs::account_ids::{account_id_floors, is_hosting_account};
use crate::utils::system_account::SystemAccount;
use crate::utils::system_accounts::system_accounts;
use crate::validation::system::managed_account_error::ManagedAccountError;
use crate::validation::system::name::AccountName;

/// The prefix of every name the panel keeps for its own services.
const PANEL_PREFIX: &str = "maran_";

/// Names no hosting account may bear, matched on the whole name.
///
/// 103 exact names in six classes, written down in
/// `docs/superpowers/notes/2026-10-26-account-reserved-names-threat-note.md`;
/// the backend carries the same list in its Accounts module and each side tests
/// its copy against that table. Every entry must itself be a valid
/// [`AccountName`], because a spelling the alphabet already refuses is a dead
/// entry that only looks like protection.
///
/// Best effort, and said so: the list covers users that do not exist yet (a
/// daemon installed later would find its name taken) and the default logins of
/// the common cloud images. For a user that already exists the floor and the
/// home check are the control, not this list.
const RESERVED_NAMES: &[&str] = &[
    // A. Superuser and base system identities.
    "root",
    "daemon",
    "bin",
    "sys",
    "sync",
    "games",
    "man",
    "mail",
    "news",
    "uucp",
    "proxy",
    "backup",
    "list",
    "irc",
    "gnats",
    "nobody",
    "nogroup",
    "adm",
    "halt",
    "shutdown",
    "operator",
    "gopher",
    "ftp",
    "www",
    "nfsnobody",
    // B. Daemon identities of software a hosting server installs.
    "nginx",
    "apache",
    "httpd",
    "sshd",
    "dbus",
    "polkitd",
    "rpc",
    "rpcuser",
    "chrony",
    "ntp",
    "tss",
    "tcpdump",
    "avahi",
    "colord",
    "sssd",
    "unbound",
    "named",
    "bind",
    "mysql",
    "mariadb",
    "postgres",
    "redis",
    "memcached",
    "mongodb",
    "mongod",
    "rabbitmq",
    "elasticsearch",
    "postfix",
    "dovecot",
    "dovenull",
    "exim",
    "opendkim",
    "clamav",
    "amavis",
    "spamd",
    "vsftpd",
    "saslauth",
    "mailnull",
    "smmsp",
    "squid",
    "haproxy",
    "varnish",
    "tomcat",
    "git",
    // C. The panel's own identity (the `maran_` prefix is checked separately).
    "maran",
    // D. Groups and roles other software trusts.
    "sudo",
    "wheel",
    "admin",
    "staff",
    "users",
    "shadow",
    "disk",
    "tty",
    "utmp",
    "dialout",
    "cdrom",
    "docker",
    "lxd",
    "kvm",
    "libvirt",
    // E. Role mailbox names.
    "administrator",
    "hostmaster",
    "postmaster",
    "webmaster",
    "abuse",
    "security",
    "noc",
    // F. Default login users of cloud images.
    "ubuntu",
    "debian",
    "almalinux",
    "rocky",
    "centos",
    "fedora",
    "ec2_user",
    "cloud_user",
    "vagrant",
    "azureuser",
    "opc",
];

/// A host user proven to be a hosting account this panel may act on as root.
///
/// The proof [`AccountName`] cannot give: a name only has a spelling, and
/// `root` is spelled correctly. Holding a `ManagedAccount` means that, when it
/// was resolved, all of these held, checked in this order:
///
/// 1. the name is not reserved;
/// 2. the local password database has exactly one row of that name;
/// 3. that row's home is exactly `<account home root>/<name>`;
/// 4. its uid and gid are not 0 and not below the host's floors, by the same
///    decision [`AccountIds`](crate::privs::account_ids::AccountIds) makes.
///
/// The lifecycle, cron, login-password and login-lock operations take this
/// proof and not the bare name. The exceptions (create, which takes a bare name
/// and refuses a pre-existing user; the SFTP and FTPS login deletions; and the
/// files, sites, php, ssl and backup areas, which go through `AccountIds` alone)
/// say why in their own doc comments.
///
/// DO NOT CACHE, CLONE OR STORE A VALUE OF THIS TYPE. It is a fact about one
/// moment: a uid recycled by `useradd` belongs to someone else an hour later.
/// It is deliberately neither `Clone` nor `Copy`; resolve again at the point of
/// use, inside the account lock where the operation takes one.
///
/// What this does NOT prove: that the panel created the user. An ordinary user
/// at or above the floor, with home `/home/<name>` and a name off the reserved
/// list, still passes. No marker is kept (see the threat note), so that residual
/// is open and is named there.
#[derive(Debug, PartialEq, Eq)]
pub struct ManagedAccount {
    /// The validated, non-reserved name.
    name: AccountName,
    /// The user id of the row, at or above the floor.
    uid: u32,
    /// The primary group id of the row, at or above the floor.
    gid: u32,
    /// The home of the row, exactly `<account home root>/<name>`.
    home: String,
}

impl ManagedAccount {
    /// Refuses a reserved name without reading any host fact.
    ///
    /// Called by account creation before `useradd`, where no user exists yet and
    /// so nothing can be resolved. A name is reserved when it is on the list or
    /// starts with the panel's own `maran_` prefix.
    ///
    /// # Errors
    ///
    /// Returns [`ManagedAccountError::Reserved`] when the name is reserved.
    pub fn refuse_reserved(name: &AccountName) -> Result<(), ManagedAccountError> {
        let candidate = name.as_str();
        if candidate.starts_with(PANEL_PREFIX) || RESERVED_NAMES.contains(&candidate) {
            return Err(ManagedAccountError::Reserved);
        }

        Ok(())
    }

    /// Resolves `name` against the text of the local password database.
    ///
    /// `passwd` is the TEXT of the file, handed in by the caller's host seam;
    /// this type reads no file of its own. The id floors are read from the
    /// host's `login.defs` at every call and not cached, because a cached floor
    /// goes stale when an operator retunes it.
    ///
    /// # Errors
    ///
    /// - [`ManagedAccountError::Reserved`] when the name is reserved.
    /// - [`ManagedAccountError::NotFound`] when the database has no row, or more
    ///   than one row, of exactly that name.
    /// - [`ManagedAccountError::WrongHome`] when the home is not exactly
    ///   `<account home root>/<name>`.
    /// - [`ManagedAccountError::SystemIdentity`] when the uid or gid is 0 or
    ///   below its floor.
    pub fn resolve(name: AccountName, passwd: &str) -> Result<Self, ManagedAccountError> {
        let (uid_minimum, gid_minimum) = account_id_floors();
        Self::admit(name, passwd, uid_minimum, gid_minimum)
    }

    /// Resolves `name` against rows already parsed from the local password
    /// database.
    ///
    /// For a caller whose host seam hands out parsed rows rather than text (the
    /// login area). The decision is the one [`ManagedAccount::resolve`] makes;
    /// the difference is that a malformed second line of the same name is no
    /// longer visible here, because the parser dropped it before this saw it.
    /// A caller that holds the text should prefer [`ManagedAccount::resolve`].
    ///
    /// # Errors
    ///
    /// The same as [`ManagedAccount::resolve`].
    pub fn resolve_rows(
        name: AccountName,
        rows: &[SystemAccount],
    ) -> Result<Self, ManagedAccountError> {
        let (uid_minimum, gid_minimum) = account_id_floors();
        Self::admit_rows(name, rows, uid_minimum, gid_minimum)
    }

    /// Resolves every row of `rows` that has a valid account name, reading the
    /// host's id floors once for the whole pass.
    ///
    /// For a caller that must examine each user of the database (a repair
    /// pass): asking [`ManagedAccount::resolve_rows`] per row would re-read the
    /// floors per row. Each answer is exactly the one `resolve_rows` gives for
    /// that name, including the refusal of a name carried by more than one row.
    /// Rows whose name is not a valid account name are skipped, since no
    /// hosting account can have one.
    #[must_use]
    pub fn resolve_each(
        rows: &[SystemAccount],
    ) -> Vec<(AccountName, Result<Self, ManagedAccountError>)> {
        let (uid_minimum, gid_minimum) = account_id_floors();

        rows.iter()
            .filter_map(|row| AccountName::parse(&row.name).ok())
            .map(|name| {
                let answer = Self::admit_rows(name.clone(), rows, uid_minimum, gid_minimum);
                (name, answer)
            })
            .collect()
    }

    /// The decision of [`ManagedAccount::resolve`] with the floors given.
    ///
    /// Split out so a test can state the floors instead of depending on the
    /// machine's `login.defs`. The order of the checks is the contract: reserved,
    /// then the row by exact name, then the home, then the ids.
    ///
    /// # Errors
    ///
    /// The same as [`ManagedAccount::resolve`].
    pub(crate) fn admit(
        name: AccountName,
        passwd: &str,
        uid_minimum: u32,
        gid_minimum: u32,
    ) -> Result<Self, ManagedAccountError> {
        Self::refuse_reserved(&name)?;

        // Rows are counted from the raw text, malformed ones included, so that a
        // second line of the same name which the parser skips still makes the
        // answer ambiguous. Ambiguity is refused, never resolved by picking one.
        let rows_by_name = passwd
            .lines()
            .filter(|line| line.split(':').next() == Some(name.as_str()))
            .count();
        if rows_by_name != 1 {
            return Err(ManagedAccountError::NotFound);
        }

        Self::admit_rows(name, &system_accounts(passwd), uid_minimum, gid_minimum)
    }

    /// The decision over parsed rows, with the floors given.
    ///
    /// The one place the reserved, row, home and id checks are written; both
    /// [`ManagedAccount::admit`] and [`ManagedAccount::resolve_rows`] end here.
    ///
    /// # Errors
    ///
    /// The same as [`ManagedAccount::resolve`].
    fn admit_rows(
        name: AccountName,
        rows: &[SystemAccount],
        uid_minimum: u32,
        gid_minimum: u32,
    ) -> Result<Self, ManagedAccountError> {
        Self::refuse_reserved(&name)?;

        let mut matching = rows.iter().filter(|row| row.name == name.as_str());
        let row = match (matching.next(), matching.next()) {
            (Some(row), None) => row,
            _ => return Err(ManagedAccountError::NotFound),
        };

        let expected_home = format!("{}/{}", AgentPaths::ACCOUNT_HOME_ROOT, name.as_str());
        if row.home != expected_home {
            return Err(ManagedAccountError::WrongHome);
        }

        is_hosting_account(row.uid, row.gid, uid_minimum, gid_minimum)
            .map_err(|_| ManagedAccountError::SystemIdentity)?;

        Ok(Self {
            name,
            uid: row.uid,
            gid: row.gid,
            home: expected_home,
        })
    }

    /// The validated name of the account.
    #[must_use]
    pub fn name(&self) -> &AccountName {
        &self.name
    }

    /// The account's user id at the moment of resolution.
    #[must_use]
    pub fn uid(&self) -> u32 {
        self.uid
    }

    /// The account's primary group id at the moment of resolution.
    #[must_use]
    pub fn gid(&self) -> u32 {
        self.gid
    }

    /// The account's home directory, exactly `<account home root>/<name>`.
    #[must_use]
    pub fn home(&self) -> &str {
        &self.home
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/managed_account_tests.rs"]
mod tests;
