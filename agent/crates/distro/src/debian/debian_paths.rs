//! Filesystem locations on the Debian family.

/// Absolute path of the shell given to an account that must not log in.
///
/// Debian ships it at `/usr/sbin/nologin`; the RHEL family documents a
/// different path, which is why this is a family fact and not a literal in
/// an operation.
#[must_use]
pub fn nologin_shell() -> &'static str {
    "/usr/sbin/nologin"
}

/// Absolute path of the shell given to an account whose owner may log in.
///
/// Debian's `bash` package installs it at `/bin/bash` and lists that path in
/// `/etc/shells`; the path resolves through the merged-`/usr` symlink, but the
/// documented one is what is answered (the crate's one merged-`/usr` rule,
/// stated on [`crate::DistroAdapter`]).
#[must_use]
pub fn bash_shell() -> &'static str {
    "/bin/bash"
}

/// Pool directory for a PHP version, e.g. `/etc/php/8.3/fpm/pool.d`.
///
/// Sury packages one pool directory per PHP version so several versions run
/// side by side; the version keeps its dot here, unlike on the RHEL family.
#[must_use]
pub fn php_fpm_pool_directory(version: &str) -> String {
    format!("/etc/php/{version}/fpm/pool.d")
}

/// The file this family's `nftables.service` reads at boot.
///
/// `ExecStart=/usr/sbin/nft -f /etc/nftables.conf`, so this is where the
/// installer appends the include lines that pull in the agent's rendered
/// ruleset and bans files.
///
/// The file the Debian package ships begins with a `#!/usr/sbin/nft -f`
/// shebang comment and then `flush ruleset`, and contains no `include` of its
/// own (verified on the Ubuntu 24.04 polygon). Appending at the end therefore
/// loads the agent's tables AFTER that flush, which is the order that makes an
/// apply converge instead of being erased at the next boot.
#[must_use]
pub fn nftables_include_target() -> &'static str {
    "/etc/nftables.conf"
}

/// Absolute path of the file the Debian family's package hooks create when an
/// installed update needs a reboot to take effect (issue #163).
///
/// `/var/run` is a link to `/run`, so the file lives in a directory cleared at
/// boot: its presence means "a reboot has not happened since the package that
/// asked for it", which is exactly the question. The agent only looks at it and
/// never creates or removes it.
#[must_use]
pub fn reboot_required_marker() -> &'static str {
    "/var/run/reboot-required"
}

/// Postfix's configuration directory on the Debian family.
///
/// See [`MailFacts::postfix_config_directory`](crate::MailFacts::postfix_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn postfix_config_directory() -> &'static str {
    "/etc/postfix"
}

/// Dovecot's configuration directory on the Debian family.
///
/// See [`MailFacts::dovecot_config_directory`](crate::MailFacts::dovecot_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn dovecot_config_directory() -> &'static str {
    "/etc/dovecot"
}

/// Rspamd's configuration directory on the Debian family.
///
/// See [`MailFacts::rspamd_config_directory`](crate::MailFacts::rspamd_config_directory) for what the value is for and how far it is verified.
#[must_use]
pub fn rspamd_config_directory() -> &'static str {
    "/etc/rspamd"
}

/// The ClamAV scanning daemon's configuration file on the Debian family.
///
/// See [`MailFacts::clamd_config_file`](crate::MailFacts::clamd_config_file) for what the value is for and how far it is verified.
#[must_use]
pub fn clamd_config_file() -> &'static str {
    "/etc/clamav/clamd.conf"
}
