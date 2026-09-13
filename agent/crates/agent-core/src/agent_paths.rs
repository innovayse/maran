//! Filesystem locations the agent owns outright, identical on every family.

use std::path::{Path, PathBuf};

use crate::validation::system::backup_id::BackupId;
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
    /// Directly under `/var/lib`, and deliberately NOT under `/var/lib/maran`,
    /// which is where it used to live — the same relocation, for a related
    /// reason, as [`Self::BULK_SCRATCH_ROOT`]. OpenSSH does not merely require
    /// the chroot directory to be root-owned; it walks EVERY component of the
    /// path and refuses the login if any one of them is owned by another uid or
    /// is group- or other-writable. `/var/lib/maran` is created `maran:maran
    /// 0750` by `installer/lib/40-user.sh`, and step 40 runs before the SFTP
    /// step, so while the base was `/var/lib/maran/sftp` every SFTP login on
    /// every real install was refused. Measured on both supported families:
    ///
    /// ```text
    /// Accepted password for <login> from 127.0.0.1 port 58788 ssh2
    /// bad ownership or modes for chroot directory component "/var/lib/maran/"
    /// ```
    ///
    /// The client sees only a connection that closes after the password was
    /// accepted; the reason exists solely in the daemon's log, which is why this
    /// survived a passing local suite and a jail base whose own mode was right.
    /// `/var/lib` is `root:root 0755` on both families, so at the sibling every
    /// component of the path belongs to root.
    ///
    /// The same move also removes an escalation: while the panel uid owned an
    /// ancestor of this directory it could rename a level aside and leave an
    /// entry of its own at that name — here, at the name of every customer's
    /// chroot — without ever having permission to enter it.
    ///
    /// The `-` in the name is not free: a `.mount` unit's file name is
    /// systemd's escaping of its `Where=`, and `-` is the escaping of `/`, so
    /// the account's mount unit is `var-lib-maran\x2dsftp-<account>-home.mount`
    /// and not the readable spelling. `AccountJail` derives it through
    /// `ops::logins::systemd_escape`, so nothing here has to be spelled twice.
    /// That function is byte-identical to `systemd-escape --path` for every path
    /// these jails can produce — measured over all of them against the real tool
    /// — and it deliberately does NOT implement systemd's whole rule: four cases
    /// outside that domain differ, and they are named where it is defined. An
    /// account name is `[a-z][a-z0-9_]{2,29}`, so none of the four is reachable.
    ///
    /// An agent decision that is identical on every family, so it belongs here
    /// and not on the `DistroAdapter` as the same literal written twice.
    pub const SFTP_JAIL_ROOT: &'static str = "/var/lib/maran-sftp";

    /// Base directory holding one root-owned FTPS jail per account.
    ///
    /// The chroot an FTPS login lands in is `<this>/<account>`, and the
    /// account's real home is bind-mounted at `<this>/<account>/home` — the
    /// same shape as [`Self::SFTP_JAIL_ROOT`] and for the same reason: an
    /// account's home is `<account>:<web server group> 0750`, an ownership
    /// every site, vhost and php-fpm pool depends on, so the jail is a
    /// directory of the agent's own rather than the home itself.
    ///
    /// A SEPARATE base from the SFTP one, not a subdirectory of it and not
    /// shared with it. The two protocols' jails have different lifetimes: an
    /// account may hold logins of one and none of the other, and removing the
    /// last login of one must not unmount the other's bind mount out from
    /// under a live customer.
    ///
    /// Directly under `/var/lib`, and deliberately NOT under `/var/lib/maran`,
    /// which `installer/lib/40-user.sh` creates `maran:maran 0750`. That is the
    /// defect that made every SFTP login on every real install fail, and it is
    /// an escalation as well as a refusal: while the panel uid owned an
    /// ancestor of this directory it could rename a level aside and leave an
    /// entry of its own at the name every customer's chroot hangs under,
    /// without ever having permission to enter it. `/var/lib` is `root:root
    /// 0755` on both families, so at the sibling every component belongs to
    /// root.
    ///
    /// # The mode is `0711`, and the reason is not the one the SFTP jail has
    ///
    /// `installer/lib/89-ftps.sh` creates this directory `root:root 0711`:
    /// traversable by everyone, listable by nobody but root. `0700` does not
    /// work here even though the directory is root's, because **vsftpd
    /// `chdir()`s into the login's home AFTER dropping to the account's uid**,
    /// unlike sshd, which chroots while it is still root. Every component of
    /// the jail path must therefore be traversable by an unprivileged uid.
    /// Measured on a polygon host, both ways, with TLS switched off so the
    /// daemon's own message was readable:
    ///
    /// ```text
    /// 0700 -> 500 OOPS: cannot change directory:/var/lib/maran-ftps/<account>
    /// 0711 -> 226 Directory send OK.
    /// ```
    ///
    /// Under forced TLS the customer's symptom is worse than that message: the
    /// connection simply breaks, because vsftpd writes the 500 in the clear on
    /// a channel the client is reading as TLS. `0711` grants traversal and
    /// nothing else — the directory cannot be listed, so no account learns the
    /// name of another account's jail from it — and it is the mode step 40
    /// already gives `/home/.maran-restore` for the same reason.
    ///
    /// The `-` in the name is not free: a `.mount` unit's file name is
    /// systemd's escaping of its `Where=`, and `-` is the escaping of `/`, so
    /// the account's mount unit is `var-lib-maran\x2dftps-<account>-home.mount`
    /// and not the readable spelling. `ops::ftps::FtpsJail` derives it through
    /// `ops::logins::systemd_escape` — the same function `AccountJail` calls, so
    /// nothing here has to be spelled twice, and one edit reaches both protocols.
    /// It is byte-identical to `systemd-escape --path` for every path these jails
    /// can produce, and deliberately not for four cases outside that domain,
    /// which an account name cannot express.
    ///
    /// An agent decision that is identical on every family, so it belongs here
    /// and not on the `DistroAdapter` as the same literal written twice.
    pub const FTPS_JAIL_ROOT: &'static str = "/var/lib/maran-ftps";

    /// The one `vsftpd.conf` this panel's FTPS daemon is started against.
    ///
    /// Maran's own file, never the distribution's. The two families disagree
    /// about where a packaged vsftpd keeps its configuration —
    /// `/etc/vsftpd.conf` on one, `/etc/vsftpd/vsftpd.conf` on the other — and
    /// neither of those paths is read by anything here: `installer/lib/89-ftps.sh`
    /// masks the packaged unit, and `installer/systemd/maran-ftps.service` names
    /// this path on its `ExecStart` line. Because the path the daemon reads is
    /// the agent's own decision rather than a fact about the platform, it
    /// belongs here and not on the `DistroAdapter`.
    ///
    /// The file is written WHOLE, through `ops::safe_write`, and never appended
    /// to or merged into. vsftpd's parser takes the **LAST** occurrence of a
    /// key, so an appended `force_local_logins_ssl=NO` does not sit inert below
    /// the rendered `YES` — it replaces it, on a file that still parses and a
    /// daemon that still starts.
    pub const VSFTPD_CONFIG_PATH: &'static str = "/etc/maran/vsftpd/vsftpd.conf";

    /// The systemd unit the FTPS daemon runs as.
    ///
    /// `installer/systemd/maran-ftps.service`, which is `Type=simple` and runs
    /// vsftpd in the foreground with `-obackground=NO`. That shape is why
    /// starting the unit is not on its own evidence that the daemon is up: a
    /// `Type=simple` start succeeds as soon as the process has been forked, so
    /// the unit's own `is-active` — and then the control port's greeting — are
    /// separate questions, asked separately (`ops::ftps::get_ftps_status`).
    ///
    /// An agent decision identical on every family, so it belongs here.
    pub const FTPS_UNIT: &'static str = "maran-ftps.service";

    /// Root-owned directory the agent stages temporary files whose size is the
    /// CUSTOMER's, not the agent's, in.
    ///
    /// The counterpart to [`Self::agent_scratch_dir`], and the difference
    /// between them is which resource a runaway file exhausts. That directory
    /// is under `/run`, a tmpfs: its capacity is a slice of physical memory —
    /// 10% of it on both families — so a database dump staged there is not
    /// stored on a disk at all, and a dump larger than the slice is an
    /// out-of-memory condition on a live server rather than a failed backup.
    /// This directory is on real storage, under the agent's own state root,
    /// where the resource a large dump consumes is the resource everyone
    /// already reasons about it consuming.
    ///
    /// Directly under `/var/lib`, and deliberately NOT under `/var/lib/maran`,
    /// which is where it used to live. `/var/lib/maran` is created
    /// `maran:maran 0750` by the installer, because the API — an unprivileged
    /// process and the largest attack surface this product exposes — writes its
    /// own state there. Staging root-only data inside a directory an
    /// unprivileged uid OWNS is not a boundary at all, whatever the modes on
    /// the leaves say: the owner of a parent directory can rename an entry
    /// aside and put a symlink in its place without ever having permission to
    /// enter it, and root's next write then lands wherever the symlink points.
    /// That was measured, both directions — a customer's plaintext dump
    /// delivered into a panel-owned file, and a root-owned `0600` file
    /// truncated and overwritten by the dump — in
    /// `docs/superpowers/notes/2026-09-05-backups-threat-note.md` §1. `/var/lib`
    /// itself is `root:root 0755` on both families, so no unprivileged uid can
    /// place an entry at this name in the first place. The operations that use
    /// this root check the whole ancestor chain as well, because a path that is
    /// out of reach today is out of reach only for as long as somebody keeps it
    /// that way.
    ///
    /// NOT under [`Self::BACKUP_ROOT`], which was
    /// the other candidate and is disk-backed too: the backup root is
    /// operator-configurable
    /// ([`crate::validation::system::local_backup_root::LocalBackupRoot`]), so
    /// staging under it would mean the agent's temporary files move whenever an
    /// operator moves their archives, and a directory of half-written dumps
    /// would appear inside the tree whose whole contract is "the artifacts you
    /// may restore from". Staging is the agent's business and lives with the
    /// agent's other state.
    ///
    /// It is NOT reboot-clean, which `/run` was. Nothing here is meant to
    /// survive a restart, so the shipped `systemd` unit empties this directory
    /// before the daemon starts; see `installer/systemd/maran-agent.service`.
    /// The operations that write here also remove their own subdirectory on
    /// every exit path, success and failure alike — the unit is the second of
    /// two locks, for the run that was killed rather than returned from.
    pub const BULK_SCRATCH_ROOT: &'static str = "/var/lib/maran-scratch";

    /// Base directory holding one root-owned log directory per account, for the
    /// logs the web server writes on that account's sites.
    ///
    /// `root:root 0750`, inside `/var/log/maran` — which is itself `root:maran
    /// 0750` — so the leaf a customer's site names has an ancestor chain of
    /// `/` `0755`, `/var` `0755`, `/var/log` `0755`, `/var/log/maran` `0750
    /// root:maran`, and this directory `0750 root:root`. A hosting account is a
    /// member of its own group and of nothing else, so from `/var/log/maran`
    /// downwards it matches neither owner nor group and its permission bits are
    /// `---`: it cannot create an entry, rename one, or so much as `stat` a
    /// path below that point. Even the PANEL uid stops here, at the `root:root`
    /// group of this directory.
    ///
    /// **These logs used to be `/home/<account>/logs/<domain>.access.log`, and
    /// that was a root compromise any customer could perform.** nginx's MASTER
    /// process runs as root and is what opens every `access_log`/`error_log`
    /// target, `O_WRONLY|O_APPEND|O_CREAT` and **without `O_NOFOLLOW`**. A
    /// customer who owns the directory replaces the file with a symbolic link
    /// to any path root can write — `/etc/ld.so.preload` was the one actually
    /// proved — and the next reload has root create that file and append a line
    /// whose content the customer chooses, because an access-log line embeds
    /// the request target. Every `CreateSite`, `EnableSite`, `DisableSite`,
    /// `DeleteSite`, `InstallCertificate` and `UpdateSitePhpVersion` ends in a
    /// reload, and one nginx serves every tenant, so any other customer's site
    /// action was enough to spring it. See
    /// `docs/superpowers/notes/2026-09-09-site-logs-threat-note.md` for the
    /// probe transcript.
    ///
    /// `chown root` on the old directory would NOT have fixed it: the account
    /// owns its home, so it renames `logs` aside and puts its own directory at
    /// that name. The containment has to come from the path being outside the
    /// home entirely — the same lesson as [`Self::BULK_SCRATCH_ROOT`] and
    /// [`Self::SFTP_JAIL_ROOT`], and the reason those are siblings of
    /// `/var/lib/maran` rather than children of it.
    ///
    /// Inside `/var/log/maran` and NOT a fourth root-only sibling, because that
    /// tree was already split along exactly this line one release ago: root
    /// owns the directory and writes the install log and the panel vhost's
    /// nginx logs directly in it, and the panel gets one subdirectory of its
    /// own (`/var/log/maran/panel`, `maran:maran 0750`). A second root-owned
    /// subdirectory continues that scheme instead of inventing another one, and
    /// an operator's `grep -r /var/log/maran` still finds everything.
    pub const SITE_LOG_ROOT: &'static str = "/var/log/maran/sites";

    /// The directory holding one account's site logs:
    /// `<site log root>/<account>`.
    ///
    /// `root:root 0750`, created by the root daemon with an explicit mode and
    /// deliberately NOT through `fork_as_account`. That reverses this area's
    /// usual rule — customer paths are touched under the account's uid — and
    /// the reversal is the point: this is no longer a customer path. The rule
    /// exists so root never follows a link a customer planted, and in a tree no
    /// customer can write to there is no link to follow. The document root is
    /// unchanged and is still created as the account.
    #[must_use]
    pub fn account_site_log_dir(account: &AccountName) -> PathBuf {
        PathBuf::from(Self::SITE_LOG_ROOT).join(account.as_str())
    }

    /// Directory the agent keeps certificate material in.
    ///
    /// A certificate is not part of a site's document root and must not be
    /// reachable through it, so the agent owns one directory of its own rather
    /// than deferring to `/etc/letsencrypt` and friends, which it does not use
    /// directly.
    pub const CERTIFICATE_DIRECTORY: &'static str = "/etc/maran/certificates";

    /// Root directory holding every account's local backup artifacts.
    ///
    /// Outside every home and every document root, `root:root 0700`. The
    /// location is the one the filesystem hierarchy already assigns to exactly
    /// this kind of file, and it exists on both families, so the agent adopts
    /// it rather than inventing a directory of its own under `/var/lib`.
    ///
    /// This is the DEFAULT and not the only legal value: an operator may point
    /// the panel at another directory, which is what
    /// [`crate::validation::system::local_backup_root::LocalBackupRoot`]
    /// validates. That type reads this constant as the value it must approve,
    /// so the shipped root and the rules the shipped root is held to cannot
    /// drift apart.
    pub const BACKUP_ROOT: &'static str = "/var/backups/maran";

    /// Root directory the two halves of a restore's directory swap live in.
    ///
    /// Under [`Self::ACCOUNT_HOME_ROOT`] on purpose, and that is the whole
    /// reason it exists as a location of its own rather than under
    /// `/var/lib/maran`. A restore replaces an account's home with two
    /// `rename` calls — the live home becomes
    /// [`Self::restore_previous_dir`], then
    /// [`Self::restore_staging_dir`] becomes the live home — and `rename` is
    /// atomic only WITHIN one filesystem. Homes frequently sit on a filesystem
    /// of their own, so staging anywhere else silently turns each of those
    /// renames into a recursive copy: not atomic, not reversible halfway
    /// through, and slow in proportion to the customer's data. A future change
    /// that moves either staging path out of this root breaks that and nothing
    /// else in the code would object, which is why the reason is written here
    /// and asserted in a test.
    ///
    /// The name begins with a dot and account names cannot, so this directory
    /// can never collide with an account's home.
    pub const RESTORE_STAGING_ROOT: &'static str = "/home/.maran-restore";

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

    /// Root-owned directory the agent writes its own SMALL temporary files in.
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
    ///
    /// **`/run` is a tmpfs, so every byte written here is RESIDENT KERNEL
    /// MEMORY, not disk.** It is sized as a fraction of RAM — `systemd`
    /// mounts it at 10% of physical memory on both families this product
    /// supports, measured at `size=1604940k` against a `MemTotal` of
    /// `16049400 kB` on the development host — so a 2 GiB VPS, a real customer
    /// of this panel, has roughly 200 MiB here in total, shared with every
    /// other consumer of `/run`. Filling it does not fail one operation; it
    /// takes memory away from every process on a live server that the agent
    /// runs as root on.
    ///
    /// That is why this directory is for files whose size is bounded by
    /// something OTHER than the customer's data — a rendered crontab table is
    /// kilobytes — and why bulk staging goes to
    /// [`Self::BULK_SCRATCH_ROOT`] instead. A new caller that stages anything
    /// proportional to what an account stores belongs there, not here.
    #[must_use]
    pub fn agent_scratch_dir() -> &'static Path {
        Path::new("/run/maran/scratch")
    }

    /// The account's backup directory: `<backup root>/<account>`.
    ///
    /// One directory per account, `root:root 0700`, so listing one account's
    /// artifacts is a directory read rather than a scan of everybody's.
    #[must_use]
    pub fn account_backup_dir(account: &AccountName) -> PathBuf {
        PathBuf::from(Self::BACKUP_ROOT).join(account.as_str())
    }

    /// The suffix every published backup artifact wears, in every destination.
    ///
    /// **The one spelling of this string in the workspace.** It was five: this
    /// method, `object_key.rs`, `list_backups.rs`, `delete_backup.rs` and an
    /// inline `format!` in `restore_backup.rs`, each of which had to stay equal
    /// to the other four for a listing to see what a creation published and for
    /// a delete to remove what a restore reads. A constant that four files
    /// agree on by inspection is four chances to be the odd one out, and the
    /// failure is silent in the worst direction: a listing that returns nothing
    /// is indistinguishable from an account with no backups, which is what
    /// retention prunes against.
    ///
    /// It is public and lives here, rather than in `ops::backup`, because
    /// `ops` already asks this type what a backup file is CALLED
    /// ([`Self::backup_artifact_path`]) and a suffix is the half of that answer
    /// a caller needs on its own — to strip it off a directory entry, or to
    /// build a key for a destination that has no filesystem path at all.
    pub const BACKUP_ARTIFACT_SUFFIX: &'static str = ".tar.gz";

    /// The suffix every backup's sidecar wears, beside its artifact.
    ///
    /// The one spelling, for the reason [`Self::BACKUP_ARTIFACT_SUFFIX`] gives;
    /// it was four.
    pub const BACKUP_SIDECAR_SUFFIX: &'static str = ".meta.json";

    /// The archive one backup produced: `<account's backup dir>/<id>.tar.gz`.
    #[must_use]
    pub fn backup_artifact_path(account: &AccountName, backup_id: &BackupId) -> PathBuf {
        Self::backup_file(account, backup_id, Self::BACKUP_ARTIFACT_SUFFIX)
    }

    /// The sidecar describing one backup:
    /// `<account's backup dir>/<id>.meta.json`.
    ///
    /// Beside the archive rather than inside it, because listing must be able
    /// to describe a backup without decompressing it. The copy INSIDE the
    /// archive is the authority a restore checks against — a sidecar that
    /// disagrees with it is a refusal, not a tie to break.
    #[must_use]
    pub fn backup_sidecar_path(account: &AccountName, backup_id: &BackupId) -> PathBuf {
        Self::backup_file(account, backup_id, Self::BACKUP_SIDECAR_SUFFIX)
    }

    /// The root-only directory one backup's database dumps are staged in:
    /// `<bulk scratch root>/backup/<id>`.
    ///
    /// Root-only and never under a home, for the reason the agent has a scratch
    /// at all: a dump written where an account can reach it can be replaced
    /// between being written and being read, and the process that reads it
    /// connects to the database as its superuser.
    ///
    /// Under [`Self::BULK_SCRATCH_ROOT`] and no longer under
    /// [`Self::agent_scratch_dir`], and the move is the point rather than a
    /// tidy-up. What is staged here is one file per database holding a full
    /// dump of it, plus — during a restore — a second file per database holding
    /// the pre-drop rollback dump, and all of them coexist until the operation
    /// ends. Its peak is therefore the customer's entire database estate,
    /// twice; on `/run` that peak was charged to RAM.
    #[must_use]
    pub fn backup_scratch_dir(backup_id: &BackupId) -> PathBuf {
        PathBuf::from(Self::BULK_SCRATCH_ROOT)
            .join("backup")
            .join(backup_id.as_str())
    }

    /// Where a restore builds the new home before it becomes the home:
    /// `<restore staging root>/<account>.<id>`.
    ///
    /// Shares its parent with [`Self::restore_previous_dir`] so the swap is two
    /// same-filesystem renames; [`Self::RESTORE_STAGING_ROOT`] carries the full
    /// argument.
    #[must_use]
    pub fn restore_staging_dir(account: &AccountName, backup_id: &BackupId) -> PathBuf {
        Self::restore_staging_entry(account, backup_id, "")
    }

    /// Where the home a restore replaced is kept until the restore has
    /// succeeded: `<restore staging root>/<account>.previous.<id>`.
    ///
    /// This directory is what makes everything before the first `DROP DATABASE`
    /// undoable: while it exists, the previous home is one rename away from
    /// being live again.
    #[must_use]
    pub fn restore_previous_dir(account: &AccountName, backup_id: &BackupId) -> PathBuf {
        Self::restore_staging_entry(account, backup_id, "previous.")
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

    /// Builds `<the account's backup directory>/<backup id><extension>`.
    ///
    /// One place composes these names, so an artifact and its sidecar cannot
    /// drift apart into two different spellings of one id.
    ///
    /// There is no traversal check here and there is deliberately none: the id
    /// arrives as a [`BackupId`], whose grammar is 36 characters of lowercase
    /// hex and four hyphens, so it cannot hold a `/`, a `..`, a leading `/` or
    /// a NUL — and `Path::join` with an absolute string REPLACES the path it is
    /// joined to. The type answers that question before a path exists at all.
    fn backup_file(account: &AccountName, backup_id: &BackupId, extension: &str) -> PathBuf {
        Self::account_backup_dir(account).join(format!("{}{extension}", backup_id.as_str()))
    }

    /// Builds `<restore staging root>/<account>.<infix><backup id>`.
    ///
    /// Flat, one level under the staging root, and named after both the account
    /// and the backup: the two directories of one swap must be siblings for the
    /// renames to be renames, and two accounts restoring at once must not meet.
    fn restore_staging_entry(account: &AccountName, backup_id: &BackupId, infix: &str) -> PathBuf {
        PathBuf::from(Self::RESTORE_STAGING_ROOT).join(format!(
            "{}.{infix}{}",
            account.as_str(),
            backup_id.as_str()
        ))
    }
}

#[cfg(test)]
#[path = "tests/agent_paths_tests.rs"]
mod tests;
