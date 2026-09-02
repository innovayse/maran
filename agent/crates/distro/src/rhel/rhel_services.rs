//! Service and binary names on the RHEL family.

/// Absolute path of the nginx binary, for the process-execution allow-list.
#[must_use]
pub fn nginx_binary() -> &'static str {
    "/usr/sbin/nginx"
}

/// Name of the nginx systemd service unit.
///
/// RHEL's nginx package registers the service under this name; the Debian
/// family happens to agree, but that is a coincidence worth stating rather
/// than a shared rule this crate relies on.
#[must_use]
pub fn nginx_service() -> &'static str {
    "nginx"
}

/// Absolute path of the service manager binary, for the process-execution
/// allow-list.
///
/// Both families are systemd and both ship it at `/usr/bin/systemctl`, but the
/// value still comes from the adapter rather than a literal in `ops`: a binary
/// is only spawnable because this trait named it, and `ops` naming an absolute
/// path is the violation whether or not the path is currently right
/// (rules/rust.md "Distro adapter").
#[must_use]
pub fn service_manager() -> &'static str {
    "/usr/bin/systemctl"
}

/// The user the web server runs as.
///
/// RHEL's nginx package creates and runs as `nginx`, not `www-data`.
#[must_use]
pub fn web_server_user() -> &'static str {
    "nginx"
}

/// The group the web server's user belongs to.
///
/// Separate from the user because the two are only accidentally the same word:
/// what an account's home is group-owned by is a GROUP, and naming the user
/// there would be right by coincidence on both families and wrong the first
/// time one of them changed. RHEL's nginx package creates `nginx` as both.
#[must_use]
pub fn web_server_group() -> &'static str {
    "nginx"
}

/// Name of the php-fpm systemd service unit for `version`, e.g.
/// `php83-php-fpm`.
///
/// Remi names the unit after the package it ships, so this must change in
/// lockstep with `rhel_packages::php_package`.
#[must_use]
pub fn php_fpm_service(version: &str) -> String {
    format!("php{}-php-fpm", version.replace('.', ""))
}

/// Absolute path of the php-fpm binary for `version`, e.g.
/// `/opt/remi/php83/root/usr/sbin/php-fpm`.
///
/// Remi ships each version as a software collection rooted at
/// `/opt/remi/php<version>/root`, so the binary keeps its plain name and the
/// VERSION is in the directory — the mirror image of the Debian family, where
/// the name carries the version and the directory does not. It is not
/// `/usr/sbin/php-fpm83`: no such file exists on a Remi host, and a pool write
/// pointed at it fails to spawn its own validator, so every PHP pool on the
/// whole family would fail. The polygon asserts this path exists
/// (`crates/agent/tests/php_pools_on_a_real_host.rs`), because a path that is
/// merely plausible is how this was wrong in the first place.
#[must_use]
pub fn php_fpm_binary(version: &str) -> String {
    format!(
        "/opt/remi/php{version}/root/usr/sbin/php-fpm",
        version = version.replace('.', "")
    )
}

/// Absolute path of the openssl binary, for the process-execution allow-list.
///
/// The same path as on the Debian family today, and asked of the adapter all
/// the same: a binary is spawnable because this trait named it, not because
/// the two families happen to agree.
#[must_use]
pub fn openssl_binary() -> &'static str {
    "/usr/bin/openssl"
}

/// Absolute path of the `mysql` client binary, for the process-execution
/// allow-list.
///
/// Every database and database-user operation is a statement handed to this
/// client. RHEL's `mariadb` package installs it at `/usr/bin/mysql`, the same
/// path the Debian family uses — an agreement between these two distributions
/// rather than a rule, and asked of the adapter all the same.
#[must_use]
pub fn mysql_client_binary() -> &'static str {
    "/usr/bin/mysql"
}

/// Name of the database systemd service unit.
///
/// `mariadb`, because the RHEL family ships MariaDB rather than MySQL proper.
/// The Debian family answers the same word for the same reason; were a family
/// to ship MySQL proper, only that family's answer would change.
#[must_use]
pub fn mysql_service() -> &'static str {
    "mariadb"
}

/// Name of the group whose members sshd's `Match Group` block chroots.
///
/// The panel's own group rather than a distribution one, so membership means
/// exactly "this account is an SFTP user" and nothing a package created can
/// drift into it. The Debian family answers the identical name deliberately:
/// the installer writes one `Match Group` block, and a family disagreeing here
/// would leave that block matching nobody, handing an SFTP user a full session
/// instead of a jail.
#[must_use]
pub fn sftp_group() -> &'static str {
    "maran-sftp"
}

/// Absolute path of `useradd`, for the process-execution allow-list.
///
/// RHEL's `shadow-utils` package installs the suite in `/usr/sbin`, the same
/// path the Debian family uses.
#[must_use]
pub fn useradd_binary() -> &'static str {
    "/usr/sbin/useradd"
}

/// Absolute path of `userdel`, for the process-execution allow-list.
#[must_use]
pub fn userdel_binary() -> &'static str {
    "/usr/sbin/userdel"
}

/// Absolute path of `usermod`, for the process-execution allow-list.
#[must_use]
pub fn usermod_binary() -> &'static str {
    "/usr/sbin/usermod"
}

/// Absolute path of `setquota`, for the process-execution allow-list.
///
/// The quota tools ship their administrative half in `/usr/sbin`, beside the
/// shadow suite, and their reporting half in `/usr/bin`.
#[must_use]
pub fn setquota_binary() -> &'static str {
    "/usr/sbin/setquota"
}

/// Absolute path of `quota`, for the process-execution allow-list.
#[must_use]
pub fn quota_binary() -> &'static str {
    "/usr/bin/quota"
}

/// Absolute path of `id`, for the process-execution allow-list.
#[must_use]
pub fn id_binary() -> &'static str {
    "/usr/bin/id"
}

/// Absolute path of `chmod`, for the process-execution allow-list.
#[must_use]
pub fn chmod_binary() -> &'static str {
    "/usr/bin/chmod"
}

/// Absolute path of `chgrp`, for the process-execution allow-list.
#[must_use]
pub fn chgrp_binary() -> &'static str {
    "/usr/bin/chgrp"
}

/// Absolute path of `chpasswd`, for the process-execution allow-list.
///
/// The one program the agent hands a password to, and it reads it from
/// standard input rather than from its arguments.
#[must_use]
pub fn chpasswd_binary() -> &'static str {
    "/usr/sbin/chpasswd"
}

/// Directory a systemd unit file must be written to for `systemctl` to see it.
///
/// The administrator's own unit directory, which outranks anything a package
/// ships in `/usr/lib/systemd/system` and is the only one the agent writes to.
/// The Debian family answers the same path — an agreement between two systemd
/// distributions, asked of the adapter all the same.
#[must_use]
pub fn systemd_unit_directory() -> &'static str {
    "/etc/systemd/system"
}

/// Absolute path of the host's local password database.
///
/// The shadow suite's own file, in the location every supported system uses.
/// The Debian family answers the same path — an agreement between two POSIX
/// systems, asked of the adapter all the same, because a path this crate does
/// not own is a platform fact wherever the two happen to agree today.
#[must_use]
pub fn passwd_database() -> &'static str {
    "/etc/passwd"
}
