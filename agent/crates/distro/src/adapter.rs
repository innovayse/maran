//! The adapter seam: behaviour that differs between distribution families.
//!
//! Operational code never branches on a distribution name; it asks the adapter
//! (rules/architecture.md). [`crate::adapter_for()`] is what chooses
//! the implementation.

use crate::family::DistroFamily;

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
    /// Passwords are set by writing one `user:password` line to this program's
    /// standard input, never by putting the value in its argument vector: a
    /// command line is world-readable through `/proc`, so a password there is a
    /// password every local user on the host can read.
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
}
