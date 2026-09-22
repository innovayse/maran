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
