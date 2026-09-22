//! The adapter seam: behaviour that differs between distribution families.
//!
//! Operational code never branches on a distribution name; it asks the adapter
//! (rules/architecture.md). [`crate::adapter_for()`] is what chooses
//! the implementation.

use crate::family::DistroFamily;
use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;

/// Behaviour that differs between distribution families.
///
/// Deliberately narrow for now: package installation, service names and firewall
/// specifics are added additively by the plans that first need them, so that each
/// method arrives with a caller rather than as speculation.
///
/// Only facts that DIFFER between families belong here. A location the agent
/// owns itself and that is the same everywhere — its nginx include directory,
/// its certificate directory — is a constant on `maran_agent_core::agent_paths::AgentPaths`,
/// not a method every adapter repeats with the same literal.
///
/// # Which spelling a path answers, on a merged-`/usr` host
///
/// Both supported families merge `/usr`: `/bin` is a symlink to `usr/bin` and
/// `/sbin` to `usr/sbin`, so one file has two absolute names and a method here
/// has to pick one. The rule, and it is the ONLY one this crate uses:
///
/// > answer the name that is a documented interface, not the name that happens
/// > to resolve.
///
/// Concretely — `/bin/sh` because that is the shell path a crontab line and
/// every shebang name; `/sbin/nologin` because that is the path the RHEL family
/// documents; `/usr/sbin/nft` because that is where the `nftables` package
/// installs it on both families. Each is checked against its own authority, not
/// against the other two.
///
/// `command -v` is how an answer is CHECKED on a real host, never how it is
/// chosen. On a merged-`/usr` host it returns whichever spelling `PATH` reaches
/// first, which is a fact about `PATH` ordering rather than about where a file
/// belongs — it answers `/usr/bin/sh` on both polygon images, while the value
/// this crate returns for the same file is, correctly, `/bin/sh`.
pub trait DistroAdapter: Send + Sync {
    /// The family this adapter implements.
    fn family(&self) -> DistroFamily;

    /// Absolute path of the shell given to an account that must not log in.
    ///
    /// A hosting account is not a person with a terminal: SFTP and cron work through
    /// it, and an interactive login is exactly what must not. The path differs between
    /// families — Debian ships it at `/usr/sbin/nologin`, RHEL documents `/sbin/nologin`
    /// — which is why it is asked of the adapter rather than written into an operation
    /// (rules/rust.md "Distro adapter": ops never hard-codes a platform path).
    fn nologin_shell(&self) -> &'static str;

    /// Absolute path of the nginx binary, for the process-execution allow-list.
    ///
    /// Both families install nginx at `/usr/sbin/nginx`, but the value still comes
    /// from the adapter rather than a literal in `ops`: a binary is only spawnable
    /// because this trait named it, not because the path happens to agree today.
    fn nginx_binary(&self) -> &'static str;

    /// Name of the nginx service unit, for `systemctl reload`/`restart`.
    fn nginx_service(&self) -> &'static str;

    /// Absolute path of the service manager binary, for the process-execution
    /// allow-list.
    ///
    /// Every reload the agent performs runs this program. It is asked of the
    /// adapter rather than written where it is used for two reasons: a family
    /// that is not systemd would answer differently, and `ops` must name no
    /// absolute binary path at all — `scripts/lib/check-structure.sh` rejects
    /// one, and a bare `"systemctl"` written to get past that check would be
    /// a program resolved through `PATH` by a root process, which is worse
    /// than the literal it replaced.
    fn service_manager(&self) -> &'static str;

    /// The user the web server runs as, which must own nothing and be able to
    /// read a site's document root.
    ///
    /// `www-data` on the Debian family, `nginx` on the RHEL family. The
    /// document root's group is set to this so the server can read files the
    /// account owns without either of them being able to write the other's.
    fn web_server_user(&self) -> &'static str;

    /// The group an account's home directory is group-owned by, so the web
    /// server can traverse into a document root the account owns.
    ///
    /// `www-data` on the Debian family, `nginx` on the RHEL family — the same
    /// word as the user on both, and asked as its own question all the same:
    /// a group is what a directory is owned by, and the two names agreeing is a
    /// fact about these two distributions rather than a rule.
    fn web_server_group(&self) -> &'static str;

