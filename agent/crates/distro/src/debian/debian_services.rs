//! Service and binary names on the Debian family.

use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;

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

/// Absolute path of `tar`, for the process-execution allow-list.
///
/// Debian's `tar` package installs it here — verified `command -v tar` on the
/// Ubuntu 24.04 polygon, where it is the real file (`ls -l` shows no
/// symlink), not merely a name that happens to resolve.
#[must_use]
pub fn tar_binary() -> &'static str {
    "/usr/bin/tar"
}

/// Absolute path of `gzip`, for the process-execution allow-list.
///
/// Debian's `gzip` package installs it here — verified `command -v gzip` on
/// the Ubuntu 24.04 polygon, where it is the real file and not a symlink.
#[must_use]
pub fn gzip_binary() -> &'static str {
    "/usr/bin/gzip"
}

/// Absolute path of the database dump client, for the process-execution
/// allow-list.
///
/// Measured on the Ubuntu 24.04 polygon rather than assumed: Debian's
/// `mariadb-client` package installs the real binary at
/// `/usr/bin/mariadb-dump` and makes `/usr/bin/mysqldump` a symlink to it
/// (`ls -l /usr/bin/mysqldump` shows `-> mariadb-dump`; `dpkg -S` attributes
/// the real file to `mariadb-client`). This answers the real name so a future
/// package that drops the compatibility symlink cannot silently break this
/// family — see [`crate::DistroAdapter::database_dump_binary`] for the full
/// measurement, including the RHEL family's answer and the `--no-tablespaces`
/// finding.
#[must_use]
pub fn database_dump_binary() -> &'static str {
    "/usr/bin/mariadb-dump"
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

/// Absolute path of the vsftpd binary, for the process-execution allow-list.
///
/// Measured rather than recalled: Debian's `vsftpd` package installs the real
/// file here — `dpkg -L vsftpd` lists `/usr/sbin/vsftpd` and `dpkg -S
/// /usr/sbin/vsftpd` answers `vsftpd`, verified in a throwaway
/// `ubuntu:24.04` container (3.0.5-0ubuntu3.1) and again on `debian:trixie`
/// (3.0.5-0.2), where `ls -l` shows a regular file and not a symlink. The
/// package's file list is the documented interface, per the merged-`/usr` rule
/// on [`crate::DistroAdapter`].
#[must_use]
pub fn vsftpd_binary() -> &'static str {
    "/usr/sbin/vsftpd"
}

/// Name of the group whose membership authorizes an FTPS login.
///
/// The panel's own group rather than a distribution one, so membership means
/// exactly "this login may transfer files over FTPS". The RHEL family answers
/// the identical name deliberately: the authorization is one
/// `pam_succeed_if.so … user ingroup <group>` line in one PAM service file, and
/// a family disagreeing here would leave that line admitting nobody there.
#[must_use]
pub fn ftps_group() -> &'static str {
    "maran-ftps"
}

/// The names of the three TLS protocol-version options a rendered
/// `vsftpd.conf` writes, as this family's build of vsftpd spells them.
///
/// Measured 2026-09-09 on `ubuntu:24.04` (`vsftpd 3.0.5-0ubuntu3.1`): the
/// binary's own option table, read with `strings`, carries `ssl_tlsv1`,
/// `ssl_tlsv11`, `ssl_tlsv12` and `ssl_tlsv13` — no underscore between the
/// major and the minor. Confirmed by running the daemon: a config carrying the
/// RHEL family's spelling makes this build exit 2 printing nothing at all,
/// while the same config with these words starts.
///
/// The 1.3 key exists on this family too and is deliberately not answered: the
/// template does not write it, because the build's own default enables TLS 1.3
/// and Task 3 observed a TLSv1.3 negotiation on this family with no 1.3 line in
/// the file.
#[must_use]
pub fn vsftpd_tls_version_keys() -> VsftpdTlsVersionKeys {
    VsftpdTlsVersionKeys {
        tls_v1: "ssl_tlsv1",
        tls_v1_1: "ssl_tlsv11",
        tls_v1_2: "ssl_tlsv12",
    }
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

/// Absolute path of `pkill`, for the process-execution allow-list.
///
/// From the `procps` package, which is priority *important* on this family and
/// therefore present on an ordinary host — but named here all the same, because
/// "ordinarily present" is not "declared", and a minimal host is exactly where
/// an undeclared tool is discovered by a suspension failing.
#[must_use]
pub fn pkill_binary() -> &'static str {
    "/usr/bin/pkill"
}

/// Absolute path of `passwd`, for the process-execution allow-list.
///
/// The shadow suite installs the user-facing half of its tools in `/usr/bin`
/// and the administrative half in `/usr/sbin`; `passwd` is the user-facing one
/// on both families, because an ordinary user runs it to change their own
/// password. Verified on the polygon by `binary_paths_on_a_real_host`, which
/// stats every path this module declares.
#[must_use]
pub fn passwd_binary() -> &'static str {
    "/usr/bin/passwd"
}

/// Absolute path of `getent`, for the process-execution allow-list.
///
/// Read-only, and the one instrument that answers a question `passwd -S`
/// cannot: `getent shadow <name>` prints the account's RAW shadow password
/// field, which distinguishes a login locked over a real password (`!<hash>`)
/// from one that simply never had a password (`!` here, `!!` on the other
/// family). `passwd -S` collapses both into `L`/`LK`, and that collapse is
/// what made reactivation impossible on the RHEL family.
///
/// It belongs to the C library rather than to any user-management package —
/// `libc-bin` on this family, `glibc-common` on the other — so unlike `passwd`
/// it cannot be absent from a host that runs anything at all.
#[must_use]
pub fn getent_binary() -> &'static str {
    "/usr/bin/getent"
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

/// Absolute path of `quotaon`, for the process-execution allow-list.
pub fn quotaon_binary() -> &'static str {
    "/usr/sbin/quotaon"
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

/// Absolute path of the OpenSSH server's main configuration file.
///
/// The `openssh-server` package installs it here on every Debian-family
/// release this product supports; see
/// [`crate::DistroAdapter::sshd_config_path`] for why this is an adapter
/// method rather than a literal in `ops` even though the RHEL family answers
/// the identical path.
#[must_use]
pub fn sshd_config_path() -> &'static str {
    "/etc/ssh/sshd_config"
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

/// Where debian keeps the host's `machine-id`.
///
/// See [`crate::DistroAdapter::machine_id_path`] for why this is an adapter fact
/// even though both families currently agree on it.
/// @returns The absolute path to the machine-id file.
#[must_use]
pub fn machine_id_path() -> &'static str {
    "/etc/machine-id"
}
