//! Service and binary names on the Debian family.

/// Absolute path of the nginx binary, for the process-execution allow-list.
#[must_use]
pub fn nginx_binary() -> &'static str {
    "/usr/sbin/nginx"
}

/// Name of the nginx systemd service unit.
///
/// Debian's nginx package registers the service under this name; the RHEL
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
/// Debian's nginx package creates and runs as `www-data`, the distribution's
/// long-standing shared web user.
#[must_use]
pub fn web_server_user() -> &'static str {
    "www-data"
}

/// The group the web server's user belongs to.
///
/// Separate from the user because the two are only accidentally the same word:
/// what an account's home is group-owned by is a GROUP, and naming the user
/// there would be right by coincidence on both families and wrong the first
/// time one of them changed. Debian's nginx package creates `www-data` as both.
#[must_use]
pub fn web_server_group() -> &'static str {
    "www-data"
}

/// Name of the php-fpm systemd service unit for `version`, e.g. `php8.3-fpm`.
///
/// Sury names the unit after the package it ships, so this must change in
/// lockstep with `debian_packages::php_package`.
#[must_use]
pub fn php_fpm_service(version: &str) -> String {
    format!("php{version}-fpm")
}

/// Absolute path of the php-fpm binary for `version`, e.g.
/// `/usr/sbin/php-fpm8.3`.
///
/// Sury installs a version-suffixed binary so several PHP versions coexist on
/// the same host.
#[must_use]
pub fn php_fpm_binary(version: &str) -> String {
    format!("/usr/sbin/php-fpm{version}")
}

/// Absolute path of the openssl binary, for the process-execution allow-list.
///
/// The certificate operations read a certificate's public key and its expiry
/// with it, and generate the self-signed placeholder a site serves before a
/// real certificate arrives. Debian ships it at `/usr/bin/openssl`.
#[must_use]
pub fn openssl_binary() -> &'static str {
    "/usr/bin/openssl"
}

/// Absolute path of the `mysql` client binary, for the process-execution
/// allow-list.
///
/// Every database and database-user operation is a statement handed to this
/// client. Debian's `mariadb-client` package installs it at `/usr/bin/mysql`,
/// the same path the RHEL family uses — an agreement between these two
/// distributions rather than a rule, and asked of the adapter all the same.
#[must_use]
pub fn mysql_client_binary() -> &'static str {
    "/usr/bin/mysql"
}

/// Name of the database systemd service unit.
///
/// `mariadb`, because the Debian family ships MariaDB rather than MySQL proper.
/// The RHEL family answers the same word for the same reason; were a family to
/// ship MySQL proper, only that family's answer would change.
#[must_use]
pub fn mysql_service() -> &'static str {
    "mariadb"
}

/// Name of the group whose members sshd's `Match Group` block chroots.
///
/// The panel's own group rather than a distribution one, so membership means
/// exactly "this account is an SFTP user" and nothing a package created can
/// drift into it. The RHEL family answers the identical name deliberately: the
/// installer writes one `Match Group` block, and a family disagreeing here
/// would leave that block matching nobody, handing an SFTP user a full session
/// instead of a jail.
#[must_use]
pub fn sftp_group() -> &'static str {
    "maran-sftp"
}

/// Absolute path of `useradd`, for the process-execution allow-list.
///
/// Debian's `passwd` package installs the shadow suite in `/usr/sbin`.
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
/// The one program the agent hands a password to; how it is handed over is
/// the calling operation's business, not this file's.
#[must_use]
pub fn chpasswd_binary() -> &'static str {
    "/usr/sbin/chpasswd"
}

/// Directory a systemd unit file must be written to for `systemctl` to see it.
///
/// The administrator's own unit directory, which outranks anything a package
/// ships in `/usr/lib/systemd/system` and is the only one the agent writes to.
/// The RHEL family answers the same path — an agreement between two systemd
/// distributions, asked of the adapter all the same.
#[must_use]
pub fn systemd_unit_directory() -> &'static str {
    "/etc/systemd/system"
}

/// Absolute path of the host's local password database.
///
/// The shadow suite's own file, in the location every supported system uses.
/// The RHEL family answers the same path — an agreement between two POSIX
/// systems, asked of the adapter all the same, because a path this crate does
/// not own is a platform fact wherever the two happen to agree today.
#[must_use]
pub fn passwd_database() -> &'static str {
    "/etc/passwd"
}