    /// Directory the php-fpm pool files for `version` live in.
    ///
    /// The families disagree twice over: the path shape, and how the version
    /// appears in it — `/etc/php/8.3/fpm/pool.d` against
    /// `/etc/opt/remi/php83/php-fpm.d`, where the dot is dropped.
    fn php_fpm_pool_directory(&self, version: &str) -> String;

    /// Name of the php-fpm service unit for `version`.
    ///
    /// Systemd unit names mirror the package name on each family — `php8.3-fpm`
    /// against `php83-php-fpm` — so this and [`Self::php_package`] must be
    /// changed together or the two silently drift apart.
    fn php_fpm_service(&self, version: &str) -> String;

    /// Absolute path of the php-fpm binary for `version`, for the
    /// process-execution allow-list.
    ///
    /// Sury and Remi both install a version-suffixed binary rather than a single
    /// `php-fpm`, since several PHP versions run side by side on the same host —
    /// `/usr/sbin/php-fpm8.3` against `/usr/sbin/php-fpm83`.
    fn php_fpm_binary(&self, version: &str) -> String;

    /// Name of the OS package that provides php-fpm for `version`.
    ///
    /// Sury's Debian packages keep the dot (`php8.3-fpm`); Remi's RHEL packages
    /// drop it (`php83-php-fpm`), matching the RPM naming convention the rest of
    /// Remi's PHP packages use. Getting this wrong installs nothing and the
    /// version-specific pool directory never appears.
    fn php_package(&self, version: &str) -> String;

    /// Absolute path of the openssl binary, for the process-execution
    /// allow-list.
    ///
    /// The certificate operations run it to read a certificate's public key
    /// and its expiry, and to generate the self-signed placeholder a site
    /// serves before a real certificate arrives. Both families install it at
    /// the same path today; it is still asked of the adapter, because `ops`
    /// names no absolute binary path of its own.
    fn openssl_binary(&self) -> &'static str;

    /// Absolute path of the package manager binary, for the process-execution
    /// allow-list.
    ///
    /// `/usr/bin/apt-get` on the Debian family, `/usr/bin/dnf` on the RHEL
    /// family — the two package repositories the spec fixes (§4) are reached
    /// through these.
    fn package_manager(&self) -> &'static str;

    /// Absolute path of `tar`, for the process-execution allow-list.
    ///
    /// A backup archive is `tar` told which compressor to run, by the absolute
    /// path [`Self::gzip_binary`] answers — never `--gzip`, which makes `tar`
    /// look the compressor up on `PATH`. Both families install `tar` at
    /// `/usr/bin/tar` — verified `command -v tar` on both polygon images, in
    /// both cases the real file and not a symlink.
    fn tar_binary(&self) -> &'static str;

    /// Absolute path of `gzip`, and the value every backup and restore argv
    /// carries as `tar`'s `--use-compress-program`.
    ///
    /// **This value is consumed, not merely recorded.** It was measured on both
    /// polygons and for a while it was named here and used nowhere, while the
    /// argv said `--gzip` — and `--gzip` makes `tar` resolve the compressor
    /// through `PATH` in a child of the root daemon, which was a `PATH`-shadow
    /// hole for as long as the measured fact went unspent (see
    /// `ArchiveSpec::arguments`). A binary this trait names and nothing spawns
    /// is a check that reports on nothing.
    ///
    /// It also lets the backup preflight assert the host has the compressor
    /// before a scheduled backup discovers its absence at 03:00, rather than
    /// after `tar` has already started writing. Both families install it at
    /// `/usr/bin/gzip` — verified `command -v gzip` on both polygon images, in
    /// both cases the real file and not a symlink.
    fn gzip_binary(&self) -> &'static str;

