//! Filesystem locations on the SUSE family.
//!
//! Nothing here was measured by us: every value is read from the distribution's documentation and
//! stays UNVERIFIED until the first SUSE polygon run. Each doc comment says which command confirms
//! its value in the pinned openSUSE Leap 16 image.

use crate::suse::suse_packages;

/// Absolute path of the shell given to an account that must not log in.
///
/// UNVERIFIED: read from the distribution documentation (the `shadow` package installs
/// `nologin`), confirm with `rpm -qf /usr/sbin/nologin` and `grep nologin /etc/shells` in the
/// pinned image. The usr-merged layout makes `/sbin/nologin` resolve too, but the documented
/// path is what is answered (the crate's one merged-`/usr` rule, stated on
/// [`crate::DistroAdapter`]).
#[must_use]
pub fn nologin_shell() -> &'static str {
    "/usr/sbin/nologin"
}

/// Absolute path of the shell given to an account whose owner may log in.
///
/// UNVERIFIED: read from the distribution documentation, confirm with
/// `grep bash /etc/shells` in the pinned image.
#[must_use]
pub fn bash_shell() -> &'static str {
    "/usr/bin/bash"
}

/// Pool directory for a PHP version.
///
/// The distribution's `php8` packages root their configuration at `/etc/php8/fpm`, with the
/// pool files in `php-fpm.d`. The directory carries the PHP major only, and the family offers
/// one minor (issue #59, owner decision 219), so that directory answers for the one version
/// the distribution ships. Every other version is answered with a same-shaped directory no
/// package creates (`/etc/php<version>/fpm/php-fpm.d`): the installed-versions listing decides
/// "installed" by whether this directory exists, and one directory shared by all six supported
/// versions would report all six as installed the moment the one real pool directory appeared.
///
/// UNVERIFIED: read from the distribution documentation, confirm with
/// `rpm -ql php8-fpm | grep php-fpm.d` in the pinned image.
#[must_use]
pub fn php_fpm_pool_directory(version: &str) -> String {
    if version == suse_packages::DISTRIBUTION_PHP_VERSION {
        String::from("/etc/php8/fpm/php-fpm.d")
    } else {
        format!("/etc/php{version}/fpm/php-fpm.d")
    }
}

/// The file this family's `nftables.service` reads at boot, where the installer appends the
/// include lines that pull in the agent's rendered ruleset and bans files.
///
/// UNVERIFIED: the upstream default `/etc/nftables.conf` is answered. openSUSE's
/// `nftables.service` may read another file (the RHEL family reads `/etc/sysconfig/nftables.conf`),
/// and a wrong answer is a firewall that never loads. Confirm with
/// `systemctl cat nftables.service | grep ExecStart` and `rpm -ql nftables | grep conf` in the
/// pinned image; the answer must be the file `ExecStart=` passes to `nft -f`.
#[must_use]
pub fn nftables_include_target() -> &'static str {
    "/etc/nftables.conf"
}

/// Postfix's configuration directory on the SUSE family.
///
/// See [`MailFacts::postfix_config_directory`](crate::MailFacts::postfix_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn postfix_config_directory() -> &'static str {
    "/etc/postfix"
}

/// Dovecot's configuration directory on the SUSE family.
///
/// See [`MailFacts::dovecot_config_directory`](crate::MailFacts::dovecot_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn dovecot_config_directory() -> &'static str {
    "/etc/dovecot"
}

/// Rspamd's configuration directory on the SUSE family.
///
/// See [`MailFacts::rspamd_config_directory`](crate::MailFacts::rspamd_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn rspamd_config_directory() -> &'static str {
    "/etc/rspamd"
}

/// The ClamAV scanning daemon's configuration file on the SUSE family.
///
/// See [`MailFacts::clamd_config_file`](crate::MailFacts::clamd_config_file) for what the value is for and how far it is verified.
/// UNVERIFIED: the `clamav` package is read to ship a single `/etc/clamd.conf`, confirm with
/// `rpm -ql clamav | grep clamd.conf` in the pinned image.
#[must_use]
pub fn clamd_config_file() -> &'static str {
    "/etc/clamd.conf"
}
