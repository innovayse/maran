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