    /// Absolute path of the database dump client, for the process-execution
    /// allow-list.
    ///
    /// **Measured, not assumed** (rules/architecture.md): both families were
    /// suspected to disagree here — historically Debian's `mariadb-client`
    /// shipped a real `mysqldump` while AlmaLinux 9's `mariadb` 10.5 was
    /// expected to ship `mariadb-dump` with `mysqldump` only as a compatibility
    /// symlink. `command -v` for BOTH names, `readlink -f` and `ls -l`,
    /// verified on both polygon images on 2026-09-05, found the
    /// two families agree after all: on Ubuntu 24.04's `mariadb-client` package
    /// AND on AlmaLinux 9's `mariadb` package, `/usr/bin/mariadb-dump` is the
    /// real file and `/usr/bin/mysqldump` is a symlink to it
    /// (`dpkg -S`/`rpm -qf` both attribute the real file to the package). This
    /// method therefore answers the REAL name, `/usr/bin/mariadb-dump`, on
    /// both families — the symlink a future package drop could remove is never
    /// the path this trait hands to a spawn.
    ///
    /// Also measured, because it decides an argv rather than a doc comment:
    /// `--no-tablespaces` is ACCEPTED by the dump client on both families
    /// (confirmed by contrast — an actually-unknown flag fails immediately
    /// with `unknown option`, while `--no-tablespaces` is parsed and the
    /// client proceeds to attempt a connection). Both polygon images run
    /// MariaDB (10.11 on Debian, 10.5.29 on RHEL), not MySQL proper, so the
    /// flag that MySQL 8 requires without `PROCESS` and that MariaDB's own
    /// client was expected to reject outright turned out to be accepted by
    /// both — this crate states the fact and leaves what `ops::backup` does
    /// with it to that area's own argv, per rules/rust.md ("ops never names a
    /// platform literal").
    fn database_dump_binary(&self) -> &'static str;

    /// Absolute path of the `mysql` client binary, for the process-execution
    /// allow-list.
    ///
    /// Every database and database-user operation is a statement handed to this
    /// client; nothing in the agent speaks the wire protocol itself. Both
    /// families install it at `/usr/bin/mysql` today — stated here so a reader
    /// does not wonder whether one family was copied by mistake — and it is
    /// still asked of the adapter, because a binary is spawnable only because
    /// this trait named it, and `ops` names no absolute path of its own.
    fn mysql_client_binary(&self) -> &'static str;

    /// Name of the database service unit, for `systemctl restart`.
    ///
    /// `mariadb` on both families, because both ship MariaDB rather than MySQL
    /// proper. The agreement is a fact about these two distributions, not a rule
    /// the crate relies on: if a family ever ships MySQL proper, this is the one
    /// method whose answer changes, and it changes for that family alone.
    fn mysql_service(&self) -> &'static str;

    /// Name of the group whose members sshd's `Match Group` block chroots.
    ///
    /// SFTP is served by the OpenSSH daemon already running on the host, so an
    /// SFTP user is a system account in this group and nothing else — no FTP
    /// daemon is installed and no FTP binary is named anywhere in this trait.
    ///
    /// The name is the same on both families ON PURPOSE, not by coincidence:
    /// the installer writes ONE `Match Group <group>` block into sshd's
    /// configuration, so a family answering a different name would have that
    /// block match nobody there, and an SFTP user created on it would get a
    /// full session instead of a jail — the opposite of the isolation the block
    /// exists for. The sameness is therefore asserted by a test rather than left
    /// to the two literals happening to agree.
    fn sftp_group(&self) -> &'static str;

    /// Absolute path of the vsftpd binary, for the process-execution
    /// allow-list.
    ///
    /// This is an `argv[0]` the agent is permitted to spawn, and that is the
    /// whole of what this method decides. The daemon itself is started and
    /// stopped by the service manager, never by the agent; what the agent runs
    /// this program for is to ask it about a candidate configuration file
    /// before that file is swapped into place, which is the same
    /// render-validate-swap discipline every other config in this workspace
    /// goes through.
    ///
    /// Both families answer `/usr/sbin/vsftpd`, and the agreement is between
    /// two packages rather than a rule: it is the path in each `vsftpd`
    /// package's own file list — `dpkg -L` on the Debian family, `rpm -ql` on
    /// the RHEL family — which is the documented interface the merged-`/usr`
    /// rule on [`DistroAdapter`] says to answer. It is asked of the adapter all
    /// the same, because a binary is spawnable only because this trait named
    /// it, and `ops` names no absolute path of its own.
    fn vsftpd_binary(&self) -> &'static str;

