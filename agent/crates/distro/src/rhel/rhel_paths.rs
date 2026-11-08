//! Filesystem locations on the RHEL family.

/// Absolute path of the shell given to an account that must not log in.
///
/// The path RHEL documents. `/usr/sbin/nologin` also resolves on RHEL 8 and
/// later through the merged-`/usr` symlink, but a symlink that happens to
/// exist is not a contract — the documented path is. That is the crate's one
/// merged-`/usr` rule, stated on [`crate::DistroAdapter`] and applied here.
#[must_use]
pub fn nologin_shell() -> &'static str {
    "/sbin/nologin"
}

/// Absolute path of the shell given to an account whose owner may log in.
///
/// RHEL's `bash` package installs it at `/bin/bash` and lists that path in
/// `/etc/shells`; the path resolves through the merged-`/usr` symlink, but the
/// documented one is what is answered (the crate's one merged-`/usr` rule,
/// stated on [`crate::DistroAdapter`]).
#[must_use]
pub fn bash_shell() -> &'static str {
    "/bin/bash"
}

/// Pool directory for a PHP version, e.g. `/etc/opt/remi/php83/php-fpm.d`.
///
/// Remi drops the dot from the version and roots its packages under
/// `/etc/opt/remi`, so neither half of the Debian path survives.
#[must_use]
pub fn php_fpm_pool_directory(version: &str) -> String {
    format!("/etc/opt/remi/php{}/php-fpm.d", version.replace('.', ""))
}

/// The file this family's `nftables.service` reads at boot.
///
/// `ExecStart=/sbin/nft -f /etc/sysconfig/nftables.conf`, so this is where the
/// installer appends the include lines that pull in the agent's rendered
/// ruleset and bans files. `/etc/nftables.conf` — the Debian family's answer —
/// does not exist on this family at all, which is what makes a single literal
/// in `ops` a firewall that never loads on one of the two.
///
/// The file the package ships holds nothing but comments and one commented-out
/// `#include "/etc/nftables/main.nft"` (verified on the AlmaLinux 9 polygon),
/// so an include appended to it has nothing before it that could undo it. The
/// sample rulesets it points at live in `/etc/nftables/`, a directory the
/// package leaves at mode 0700; the agent neither reads nor writes them.
#[must_use]
pub fn nftables_include_target() -> &'static str {
    "/etc/sysconfig/nftables.conf"
}

/// Postfix's configuration directory on the RHEL family.
///
/// See [`MailFacts::postfix_config_directory`](crate::MailFacts::postfix_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn postfix_config_directory() -> &'static str {
    "/etc/postfix"
}

/// Dovecot's configuration directory on the RHEL family.
///
/// See [`MailFacts::dovecot_config_directory`](crate::MailFacts::dovecot_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn dovecot_config_directory() -> &'static str {
    "/etc/dovecot"
}

/// Rspamd's configuration directory on the RHEL family.
///
/// See [`MailFacts::rspamd_config_directory`](crate::MailFacts::rspamd_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn rspamd_config_directory() -> &'static str {
    "/etc/rspamd"
}

/// The ClamAV scanning daemon's configuration file on the RHEL family.
///
/// See [`MailFacts::clamd_config_file`](crate::MailFacts::clamd_config_file) for what the value is for and how far it is verified.
#[must_use]
pub fn clamd_config_file() -> &'static str {
    "/etc/clamd.d/scan.conf"
}
