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
}