    /// Name of the group whose membership is the entire authorization to use
    /// FTPS.
    ///
    /// The panel's own group rather than a distribution one, so membership
    /// means exactly "this login may transfer files over FTPS" and nothing a
    /// package created can drift into it. It is a group and not a file of
    /// names because the check that reads it is a PAM line, and PAM asks about
    /// group membership.
    ///
    /// The name is the same on both families ON PURPOSE, for the reason
    /// [`Self::sftp_group`] is: the authorization is one `pam_succeed_if.so …
    /// user ingroup <group>` line in one PAM service file, so a family
    /// answering a different name would leave that line admitting nobody
    /// there. Each family's own adapter test pins the literal, so a drift on
    /// either side is a named failure rather than two values quietly ceasing
    /// to agree.
    fn ftps_group(&self) -> &'static str;

    /// The names of the three TLS protocol-version options a rendered
    /// `vsftpd.conf` writes.
    ///
    /// What the value is FOR: the agent renders one `vsftpd.conf` per host, and
    /// three of its lines are `<key>=YES`/`<key>=NO` where `<key>` is a word
    /// this method supplies verbatim. `maran-templates` receives the three
    /// words as plain strings and never learns which family it is rendering
    /// for, which is what keeps the platform fact inside this crate.
    ///
    /// It is a method and not a constant because the two families' builds of
    /// the same vsftpd version spell the family of options differently:
    /// `ssl_tlsv11`/`ssl_tlsv12` on the Debian family, `ssl_tlsv1_1`/
    /// `ssl_tlsv1_2` on the RHEL family. Each family's spelling is pinned by a
    /// literal in that family's own adapter test, so a copy-paste of one
    /// implementation into the other is a named failure rather than a config
    /// that renders and does not start.
    ///
    /// # What the wrong spelling costs
    ///
    /// The two failures are not equally readable, and the worse one is the
    /// reason this is an adapter method rather than a comment. On the RHEL
    /// family an unknown option is reported: `500 OOPS: unrecognised variable
    /// in config file: <key>`. On the Debian family **vsftpd exits 2 and prints
    /// nothing at all** — a daemon that never starts and never says why, which
    /// gives an operator nothing to read. Both measured 2026-09-09; see
    /// [`crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys`] for the images
    /// and package versions the words themselves came off.
    fn vsftpd_tls_version_keys(&self) -> VsftpdTlsVersionKeys;

    /// Absolute path of `useradd`, for the process-execution allow-list.
    ///
    /// The SFTP area creates a system login with it. Asked of the adapter for
    /// the reason [`Self::service_manager`] is: `ops` names no absolute binary
    /// path of its own, and a bare `"useradd"` written to satisfy that check
    /// would be a program a root daemon resolves through `PATH`, which is worse
    /// than the literal it replaced.
    fn useradd_binary(&self) -> &'static str;

    /// Absolute path of `userdel`, for the process-execution allow-list.
    fn userdel_binary(&self) -> &'static str;

    /// Absolute path of `usermod`, for the process-execution allow-list.
    ///
    /// Suspending and unsuspending an account lock and unlock its password with
    /// this program.
    fn usermod_binary(&self) -> &'static str;

    /// Absolute path of `pkill`, for the process-execution allow-list.
    ///
    /// The login area signals every process running as a suspended account's
    /// uid with it (`pkill --signal KILL --count --uid <uid>`), so that a
    /// suspension ends the transfer sessions the account already has open
    /// rather than only refusing new logins. It is asked of the adapter for the
    /// reason [`Self::useradd_binary`] is, and with one extra edge here: an
    /// unattributed hunk naming this exact path as a literal in `ops` has
    /// already appeared in this tree, and `maran structure` rejects it.
    ///
    /// It comes from `procps` on the Debian family and `procps-ng` on the RHEL
    /// family — two package names for one tool, which is why the path is a
    /// per-family answer even though both families currently agree on it.
    /// `agent/crates/agent/tests/binary_paths_on_a_real_host.rs` stats what this
    /// returns on a host of each family, so a family that stops shipping it
    /// fails by name rather than at exec time on a customer's server.
    fn pkill_binary(&self) -> &'static str;

