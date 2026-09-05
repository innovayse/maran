//! Filesystem locations the agent owns outright, identical on every family.

use std::path::{Path, PathBuf};

use crate::validation::system::cron_entry_id::CronEntryId;
use crate::validation::system::name::AccountName;

/// Locations the agent creates and writes, outside every distribution's
/// packaged layout.
///
/// These are agent decisions, not platform facts: both families get the same
/// answer, so they live here once rather than as a method every
/// `DistroAdapter` must repeat with the same literal. A path that does differ
/// between families belongs in `maran-distro`, not here.
///
/// Most of them sit outside every account's home; the cron helpers are the
/// exception and say why in their own comments. They are functions rather than
/// constants because they are derived from an [`AccountName`] and an entry id,
/// which is what keeps "the path the agent built" and "the path a check
/// approved" the same path.
pub struct AgentPaths;

impl AgentPaths {
    /// Directory the agent's own nginx site includes are written to.
    ///
    /// Never `sites-available`/`sites-enabled` (Debian) or `conf.d` (RHEL) —
    /// those belong to the distribution's own packaging and the agent does not
    /// touch files it does not own (spec §9). Both families are configured,
    /// once, to include this directory from their packaged `nginx.conf`.
    pub const NGINX_INCLUDE_DIRECTORY: &'static str = "/etc/maran/nginx/sites";

    /// Base directory holding every account's home.
    ///
    /// An agent decision, not a platform fact — both families would accept a
    /// different root, and the panel picks this one — so it lives here rather
    /// than being written again by every unit that has to NAME a customer path
    /// before it exists. `validation::fs::path::resolve_in_home` roots its
    /// containment check at the same constant, which is what makes "the path
    /// this operation built" and "the path the check approved" the same path.
    pub const ACCOUNT_HOME_ROOT: &'static str = "/home";

    /// Directory the php-fpm pools' unix sockets are created in.
    ///
    /// The agent's own directory, not the one either family's php-fpm package
    /// ships (`/run/php` on Debian, `/run/php-fpm` on RHEL): the agent renders
    /// every pool it runs and therefore chooses where their sockets live, and
    /// choosing once means the nginx `fastcgi_pass` and the pool's `listen`
    /// cannot disagree about a path for family-specific reasons. A directory
    /// under `/run` also disappears on reboot, which is what keeps a stale
    /// socket from outliving the pool that owned it.
    pub const PHP_FPM_SOCKET_DIRECTORY: &'static str = "/run/maran/php";

    /// Base directory holding one root-owned SFTP jail per account.
    ///
    /// The chroot an SFTP login lands in is `<this>/<account>`, and the
    /// account's real home is bind-mounted at `<this>/<account>/home`. The jail
    /// exists because OpenSSH refuses to chroot into a directory that is not
    /// root-owned and not group- or world-writable, while an account's home is
    /// `<account>:<web server group> 0750` — an ownership every site, nginx
    /// vhost and php-fpm pool already depends on. Giving SFTP a jail of its own
    /// keeps that home exactly as it is, and there is no caller-supplied chroot
    /// path anywhere, so a chroot escape has nothing to aim at.
    ///
    /// Under `/var/lib` rather than `/run`, unlike
    /// [`Self::PHP_FPM_SOCKET_DIRECTORY`]: the jail must survive a reboot,
    /// because the `systemd` mount unit that fills it is enabled and expects
    /// its mount point to be there before the first login rather than after the
    /// next account operation.
    ///
    /// An agent decision that is identical on every family, so it belongs here
    /// and not on the `DistroAdapter` as the same literal written twice.
    pub const SFTP_JAIL_ROOT: &'static str = "/var/lib/maran/sftp";

    /// Directory the agent keeps certificate material in.
    ///
    /// A certificate is not part of a site's document root and must not be
    /// reachable through it, so the agent owns one directory of its own rather
    /// than deferring to `/etc/letsencrypt` and friends, which it does not use
    /// directly.
    pub const CERTIFICATE_DIRECTORY: &'static str = "/etc/maran/certificates";

    /// Directory, relative to an account's home, holding that account's cron
    /// artefacts.
    ///
    /// Under the home rather than under `/var/lib/maran`, because everything
    /// the agent does here it does AS the account: it creates the directory,
    /// writes the command file and reads the output back inside a forked child
    /// that has dropped to the account's uid. A root process reading files out
    /// of a directory an account owns is an arbitrary-file-read waiting for a
    /// symlink; under the account's own uid a symlink can only reach what the
    /// account already reads.
    ///
    /// [`Self::account_cron_dir`] is how callers reach it — the constant is
    /// public so the layout is documented in one place, not so that paths get
    /// joined by hand.
    pub const ACCOUNT_CRON_DIRECTORY: &'static str = ".maran/cron";

