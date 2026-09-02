//! Filesystem locations the agent owns outright, identical on every family.

/// Directories the agent creates and writes for itself, outside every
/// account's home and outside every distribution's packaged layout.
///
/// These are agent decisions, not platform facts: both families get the same
/// answer, so they live here once rather than as a method every
/// `DistroAdapter` must repeat with the same literal. A path that does differ
/// between families belongs in `maran-distro`, not here.
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
}