    /// Absolute path of `passwd`, for the process-execution allow-list.
    ///
    /// Needed for `passwd -S <user>`, which REPORTS whether a password can
    /// authenticate a login. That is the question the suspension attestation
    /// asks, and one letter is the whole answer.
    ///
    /// It is NOT the instrument for the other password question — is there a
    /// hash behind the lock? — because it cannot answer it: a login that never
    /// had a password and one locked over a real one both read `L` here (`LK`
    /// on the RHEL family). `getent_binary` covers that question, one entry at
    /// a time, and neither call opens the shadow database at large
    /// (rules/security.md item 8).
    ///
    /// On the RHEL family this program comes from its OWN package (`passwd`),
    /// separate from the `shadow-utils` package that supplies `useradd`,
    /// `usermod`, `userdel` and `chpasswd`; on the Debian family a single
    /// `passwd` package supplies all five. It is the only tool this adapter
    /// declares with that shape, and a minimal RHEL host without that package
    /// fails this call at exec time — which is why `installer/` names it.
    fn passwd_binary(&self) -> &'static str;

    /// Absolute path of `getent`, for the process-execution allow-list.
    ///
    /// Needed for `getent shadow <user>`, which reports ONE account's raw
    /// shadow password field. The agent asks it exactly one question that
    /// `passwd -S` cannot answer: is there a password hash behind the lock, or
    /// is the login simply passwordless? Both read `L`/`LK` through `passwd
    /// -S`, and `usermod --unlock` refuses the second one — exiting 0 on the
    /// Debian family and 1 on the RHEL family, which is why an account could
    /// never be reactivated there.
    ///
    /// One key, one line: this is not "open the shadow database". The field is
    /// classified where it is read and the bytes are dropped, so no hash
    /// reaches a return value, an error, a log line or the wire
    /// (rules/security.md item 8).
    fn getent_binary(&self) -> &'static str;

    /// Absolute path of `setquota`, for the process-execution allow-list.
    fn setquota_binary(&self) -> &'static str;

    /// Absolute path of `quota`, for the process-execution allow-list.
    ///
    /// Read-only: it reports what an account is using. A host without the quota
    /// tools installed does NOT degrade gracefully — reading usage propagates
    /// the spawn failure and the request fails. That is worth knowing before
    /// shipping to a host that has no `quota` package, and it is stated here
    /// rather than implied, because an earlier version of this comment claimed
    /// the opposite and no test contradicted it.
    fn quota_binary(&self) -> &'static str;

    /// Absolute path of `quotaon`, for the process-execution allow-list.
    ///
    /// Read-only in the sense this crate uses it: only `quotaon -p` is ever
    /// run against it, to ask whether the kernel is tracking quota accounting
    /// on a filesystem right now. Nothing in this crate calls `quotaon`
    /// without `-p` — turning accounting ON is an operator's decision, never
    /// this agent's (rules/architecture.md; the reasoning is repeated at the
    /// call site rather than here, so it is read where the risk is).
    fn quotaon_binary(&self) -> &'static str;

    /// Absolute path of `id`, for the process-execution allow-list.
    ///
    /// The accounts area asks it for a name's numeric uid, so the answer covers
    /// every name source the host is configured with rather than only the local
    /// password file.
    fn id_binary(&self) -> &'static str;

    /// Absolute path of `chmod`, for the process-execution allow-list.
    fn chmod_binary(&self) -> &'static str;

    /// Absolute path of `chgrp`, for the process-execution allow-list.
    fn chgrp_binary(&self) -> &'static str;

    /// Absolute path of `chpasswd`, for the process-execution allow-list.
    ///
    /// How a password reaches it, and why that way, belongs to the operation
    /// that sets one: `ops::sftp`'s `set_sftp_password` carries the reasoning
    /// beside the code that must keep it true.
    fn chpasswd_binary(&self) -> &'static str;

    /// Directory a unit file must be written to for the service manager to
    /// read it.
    ///
    /// The agent installs one bind-mount unit per account with an SFTP user, so
    /// the mount survives a reboot instead of vanishing with the process that
    /// made it. The path is a fact of the service manager rather than a
    /// location the agent owns, which is why it is asked here and is not an
    /// `AgentPaths` constant.
    fn systemd_unit_directory(&self) -> &'static str;

    /// Absolute path of the host's local password database.
    ///
    /// The file the agent reads to find out which SFTP logins an account
    /// actually has, which is what an account deletion has to remove: the
    /// panel's rows say what it remembers creating, not what is on the machine.
    ///
    /// A platform fact rather than a location the agent owns, so it is asked
    /// here and is not an `AgentPaths` constant — the same reasoning as
    /// [`DistroAdapter::systemd_unit_directory`], and the two families agree on
    /// this answer for the same reason: both are POSIX systems using the shadow
    /// suite.
    fn passwd_database(&self) -> &'static str;