/// Absolute path of `crontab`, for the process-execution allow-list.
///
/// Debian's `cron` package installs it here — verified on the Ubuntu 24.04
/// polygon, where `dpkg -S /usr/bin/crontab` answers `cron`. The RHEL family's
/// `cronie` package chooses the same path, which is an agreement between two
/// packages rather than a rule.
#[must_use]
pub fn crontab_binary() -> &'static str {
    "/usr/bin/crontab"
}

/// Absolute path of the `nft` binary, for the process-execution allow-list.
///
/// Debian's `nftables` package installs it here, and that is the path in the
/// package's own file list — `dpkg -L nftables` names `/usr/sbin/nft`, and
/// `dpkg -S /usr/sbin/nft` answers `nftables`, both verified on the Ubuntu
/// 24.04 polygon; the packaged unit's `ExecStart` names the same path. The file
/// list is the documented interface the rule on [`crate::DistroAdapter`] says to
/// answer, and it is the same evidence the RHEL family cites for the same
/// value.
#[must_use]
pub fn nft_binary() -> &'static str {
    "/usr/sbin/nft"
}

/// Absolute path of the POSIX shell a crontab line names.
///
/// Here with every other `*_binary` rather than in `debian_paths.rs`, even
/// though the agent never spawns it: a reader looking for one asks the file
/// whose subject is binary names, and one exception to that is one more than a
/// reader can be expected to know about.
///
/// The agent writes this path into a crontab line, and `cron` is what runs it.
/// On the Debian family `/bin/sh` is `dash`, not `bash` — verified on the
/// Ubuntu 24.04 polygon, where it is a symlink to `/usr/bin/dash` — so a
/// customer command relying on bash builtins fails here and succeeds on the
/// RHEL family. The path is what the two families agree on; the shell is not.
/// `/bin/sh` rather than the `/usr/bin/sh` that `command -v sh` answers, per
/// the merged-`/usr` rule on [`crate::DistroAdapter`].
#[must_use]
pub fn sh_binary() -> &'static str {
    "/bin/sh"
}

/// Name of the firewall systemd service unit.
///
/// The unit `nftables` ships, which loads
/// [`crate::debian::debian_paths::nftables_include_target`] at boot. The RHEL
/// family registers the same name for the same upstream service.
#[must_use]
pub fn firewall_service() -> &'static str {
    "nftables"
}

/// Name of the cron systemd service unit.
///
/// `cron`, after the package of that name — the RHEL family's `cronie` package
/// registers `crond` instead, and this disagreement is the reason the name is
/// asked of the adapter rather than written where a crontab is installed.
#[must_use]
pub fn cron_service() -> &'static str {
    "cron"
}

/// Name of the OpenSSH server's systemd service unit.
///
/// `ssh` on this family, against `sshd` on the RHEL family. Debian's unit does
/// carry `Alias=sshd.service`, so the other family's name happens to resolve on
/// an enabled host — an alias is not a contract, and the canonical name is what
/// is reported.
///
/// Worth knowing where this answer is USED: on Ubuntu 24.04 the ENABLED unit
/// is `ssh.socket`, and with its `Accept=no` the service it triggers is a
/// single long-lived `sshd -D` that stays active once started. So this unit is
/// inactive from boot until the first connection, not "between logins" — the
/// window closes at the first login and does not reopen. "Is this unit active"
/// is therefore not the same question as "can anyone reach SSH" on this family;
/// the full consequence, and what to check instead, is on
/// [`crate::DistroAdapter::managed_units`].
#[must_use]
pub fn ssh_service() -> &'static str {
    "ssh"
}

/// The closed set of units whose state the panel reports, in the order the
/// trait fixes: web server, database, cron, OpenSSH.
///
/// Built from this family's own answers rather than from four fresh literals,
/// so a unit renamed above cannot stay right there and go stale here.
#[must_use]
pub fn managed_units() -> [&'static str; 4] {
    [
        nginx_service(),
        mysql_service(),
        cron_service(),
        ssh_service(),
    ]
}