    /// The account's cron directory: `<home root>/<account>/.maran/cron`.
    #[must_use]
    pub fn account_cron_dir(account: &AccountName) -> PathBuf {
        PathBuf::from(Self::ACCOUNT_HOME_ROOT)
            .join(account.as_str())
            .join(Self::ACCOUNT_CRON_DIRECTORY)
    }

    /// The file holding one entry's command, verbatim.
    ///
    /// The crontab line runs `/bin/sh <this path>` and carries no byte of the
    /// customer's command, which is what keeps cron's own rewriting rules — a
    /// `%` becoming a newline, a `#` starting a comment — away from it
    /// entirely.
    #[must_use]
    pub fn cron_cmd_path(account: &AccountName, entry_id: &CronEntryId) -> PathBuf {
        Self::cron_entry_file(account, entry_id, ".cmd")
    }

    /// The file one entry's last run wrote its output to.
    ///
    /// Truncated on every run: the panel shows the LAST run's output, so the
    /// crontab line redirects with `>` rather than `>>` and this file never
    /// grows without bound.
    #[must_use]
    pub fn cron_log_path(account: &AccountName, entry_id: &CronEntryId) -> PathBuf {
        Self::cron_entry_file(account, entry_id, ".log")
    }

    /// The file one entry's last run wrote its exit status to.
    ///
    /// Both halves of a run record live in this one file: the CONTENT is the
    /// exit code and the MTIME is when the run finished. That is why the
    /// crontab line needs no `date` call — and therefore no `%`, which cron
    /// would have rewritten into a newline.
    #[must_use]
    pub fn cron_exit_path(account: &AccountName, entry_id: &CronEntryId) -> PathBuf {
        Self::cron_entry_file(account, entry_id, ".exit")
    }

    /// The file holding the rendered nftables rules.
    ///
    /// An agent-owned location and not a `DistroAdapter` answer, for the same
    /// reason as every constant above: the agent renders this file, replaces it
    /// whole and loads it, so it chooses where it lives, and both families
    /// would accept the same choice. What DOES differ per family is how the
    /// packaged nftables service is made to read it, and that wiring is a
    /// distro fact.
    ///
    /// Returned as a `&Path` rather than as a `&str` const because its callers
    /// hand it to filesystem calls; the older locations above are `&str`
    /// because theirs build strings.
    #[must_use]
    pub fn nftables_ruleset_path() -> &'static Path {
        Path::new("/etc/maran/firewall.nft")
    }

    /// The file holding the rendered nftables bans table.
    ///
    /// A second file, and a second table, because the ruleset file above is
    /// REPLACED whole on every apply — `nft -f` is additive, so the rendered
    /// ruleset deletes its own table and redeclares it — and a runtime ban
    /// living in that table would be erased by every rule change. Bans are
    /// elements of a table only this file declares, so replacing the rules
    /// cannot touch them.
    #[must_use]
    pub fn nftables_bans_path() -> &'static Path {
        Path::new("/etc/maran/firewall-bans.nft")
    }

    /// Root-owned directory the agent writes its own temporary files in.
    ///
    /// Mode 0700 and owned by root, and that is the whole point of it existing:
    /// a temporary file written by root anywhere an account can reach is a
    /// symlink an account can pre-plant. The crontab a root `crontab -u` reads
    /// is written here, never under the home of the account it is installed
    /// for.
    ///
    /// Under `/run`, like [`Self::PHP_FPM_SOCKET_DIRECTORY`] and unlike
    /// [`Self::SFTP_JAIL_ROOT`]: nothing here is meant to survive a reboot, and
    /// a scratch file that outlives the operation that made it is litter at
    /// best.
    #[must_use]
    pub fn agent_scratch_dir() -> &'static Path {
        Path::new("/run/maran/scratch")
    }

    /// Builds `<the account's cron directory>/<entry id><extension>`.
    ///
    /// One place composes these names, so the three run files of an entry
    /// cannot drift apart into three different spellings of its id.
    ///
    /// There is no traversal check here and there is deliberately none: the id
    /// arrives as a [`CronEntryId`], whose grammar is 36 characters of
    /// lowercase hex and four hyphens, so it cannot hold a `/`, a `..`, a
    /// leading `/` or a NUL. That matters because `Path::join` with an absolute
    /// string REPLACES the path it is joined to rather than appending to it —
    /// a check here would be a second answer to a question the type already
    /// answers, and the type answers it before the path exists at all.
    fn cron_entry_file(account: &AccountName, entry_id: &CronEntryId, extension: &str) -> PathBuf {
        Self::account_cron_dir(account).join(format!("{}{extension}", entry_id.as_str()))
    }
}

#[cfg(test)]
#[path = "tests/agent_paths_tests.rs"]
mod tests;
