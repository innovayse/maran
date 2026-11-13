//! Service and binary names on the SUSE family: web server, PHP, package manager, database,
//! file transfer, backup, systemd, cron and the firewall tool.
//!
//! The account programs, the mail stack and the DNS daemon have their own concern files beside
//! this one. The web server facts (nginx, Apache) were observed in `opensuse/leap:latest`
//! (Leap 16.0) and say so; every other value is read from the distribution's documentation and
//! stays UNVERIFIED until the first SUSE polygon run. The distribution is
//! usr-merged, so a program under `/usr/sbin` also resolves from `/sbin`; the documented
//! `/usr` path is what is answered (the crate's one merged-`/usr` rule, stated on
//! [`crate::DistroAdapter`]). Unless a comment names another, the confirming command for a
//! binary is `rpm -qf <path>` in the pinned openSUSE Leap 16 image.

use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::adapter_binary::AdapterBinary;
use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;

/// Absolute path of the nginx binary, for the process-execution allow-list.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0) with `rpm -ql nginx | grep sbin`:
/// `/usr/sbin/nginx`.
#[must_use]
pub fn nginx_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/nginx")
}

/// Name of the nginx systemd service unit.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0) with `rpm -ql nginx | grep service`:
/// `/usr/lib/systemd/system/nginx.service`.
#[must_use]
pub fn nginx_service() -> &'static str {
    "nginx"
}

/// Absolute path of the service manager binary, for the process-execution allow-list.
///
/// All families are systemd, but the value still comes from the adapter rather than a literal
/// in `ops`: a binary is only spawnable because this trait named it (rules/rust.md "Distro
/// adapter").
#[must_use]
pub fn service_manager() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/systemctl")
}

/// Absolute path of the journal reader, for the process-execution allow-list.
#[must_use]
pub fn journal_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/journalctl")
}

/// Absolute path of the program the agent runs to read the pending package updates, for the
/// process-execution allow-list (issue #324).
///
/// `zypper`, the same file [`crate::PackageFacts::package_manager`] names as plain text for the
/// install unit. It is only ever given the constant arguments of the pending-updates read.
#[must_use]
pub fn package_query_binary() -> AdapterBinary {
    zypper_binary()
}

/// Absolute path of `zypper`, for the process-execution allow-list.
///
/// Named once and returned by [`package_query_binary`] too: the pending-updates read and the
/// reboot probe (`zypper needs-rebooting`, issue #163) are two uses of one program, so the
/// family needs no separate reboot tool.
#[must_use]
pub fn zypper_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/zypper")
}

/// Absolute path of the transient-unit starter, for the process-execution allow-list.
///
/// It starts a customer's cron entry by hand inside the account's slice (issue #158).
#[must_use]
pub fn systemd_run_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/systemd-run")
}

/// The user the web server runs as.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0) with `getent passwd nginx` after installing the
/// package: the package creates the system user `nginx` (its sysusers.d entry).
#[must_use]
pub fn web_server_user() -> &'static str {
    "nginx"
}

/// The group the web server's user belongs to.
///
/// Separate from the user because what an account's home is group-owned by is a GROUP, and
/// naming the user there would be right only by coincidence. Observed in `opensuse/leap:latest`
/// (Leap 16.0) with `getent group nginx` after installing the package: group `nginx` exists.
#[must_use]
pub fn web_server_group() -> &'static str {
    "nginx"
}

/// Absolute path of the program whose `-t` checks the Apache configuration.
///
/// This family's `apache2` package ships NO `apache2ctl` and no `apachectl`: `apachectl` comes only
/// from `apache2-utils`, which a `--no-recommends` install of `apache2` does not bring. The
/// program that is always there is `/usr/sbin/httpd`, an alternatives link to the MPM binary
/// (`httpd-prefork`, package `apache2-prefork`, required by `apache2`); `httpd -t` and `httpd -v`
/// are the two runs the agent makes. Observed in `opensuse/leap:latest` (Leap 16.0): after
/// `zypper install --no-recommends apache2`, `ls /usr/sbin | grep -i "apache\|httpd"` lists
/// `httpd`, `httpd-prefork` and no `apache2ctl`; `httpd -t` prints `Syntax OK` and, with a bad
/// directive planted in `conf.d`, exits 1 naming the line; `httpd -v` prints `Apache/2.4.66`.
#[must_use]
pub fn apache_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/httpd")
}

