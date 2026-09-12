//! Service and binary names on the RHEL family.

use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;

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

/// Absolute path of `tar`, for the process-execution allow-list.
///
/// RHEL's `tar` package installs it here — verified `command -v tar` on the
/// AlmaLinux 9 polygon, where it is the real file (`ls -l` shows no symlink),
/// the same path the Debian family uses.
#[must_use]
pub fn tar_binary() -> &'static str {
    "/usr/bin/tar"
}

/// Absolute path of `gzip`, for the process-execution allow-list.
///
/// RHEL's `gzip` package installs it here — verified `command -v gzip` on the
/// AlmaLinux 9 polygon, where it is the real file and not a symlink.
#[must_use]
pub fn gzip_binary() -> &'static str {
    "/usr/bin/gzip"
}

/// Absolute path of the database dump client, for the process-execution
/// allow-list.
///
/// Measured on the AlmaLinux 9 polygon rather than assumed: this family was
/// suspected to ship `mariadb-dump` as the real binary with `mysqldump` as a
/// compatibility symlink, distinct from the Debian family — and the
/// measurement confirms half of that and corrects the other half. RHEL's
/// `mariadb` package (10.5.29) does install the real binary at
/// `/usr/bin/mariadb-dump` and makes `/usr/bin/mysqldump` a symlink to it
/// (`ls -l /usr/bin/mysqldump` shows `-> mariadb-dump`; `rpm -qf` attributes
/// the real file to `mariadb-10.5.29`) — but so does the Debian family's
/// `mariadb-client` package, so the two families agree rather than differ.
/// This answers the real name so a future package that drops the
/// compatibility symlink cannot silently break this family — see
/// [`crate::DistroAdapter::database_dump_binary`] for the full measurement,
/// including the `--no-tablespaces` finding.
#[must_use]
pub fn database_dump_binary() -> &'static str {
    "/usr/bin/mariadb-dump"
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

/// Absolute path of the vsftpd binary, for the process-execution allow-list.
///
/// Measured rather than recalled: RHEL's `vsftpd` package installs the real
/// file here — `rpm -ql vsftpd` lists `/usr/sbin/vsftpd` and `rpm -qf
/// /usr/sbin/vsftpd` answers `vsftpd`, verified in a throwaway `almalinux:9`
/// container (3.0.5-8.el9) and again on `almalinux:10` (3.0.5-12.el10), where
/// `ls -l` shows a regular file and not a symlink. The Debian family installs
/// it at the same path — an agreement between two packages rather than a rule,
/// asked of the adapter all the same.
#[must_use]
pub fn vsftpd_binary() -> &'static str {
    "/usr/sbin/vsftpd"
}

/// Name of the group whose membership authorizes an FTPS login.
///
/// The panel's own group rather than a distribution one, so membership means
/// exactly "this login may transfer files over FTPS". The Debian family answers
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
/// Measured 2026-09-09 on `almalinux:9` (`vsftpd 3.0.5-8.el9`): the binary's
/// own option table, read with `strings`, carries `ssl_tlsv1`, `ssl_tlsv1_1`,
/// `ssl_tlsv1_2` and `ssl_tlsv1_3` — an underscore between the major and the
/// minor, which is where this family parts company with the Debian one.
/// Confirmed by running the daemon: the Debian spelling is reported here as
/// `500 OOPS: unrecognised variable in config file: <key>`.
///
/// The 1.3 key exists on this family too and is deliberately not answered: the
/// template does not write it, because the build's own default enables TLS 1.3
/// and Task 3 observed a TLSv1.3 negotiation on this family with no 1.3 line in
/// the file.
#[must_use]
pub fn vsftpd_tls_version_keys() -> VsftpdTlsVersionKeys {
    VsftpdTlsVersionKeys {
        tls_v1: "ssl_tlsv1",
        tls_v1_1: "ssl_tlsv1_1",
        tls_v1_2: "ssl_tlsv1_2",
    }
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

/// Absolute path of `pkill`, for the process-execution allow-list.
///
/// From the `procps-ng` package — a different package name from the Debian
/// family's `procps` for the same tool, which is the whole reason this is a
/// per-family answer and not a shared constant, even though the two families
/// install it at the same path today.
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

/// Absolute path of `crontab`, for the process-execution allow-list.
///
/// RHEL's `cronie` package installs it here — verified on the AlmaLinux 9
/// polygon, where `rpm -qf /usr/bin/crontab` answers `cronie-1.5.7`. The Debian
/// family's `cron` package chooses the same path, which is an agreement between
/// two packages rather than a rule.
#[must_use]
pub fn crontab_binary() -> &'static str {
    "/usr/bin/crontab"
}

/// Absolute path of the `nft` binary, for the process-execution allow-list.
///
/// RHEL's `nftables` package installs it here, and that is the path in the
/// package's own file list — `rpm -ql nftables` names `/usr/sbin/nft`, and
/// `rpm -qf /usr/sbin/nft` answers `nftables-1.0.9`, both verified on the
/// AlmaLinux 9 polygon. The packaged unit spells the same file `/sbin/nft`,
/// through that directory's merged-`/usr` symlink rather than as a second
/// binary; the file list is the documented interface the rule on
/// [`crate::DistroAdapter`] says to answer, and it is the same evidence the
/// Debian family cites for the same value.
#[must_use]
pub fn nft_binary() -> &'static str {
    "/usr/sbin/nft"
}

/// Absolute path of the POSIX shell a crontab line names.
///
/// Here with every other `*_binary` rather than in `rhel_paths.rs`, even though
/// the agent never spawns it: a reader looking for one asks the file whose
/// subject is binary names, and one exception to that is one more than a reader
/// can be expected to know about.
///
/// The agent writes this path into a crontab line, and `cron` is what runs it.
/// On the RHEL family `/bin/sh` is `bash` — verified on the AlmaLinux 9
/// polygon, where it is a symlink to `/usr/bin/bash` — while the Debian family
/// puts `dash` behind the identical path, so a customer command may behave
/// differently on each without either answer being wrong. `/bin/sh` rather than
/// the `/usr/bin/sh` that `command -v sh` answers, per the merged-`/usr` rule
/// on [`crate::DistroAdapter`].
#[must_use]
pub fn sh_binary() -> &'static str {
    "/bin/sh"
}

/// Name of the cron systemd service unit.
///
/// `crond`, after the daemon `cronie` ships — the Debian family's `cron`
/// package registers `cron` instead, and no alias bridges the two on this
/// family. The disagreement is the reason the name is asked of the adapter
/// rather than written where a crontab is installed.
#[must_use]
pub fn cron_service() -> &'static str {
    "crond"
}

/// Name of the OpenSSH server's systemd service unit.
///
/// `sshd` on this family, against `ssh` on the Debian family, and this family's
/// unit carries no alias for the other name.
#[must_use]
pub fn ssh_service() -> &'static str {
    "sshd"
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