    /// Absolute path of `crontab`, for the process-execution allow-list.
    ///
    /// A per-account crontab is installed by running this program, never by
    /// writing the spool directory directly: where that spool lives, what owns
    /// it, what mode it carries and how the daemon learns it changed are the
    /// program's business on each family, and asking it to do the work is what
    /// keeps all four out of `ops`. That is the part this crate knows.
    ///
    /// HOW it is run — what the argument vector is and where the table comes
    /// from — is the caller's contract and is documented there, on
    /// `ops::cron`'s `ProcessCronHost`. This comment used to assert one and was
    /// wrong about it, which is the reason the convention is now stated in one
    /// place instead of two.
    ///
    /// Both families install it at `/usr/bin/crontab` — from `cron` on the
    /// Debian family and from `cronie` on the RHEL family — which is an
    /// agreement between two packages rather than a rule, verified on both
    /// polygon images and asked of the adapter all the same.
    fn crontab_binary(&self) -> &'static str;

    /// Absolute path of the POSIX shell a crontab line names.
    ///
    /// The agent never spawns this and never builds a command line for it:
    /// running a shell is forbidden outright (rules/rust.md "Process
    /// execution"). It is a path WRITTEN into a crontab line, and `cron` is
    /// what runs it. What that line looks like belongs to the unit that
    /// renders it, not here.
    ///
    /// Both families answer `/bin/sh`, and what stands behind that path is NOT
    /// the same program: `dash` on the Debian family, `bash` on the RHEL family
    /// (verified on both polygon images). The path is the contract; the shell
    /// behind it is not, so a customer command written against bash builtins
    /// runs on one family and fails on the other.
    ///
    /// `/bin/sh` and not the `/usr/bin/sh` that `command -v sh` answers on both
    /// images: they are one file, and the rule under [`DistroAdapter`] answers
    /// the spelling a crontab line and a shebang name.
    fn sh_binary(&self) -> &'static str;

    /// Absolute path of the `nft` binary, for the process-execution
    /// allow-list.
    ///
    /// Every firewall operation is a call to this program: the rendered ruleset
    /// is checked with it, loaded with it, and every ban is an element added
    /// through it. Both families install it at `/usr/sbin/nft` — that is the
    /// path in the `nftables` package's own file list on both, which is the
    /// documented interface the rule under [`DistroAdapter`] says to answer.
    /// The RHEL family's unit file spells the same file `/sbin/nft`, through
    /// that directory's merged-`/usr` symlink rather than as a second binary.
    fn nft_binary(&self) -> &'static str;

    /// The file the packaged nftables service reads at boot, and therefore the
    /// one file the installer wires the agent's include lines into.
    ///
    /// Name of the cron service unit.
    ///
    /// The one place the two families' cron packaging shows: `cron` on the
    /// Debian family, `crond` on the RHEL family, the same daemon's job under
    /// two package names. Writing either literal into `ops` would install
    /// crontabs correctly on one family and fail to restart the daemon on the
    /// other.
    fn cron_service(&self) -> &'static str;

    /// Name of the OpenSSH server's service unit.
    ///
    /// `ssh` on the Debian family, `sshd` on the RHEL family. Reported, never
    /// restarted: the agent has no operation that touches the daemon its own
    /// caller may be connected through.
    ///
    /// Reported, but NOT by asking whether this unit is active — see the
    /// socket-activation warning on [`Self::managed_units`] before writing any
    /// code that does. On the Debian family this unit is normally inactive on a
    /// completely healthy host.
    fn ssh_service(&self) -> &'static str;

    /// Absolute path of the OpenSSH server's main configuration file.
    ///
    /// `/etc/ssh/sshd_config` on both supported families — the OpenSSH package
    /// installs it at the identical path everywhere, so this method answers the
    /// same literal on every arm. It is still asked of the adapter rather than
    /// written as a literal in `ops`, for the same reason [`Self::service_manager`]
    /// is: a value this crate names once is a value that cannot drift out of
    /// step between two call sites, and the day a family's packaging DOES
    /// diverge, one arm changes and nothing outside this crate has to know.
    ///
    /// This is the file the installer's `installer/lib/86-sftp.sh` appends its
    /// `Match Group` block to — at the end of the MAIN file, never into a
    /// drop-in — so a reader that wants to find that block reads exactly this
    /// path and no `Include` target.
    fn sshd_config_path(&self) -> &'static str;