/// Name of the Apache systemd service unit.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0) with `rpm -ql apache2 | grep service`:
/// `/usr/lib/systemd/system/apache2.service` (aliased `httpd.service`).
#[must_use]
pub fn apache_service() -> &'static str {
    "apache2"
}

/// The user the Apache workers run as.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0): `getent passwd wwwrun` exists after installing
/// `apache2`, and `/etc/apache2/uid.conf` sets `User wwwrun`.
#[must_use]
pub fn apache_user() -> &'static str {
    "wwwrun"
}

/// The group the Apache workers run in, and the one an account's home is group-owned by
/// when Apache is the site web server.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0): `getent group www` exists after installing
/// `apache2` (member `wwwrun`), and `/etc/apache2/uid.conf` sets `Group www`.
#[must_use]
pub fn apache_group() -> &'static str {
    "www"
}

/// Absolute path of the Node.js interpreter of `major` that a site's unit runs: the file
/// [`crate::suse::suse_apps::node_binary`] names.
///
/// Text and not an `AdapterBinary`: the agent never executes it, the service manager does,
/// from a unit the agent renders (the same split as `sh_binary`).
#[must_use]
pub fn node_executable(major: &NodeMajor) -> String {
    crate::suse::suse_apps::node_binary(major)
        .as_str()
        .to_owned()
}

/// Absolute path of the npm launcher of `major` that a site's unit may run.
///
/// Observed in `opensuse/leap:latest` (Leap 16.0): `rpm -ql npm22 | grep bin/` is `/usr/bin/npm22`
/// (and `/usr/bin/npx22`), from the separate `npm22` package, which `nodejs22` does not require
/// (`rpm -q --requires nodejs22` lists no npm): the launcher exists only once `npm<major>` is
/// installed, which is why the app's install installs `npm<major>` after `nodejs<major>`
/// ([`crate::AppFacts::nodejs_extra_packages`]).
///
/// Text for the same reason as [`node_executable`].
#[must_use]
pub fn npm_executable(major: &NodeMajor) -> String {
    format!("/usr/bin/npm{}", major.as_str())
}

/// Name of the php-fpm systemd service unit.
///
/// The `php8-fpm` package registers one `php-fpm` unit for the single PHP minor the family
/// offers (issue #59, owner decision 219), so `_version` does not change the answer.
/// UNVERIFIED: confirm with `rpm -ql php8-fpm | grep service`.
#[must_use]
pub fn php_fpm_service(_version: &str) -> String {
    String::from("php-fpm")
}

/// Absolute path of the php-fpm binary.
///
/// The binary keeps its plain name and the family offers one PHP minor, so the validated
/// `_version` does not change the path. UNVERIFIED: confirm with `rpm -ql php8-fpm | grep sbin`;
/// the polygon asserts the path exists (`crates/agent/tests/php_pools_on_a_real_host.rs`).
#[must_use]
pub fn php_fpm_binary(_version: &PhpVersion) -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/php-fpm")
}

/// Absolute path of the openssl binary, for the process-execution allow-list.
#[must_use]
pub fn openssl_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/openssl")
}

/// Absolute path of `tar`, for the process-execution allow-list.
#[must_use]
pub fn tar_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/tar")
}

/// Absolute path of `gzip`, for the process-execution allow-list.
#[must_use]
pub fn gzip_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/gzip")
}

/// Absolute path of the database dump client, for the process-execution allow-list.
///
/// The real name `mariadb-dump`, never the `mysqldump` compatibility name a future package may
/// drop (see [`BackupFacts::database_dump_binary`](crate::BackupFacts::database_dump_binary)
/// for the `--no-tablespaces` finding). UNVERIFIED: confirm with `rpm -ql mariadb-client | grep dump`.
#[must_use]
pub fn database_dump_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/mariadb-dump")
}

/// Absolute path of the MariaDB client binary, for the process-execution allow-list.
///
/// Every database and database-user operation is a statement handed to this client. The real
/// name `mariadb` is answered, as with the dump client. UNVERIFIED: confirm with
/// `rpm -ql mariadb-client | grep bin/mariadb$`.
#[must_use]
pub fn mysql_client_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/mariadb")
}

/// Name of the database systemd service unit: `mariadb`, because the family ships MariaDB.
///
/// UNVERIFIED: confirm with `rpm -ql mariadb | grep service`.
#[must_use]
pub fn mysql_service() -> &'static str {
    "mariadb"
}

/// Name of the group whose members sshd's `Match Group` block chroots.
///
/// The panel's own group, identical on every family: the installer writes one `Match Group`
/// block, and a family disagreeing here would hand an SFTP user a full session instead of a jail.
#[must_use]
pub fn sftp_group() -> &'static str {
    "maran-sftp"
}

/// Absolute path of the vsftpd binary, for the process-execution allow-list.
///
/// vsftpd is not packaged for Leap 16 (issue #59, owner decision 222): the installer skips
/// FTPS on this family and the answer exists so the trait is complete, naming where the
/// upstream package would put it. UNVERIFIED: confirm with `zypper search vsftpd`, then
/// `rpm -ql vsftpd | grep sbin`, if a repository ever carries it.
#[must_use]
pub fn vsftpd_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/vsftpd")
}

/// Name of the group whose membership authorizes an FTPS login.
///
/// The panel's own group, identical on every family: the authorization is one
/// `pam_succeed_if.so … user ingroup <group>` line in one PAM service file.
#[must_use]
pub fn ftps_group() -> &'static str {
    "maran-ftps"
}

/// The names of the three TLS protocol-version options a rendered `vsftpd.conf` writes.
///
/// All three are fields, including the one the families spell alike: leaving it a literal is
/// the trap, and a wrong spelling makes vsftpd exit 2 printing nothing. UNVERIFIED: upstream
/// vsftpd's underscore spelling (`ssl_tlsv1_1`) is answered, as on the RHEL family; no build
/// of vsftpd exists on this family to read it from. Confirm with
/// `strings /usr/sbin/vsftpd | grep ssl_tlsv` if a package is ever carried.
#[must_use]
pub fn vsftpd_tls_version_keys() -> VsftpdTlsVersionKeys {
    VsftpdTlsVersionKeys {
        tls_v1: "ssl_tlsv1",
        tls_v1_1: "ssl_tlsv1_1",
        tls_v1_2: "ssl_tlsv1_2",
    }
}

/// Absolute path of `ssh-keygen`, for the process-execution allow-list.
#[must_use]
pub fn ssh_keygen_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ssh-keygen")
}

/// Absolute path of the OpenSSH client `ssh`, for the process-execution allow-list
/// (issue #197).
#[must_use]
pub fn ssh_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ssh")
}

/// Absolute path of `ssh-keyscan`, for the process-execution allow-list
/// (issue #197).
#[must_use]
pub fn ssh_keyscan_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ssh-keyscan")
}

/// Directory a systemd unit file must be written to for `systemctl` to see it.
///
/// The administrator's own unit directory, which outranks anything a package ships in
/// `/usr/lib/systemd/system` and is the only one the agent writes to.
#[must_use]
pub fn systemd_unit_directory() -> &'static str {
    "/etc/systemd/system"
}