    /// Where this family keeps the host's `machine-id`, the value a licence
    /// fingerprint is computed from.
    ///
    /// An adapter fact rather than a literal in `ops` for the reason this whole
    /// trait exists: today both families are systemd and name the same file, so
    /// the two arms are identical — and an identical pair that is NAMED here is
    /// what makes the day they diverge a one-line change instead of a hunt
    /// through `ops`. `/var/lib/dbus/machine-id` is not an alternative on either:
    /// it is a symlink to this path, verified by reading it, so a reader must not
    /// treat the two as separate sources that could disagree.
    fn machine_id_path(&self) -> &'static str;

    /// The closed set of units whose state the panel reports, in this fixed
    /// order: web server, database, cron, OpenSSH.
    ///
    /// Closed, and a fixed-size array rather than a slice, on purpose. Status
    /// reporting never accepts a unit name from a caller, so no rpc can ask
    /// about an arbitrary unit; and growing the set is then a type change every
    /// family must answer rather than a line one family can be left out of.
    ///
    /// Each element is the corresponding accessor's own answer — see
    /// [`Self::nginx_service`], [`Self::mysql_service`], [`Self::cron_service`]
    /// and [`Self::ssh_service`] — so a unit cannot be named correctly in one
    /// place and staler here.
    ///
    /// Two absences are deliberate. php-fpm is not in the set because its unit
    /// name carries a PHP version ([`Self::php_fpm_service`]) and there is no
    /// one unit to name; the firewall is not, because a unit that loads a
    /// ruleset and then `RemainAfterExit`s is not answering the question this
    /// set asks. The firewall unit is not named by this crate at all: the agent
    /// applies rules with [`Self::nft_binary`] and never through systemd, and
    /// the one place a unit name and a boot-time include target ARE needed is
    /// the installer's `installer/lib/87-firewall.sh`, which is shell and reads
    /// nothing from here.
    ///
    /// # `is-active` on the SSH unit is NOT the question "is SSH up"
    ///
    /// Read this before turning this set into statuses. On the Debian family
    /// the enabled unit is `ssh.socket`, not `ssh.service`: on the Ubuntu 24.04
    /// polygon `ssh.socket` is the only entry in `sockets.target.wants/`,
    /// `ssh.service` is absent from `multi-user.target.wants/` entirely, and the
    /// socket declares `Accept=no` with `RequiredBy=ssh.service`. So after a
    /// boot on which nobody has connected yet, `systemctl is-active ssh` —
    /// which resolves to `ssh.service` — reads **inactive** on a host whose SSH
    /// is listening and completely healthy.
    ///
    /// The mechanism decides what the fix has to be, so it is stated exactly.
    /// `Accept=no` means the socket hands its LISTENING file descriptor to one
    /// `ssh.service`, which runs `sshd -D` and stays running; the family ships
    /// no `ssh@.service` template and neither unit sets `StopWhenUnneeded`
    /// (all four measured on the image). The service is therefore inactive from
    /// boot until the FIRST connection triggers it, and active from then on —
    /// the window CLOSES and does not reopen. It is not a per-connection unit
    /// that comes and goes between logins: that shape is `Accept=yes` plus a
    /// per-connection `sshd@.service` running `sshd -i`, which is what the RHEL
    /// family's `sshd.socket` is — and that socket is the one alma9 does NOT
    /// enable.
    ///
    /// A monitor that maps this inactive state to "stopped" therefore invents
    /// an SSH outage on every freshly booted Debian-family host until someone
    /// happens to log in. Because the window is bounded that way, treating a
    /// socket-activated unit's inactive state as "not yet started" — asking the
    /// SOCKET whether it is listening — settles it; polling per connection
    /// would be answering a question this shape never asks. The hazard is also
    /// one-sided: the RHEL family enables `sshd.service` in
    /// `multi-user.target.wants/` and does not enable its `sshd.socket` at all,
    /// so the naive read is right there and wrong here.
    ///
    /// Four `&'static str`s cannot carry "ask this one's socket too", so the
    /// warning is prose until the element type stops being a bare unit name.
    fn managed_units(&self) -> [&'static str; 4];
}