/// Absolute path of `crontab`, for the process-execution allow-list.
///
/// UNVERIFIED: the `cronie` package is read to install it here; confirm with
/// `rpm -qf /usr/bin/crontab`.
#[must_use]
pub fn crontab_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/crontab")
}

/// Absolute path of the `nft` binary, for the process-execution allow-list.
///
/// UNVERIFIED: confirm with `rpm -ql nftables | grep sbin/nft`.
#[must_use]
pub fn nft_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/nft")
}

/// The files that may define `UID_MIN`, most authoritative first.
///
/// `/etc/login.defs` overrides the vendor copy under `/usr/etc`, which is where this family ships its defaults when `/etc` holds none.
///
/// UNVERIFIED: the `/usr/etc/login.defs` location is taken from the family's
/// usrmerge-era packaging notes and has not been seen on a `leap16` host.
#[must_use]
pub fn login_defs_paths() -> &'static [&'static str] {
    &["/etc/login.defs", "/usr/etc/login.defs"]
}

/// Absolute path of the POSIX shell a crontab line names.
///
/// Here with every other `*_binary` although the agent never spawns it: the agent writes the
/// path into a crontab line and `cron` runs it. `/bin/sh` rather than `/usr/bin/sh`, per the
/// merged-`/usr` rule on [`crate::DistroAdapter`]. UNVERIFIED: this family's `/bin/sh` is read
/// to be `bash`; confirm with `readlink -f /bin/sh`.
#[must_use]
pub fn sh_binary() -> &'static str {
    "/bin/sh"
}

/// Name of the cron systemd service unit.
///
/// UNVERIFIED: the `cronie` package is read to register `cron.service` here (the RHEL family
/// registers `crond`); confirm with `rpm -ql cronie | grep service`.
#[must_use]
pub fn cron_service() -> &'static str {
    "cron"
}

/// Name of the OpenSSH server's systemd service unit.
///
/// UNVERIFIED: confirm with `rpm -ql openssh-server | grep service`.
#[must_use]
pub fn ssh_service() -> &'static str {
    "sshd"
}

/// Absolute path of the OpenSSH server's main configuration file.
///
/// See [`FileTransferFacts::sshd_config_path`](crate::FileTransferFacts::sshd_config_path) for why
/// this is an adapter method. UNVERIFIED: the administrator's file is read to be
/// `/etc/ssh/sshd_config` as everywhere; this family may keep vendor defaults elsewhere
/// (`/usr/etc`), so confirm the file the daemon actually reads with `sshd -T | head -1` and
/// `ls -l /etc/ssh/sshd_config` in the pinned image.
#[must_use]
pub fn sshd_config_path() -> &'static str {
    "/etc/ssh/sshd_config"
}

/// The closed set of units whose state the panel reports, in the order the trait fixes: web
/// server, database, cron, OpenSSH.
///
/// Built from this family's own answers rather than from four fresh literals, so a unit renamed
/// above cannot stay right there and go stale here.
#[must_use]
pub fn managed_units() -> [&'static str; 4] {
    [
        nginx_service(),
        mysql_service(),
        cron_service(),
        ssh_service(),
    ]
}

/// Where this family keeps the host's `machine-id`.
///
/// See [`ServiceManagerFacts::machine_id_path`](crate::ServiceManagerFacts::machine_id_path) for
/// why this is an adapter fact even though the families agree on it.
#[must_use]
pub fn machine_id_path() -> &'static str {
    "/etc/machine-id"
}

/// Absolute path of `timedatectl`, for the process-execution allow-list.
#[must_use]
pub fn timedatectl_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/timedatectl")
}

/// Absolute path of `rsync`, for the process-execution allow-list (issue #197,
/// phase 2).
///
/// Shipped by the `rsync` package, which the installer installs with the OpenSSH
/// client. It is the path in the package's own file list, which is the documented
/// interface the merged-`/usr` rule on `DistroAdapter` says to answer.
#[must_use]
pub fn rsync_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/rsync")
}
