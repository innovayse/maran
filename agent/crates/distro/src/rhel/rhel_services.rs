//! Service and binary names on the RHEL family.

use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::adapter_binary::AdapterBinary;
use crate::rhel::rhel_generation::RhelGeneration;
use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;

/// Absolute path of the nginx binary, for the process-execution allow-list.
#[must_use]
pub fn nginx_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/nginx")
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
pub fn service_manager() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/systemctl")
}

/// Absolute path of the journal reader, for the process-execution allow-list.
///
/// systemd installs `journalctl` at `/usr/bin/journalctl` on both families; it
/// is asked of the adapter all the same, for the reason
/// [`service_manager`] gives.
#[must_use]
pub fn journal_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/journalctl")
}

/// Absolute path of the program the agent runs to read the pending package
/// updates, for the process-execution allow-list (issue #324).
///
/// `dnf` at `/usr/bin/dnf`, the same file
/// [`crate::PackageFacts::package_manager`] names as plain text for the install
/// unit. This is the one package-manager path the agent itself may execute, and
/// it is only ever given the constant arguments of the pending-updates read.
#[must_use]
pub fn package_query_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/dnf")
}

/// Absolute path of the program that reports whether the host needs a reboot,
/// for the process-execution allow-list (issue #163).
///
/// `needs-restarting` at `/usr/bin/needs-restarting`, shipped by `yum-utils`
/// (which `dnf-utils` names as a provide) on AlmaLinux 9, where it was
/// measured. It is only ever given the constant `-r` argument.
#[must_use]
pub fn needs_restarting_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/needs-restarting")
}

/// Absolute path of the transient-unit starter, for the process-execution
/// allow-list.
///
/// systemd installs `systemd-run` at `/usr/bin/systemd-run` on both families,
/// beside `systemctl`; it is asked of the adapter all the same, for the reason
/// [`service_manager`] gives. It starts a customer's cron entry by hand inside
/// the account's slice (issue #158).
#[must_use]
pub fn systemd_run_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/systemd-run")
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

/// Absolute path of the Apache control script, whose `-t` checks the configuration.
///
/// RHEL's `httpd` package ships `apachectl`, runs as `apache` and registers the unit as `httpd`.
/// Observed in `almalinux:9` (9.8) after `dnf install httpd`: `/usr/sbin/apachectl` (package
/// `httpd-core`), `/usr/lib/systemd/system/httpd.service`, `getent passwd apache` and `getent group
/// apache` (uid/gid 48), `User apache` / `Group apache` in `httpd.conf`.
#[must_use]
pub fn apache_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/apachectl")
}

/// Name of the Apache systemd service unit.
///
/// RHEL's `httpd` package ships `apachectl`, runs as `apache` and registers the unit as `httpd`.
/// Observed in `almalinux:9` (9.8) after `dnf install httpd`: `/usr/sbin/apachectl` (package
/// `httpd-core`), `/usr/lib/systemd/system/httpd.service`, `getent passwd apache` and `getent group
/// apache` (uid/gid 48), `User apache` / `Group apache` in `httpd.conf`.
#[must_use]
pub fn apache_service() -> &'static str {
    "httpd"
}

/// The user the Apache workers run as.
///
/// RHEL's `httpd` package ships `apachectl`, runs as `apache` and registers the unit as `httpd`.
/// Observed in `almalinux:9` (9.8) after `dnf install httpd`: `/usr/sbin/apachectl` (package
/// `httpd-core`), `/usr/lib/systemd/system/httpd.service`, `getent passwd apache` and `getent group
/// apache` (uid/gid 48), `User apache` / `Group apache` in `httpd.conf`.
#[must_use]
pub fn apache_user() -> &'static str {
    "apache"
}

/// The group the Apache workers run in, and the one an account's home is group-owned by
/// when Apache is the site web server.
///
/// RHEL's `httpd` package ships `apachectl`, runs as `apache` and registers the unit as `httpd`.
/// Observed in `almalinux:9` (9.8) after `dnf install httpd`: `/usr/sbin/apachectl` (package
/// `httpd-core`), `/usr/lib/systemd/system/httpd.service`, `getent passwd apache` and `getent group
/// apache` (uid/gid 48), `User apache` / `Group apache` in `httpd.conf`.
#[must_use]
pub fn apache_group() -> &'static str {
    "apache"
}

/// Absolute path of the Node.js interpreter of `major` that a site's unit runs: the file
/// [`crate::rhel::rhel_apps::node_binary`] names.
///
/// Text and not an `AdapterBinary`: the agent never executes it, the service manager does,
/// from a unit the agent renders (the same split as `sh_binary`).
#[must_use]
pub fn node_executable(generation: RhelGeneration, major: &NodeMajor) -> String {
    crate::rhel::rhel_apps::node_binary(generation, major)
        .as_str()
        .to_owned()
}

/// Absolute path of the npm launcher that a site's unit may run.
///
/// The 9 generation: observed in `almalinux:9` (9.8) with `rpm -ql npm | grep bin/` after `dnf
/// module install nodejs:20/common`, `/usr/bin/npm` (a link to `npm-cli.js`) from the separate
/// `npm` package, the only npm launcher; a module stream has no per-major file.
///
/// The 10 generation: observed in `almalinux:10` (10.2) with `nodejs`, `nodejs-npm`, `nodejs24`
/// and `nodejs24-npm` installed, each major's npm package ships a launcher of its own,
/// `/usr/bin/npm-22` and `/usr/bin/npm-24` (links into `/usr/lib/node_modules_<major>`), and
/// the unversioned `/usr/bin/npm` is the default major's, so the versioned name is the one that
/// belongs to `major` whatever else is installed
/// (`tests/fixtures/package_manager/el10_nodejs_binaries.txt`).
///
/// Text for the same reason as [`node_executable`].
#[must_use]
pub fn npm_executable(generation: RhelGeneration, major: &NodeMajor) -> String {
    match generation {
        RhelGeneration::El9 => "/usr/bin/npm".to_owned(),
        RhelGeneration::El10 => format!("/usr/bin/npm-{}", major.as_str()),
    }
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
pub fn php_fpm_binary(version: &PhpVersion) -> AdapterBinary {
    AdapterBinary::versioned(format!(
        "/opt/remi/php{version}/root/usr/sbin/php-fpm",
        version = version.as_str().replace('.', "")
    ))
}

/// Absolute path of the openssl binary, for the process-execution allow-list.
///
/// The same path as on the Debian family today, and asked of the adapter all
/// the same: a binary is spawnable because this trait named it, not because
/// the two families happen to agree.
#[must_use]
pub fn openssl_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/openssl")
}

/// Absolute path of `tar`, for the process-execution allow-list.
///
/// RHEL's `tar` package installs it here — verified `command -v tar` on the
/// AlmaLinux 9 polygon, where it is the real file (`ls -l` shows no symlink),
/// the same path the Debian family uses.
#[must_use]
pub fn tar_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/tar")
}

/// Absolute path of `gzip`, for the process-execution allow-list.
///
/// RHEL's `gzip` package installs it here — verified `command -v gzip` on the
/// AlmaLinux 9 polygon, where it is the real file and not a symlink.
#[must_use]
pub fn gzip_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/gzip")
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
/// [`BackupFacts::database_dump_binary`](crate::BackupFacts::database_dump_binary) for the full measurement,
/// including the `--no-tablespaces` finding.
#[must_use]
pub fn database_dump_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/mariadb-dump")
}

/// Absolute path of the `mysql` client binary, for the process-execution
/// allow-list.
///
/// Every database and database-user operation is a statement handed to this
/// client. RHEL's `mariadb` package installs it at `/usr/bin/mysql`, the same
/// path the Debian family uses — an agreement between these two distributions
/// rather than a rule, and asked of the adapter all the same.
#[must_use]
pub fn mysql_client_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/mysql")
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
pub fn vsftpd_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/vsftpd")
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
pub fn useradd_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/useradd")
}

/// Absolute path of `userdel`, for the process-execution allow-list.
#[must_use]
pub fn userdel_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/userdel")
}

/// Absolute path of `usermod`, for the process-execution allow-list.
#[must_use]
pub fn usermod_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/usermod")
}

/// Absolute path of `pkill`, for the process-execution allow-list.
///
/// From the `procps-ng` package — a different package name from the Debian
/// family's `procps` for the same tool, which is the whole reason this is a
/// per-family answer and not a shared constant, even though the two families
/// install it at the same path today.
#[must_use]
pub fn pkill_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/pkill")
}

/// Absolute path of `pgrep`, for the process-execution allow-list.
///
/// From the `procps-ng` package, the same one that ships `pkill`, so no extra
/// dependency is declared for it.
#[must_use]
pub fn pgrep_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/pgrep")
}

/// Absolute path of `atq`, for the process-execution allow-list.
///
/// From the `at` package, which is NOT installed by default on this family: a
/// host without it has no `at` jobs, and the caller treats the absent binary as
/// "nothing to remove". Not verified on every release of the family.
#[must_use]
pub fn atq_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/atq")
}

/// Absolute path of `atrm`, for the process-execution allow-list.
///
/// From the `at` package, the same one that ships `atq`, and optional in the
/// same way.
#[must_use]
pub fn atrm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/atrm")
}

/// Absolute path of `loginctl`, for the process-execution allow-list.
///
/// From the `systemd` package, which this agent already requires (it drives
/// every service through `systemctl`), so no extra dependency is declared.
#[must_use]
pub fn loginctl_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/loginctl")
}

/// Absolute path of `ipcrm`, for the process-execution allow-list.
///
/// From `util-linux`, a base package on this family, so no extra dependency is
/// declared.
#[must_use]
pub fn ipcrm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ipcrm")
}

/// Directory the `at` daemon keeps its job files in on this family.
///
/// Present only where the `at` package is installed.
#[must_use]
pub fn at_spool_directory() -> &'static str {
    "/var/spool/at"
}

/// The real directory per-user mail spool files live in on this family.
///
/// RHEL's is `/var/spool/mail`; the other spelling is a symlink to it.
#[must_use]
pub fn mail_spool_directory() -> &'static str {
    "/var/spool/mail"
}

/// Absolute path of `passwd`, for the process-execution allow-list.
///
/// The shadow suite installs the user-facing half of its tools in `/usr/bin`
/// and the administrative half in `/usr/sbin`; `passwd` is the user-facing one
/// on both families, because an ordinary user runs it to change their own
/// password. Verified on the polygon by `binary_paths_on_a_real_host`, which
/// stats every path this module declares.
#[must_use]
pub fn passwd_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/passwd")
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
pub fn getent_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/getent")
}

/// Absolute path of `setquota`, for the process-execution allow-list.
///
/// The quota tools ship their administrative half in `/usr/sbin`, beside the
/// shadow suite, and their reporting half in `/usr/bin`.
#[must_use]
pub fn setquota_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/setquota")
}

/// Absolute path of `quota`, for the process-execution allow-list.
#[must_use]
pub fn quota_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/quota")
}

/// Absolute path of `quotaon`, for the process-execution allow-list.
#[must_use]
pub fn quotaon_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/quotaon")
}

/// Absolute path of `timedatectl`, for the process-execution allow-list.
/// systemd ships it in `/usr/bin` on both families.
#[must_use]
pub fn timedatectl_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/timedatectl")
}

/// Absolute path of `id`, for the process-execution allow-list.
#[must_use]
pub fn id_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/id")
}

/// Absolute path of `chmod`, for the process-execution allow-list.
#[must_use]
pub fn chmod_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/chmod")
}

/// Absolute path of `chgrp`, for the process-execution allow-list.
#[must_use]
pub fn chgrp_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/chgrp")
}

/// Absolute path of `chpasswd`, for the process-execution allow-list.
///
/// The one program the agent hands a password to; how it is handed over is
/// the calling operation's business, not this file's.
#[must_use]
pub fn chpasswd_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/chpasswd")
}

/// Absolute path of `ssh-keygen`, for the process-execution allow-list.
///
/// Shipped by `openssh`, which the `openssh-server` package installer step 86
/// installs depends on.
pub fn ssh_keygen_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ssh-keygen")
}

/// Absolute path of the OpenSSH client `ssh`, for the process-execution allow-list.
///
/// Shipped by `openssh-clients`, a different package from `openssh` (which ships
/// `ssh-keygen` above) and from `openssh-server`, so the installer installs it as
/// a step of its own. It is the path in the package's own file list, which is the
/// documented interface the merged-`/usr` rule on `DistroAdapter` says to answer
/// (issue #197).
pub fn ssh_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ssh")
}

/// Absolute path of `ssh-keyscan`, for the process-execution allow-list.
///
/// Shipped by `openssh-clients`, with [`ssh_binary`] (issue #197).
pub fn ssh_keyscan_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ssh-keyscan")
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

/// Absolute path of the host's local group database.
///
/// See [`AccountPrograms::group_database`](crate::AccountPrograms::group_database) for what the value is for.
#[must_use]
pub fn group_database() -> &'static str {
    "/etc/group"
}

/// Absolute path of `crontab`, for the process-execution allow-list.
///
/// RHEL's `cronie` package installs it here — verified on the AlmaLinux 9
/// polygon, where `rpm -qf /usr/bin/crontab` answers `cronie-1.5.7`. The Debian
/// family's `cron` package chooses the same path, which is an agreement between
/// two packages rather than a rule.
#[must_use]
pub fn crontab_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/crontab")
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
pub fn nft_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/nft")
}

/// The files that may define `UID_MIN`, most authoritative first.
///
/// `/etc/login.defs` is the shadow-utils file on this family.
#[must_use]
pub fn login_defs_paths() -> &'static [&'static str] {
    &["/etc/login.defs"]
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

/// Absolute path of the OpenSSH server's main configuration file.
///
/// The `openssh-server` package installs it here on every RHEL-family release
/// this product supports — the same path the Debian family answers; see
/// [`FileTransferFacts::sshd_config_path`](crate::FileTransferFacts::sshd_config_path) for why this is still an adapter
/// method rather than a literal in `ops`.
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

/// Where rhel keeps the host's `machine-id`.
///
/// See [`ServiceManagerFacts::machine_id_path`](crate::ServiceManagerFacts::machine_id_path) for why this is an adapter fact
/// even though both families currently agree on it.
/// @returns The absolute path to the machine-id file.
#[must_use]
pub fn machine_id_path() -> &'static str {
    "/etc/machine-id"
}

/// Absolute path of `postfix` on the RHEL family.
///
/// See [`MailFacts::postfix_binary`](crate::MailFacts::postfix_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn postfix_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postfix")
}

/// Absolute path of `postconf` on the RHEL family.
///
/// See [`MailFacts::postconf_binary`](crate::MailFacts::postconf_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn postconf_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postconf")
}

/// Absolute path of `postmap` on the RHEL family.
///
/// See [`MailFacts::postmap_binary`](crate::MailFacts::postmap_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn postmap_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postmap")
}

/// Absolute path of `postqueue` on the RHEL family.
///
/// See [`MailFacts::postqueue_binary`](crate::MailFacts::postqueue_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn postqueue_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postqueue")
}

/// Absolute path of `postsuper` on the RHEL family.
///
/// See [`MailFacts::postsuper_binary`](crate::MailFacts::postsuper_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn postsuper_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postsuper")
}

/// Absolute path of `doveadm` on the RHEL family.
///
/// See [`MailFacts::doveadm_binary`](crate::MailFacts::doveadm_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn doveadm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/doveadm")
}

/// Absolute path of `doveconf` on the RHEL family.
///
/// See [`MailFacts::doveconf_binary`](crate::MailFacts::doveconf_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn doveconf_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/doveconf")
}

/// Absolute path of `rspamadm` on the RHEL family.
///
/// See [`MailFacts::rspamadm_binary`](crate::MailFacts::rspamadm_binary) for what the value is for and how far it is verified.
#[must_use]
pub fn rspamadm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/rspamadm")
}

/// Name of the Postfix unit on the RHEL family.
///
/// See [`MailFacts::postfix_service`](crate::MailFacts::postfix_service) for what the value is for and how far it is verified.
#[must_use]
pub fn postfix_service() -> &'static str {
    "postfix"
}

/// Name of the Dovecot unit on the RHEL family.
///
/// See [`MailFacts::dovecot_service`](crate::MailFacts::dovecot_service) for what the value is for and how far it is verified.
#[must_use]
pub fn dovecot_service() -> &'static str {
    "dovecot"
}

/// Name of the Rspamd unit on the RHEL family.
///
/// See [`MailFacts::rspamd_service`](crate::MailFacts::rspamd_service) for what the value is for and how far it is verified.
#[must_use]
pub fn rspamd_service() -> &'static str {
    "rspamd"
}

/// Name of the ClamAV scanning daemon's unit on the RHEL family.
///
/// See [`MailFacts::clamd_service`](crate::MailFacts::clamd_service) for what the value is for and how far it is verified.
#[must_use]
pub fn clamd_service() -> &'static str {
    "clamd@scan"
}

/// Name of the ClamAV signature updater's unit on the RHEL family.
///
/// See [`MailFacts::freshclam_service`](crate::MailFacts::freshclam_service) for what the value is for and how far it is verified.
#[must_use]
pub fn freshclam_service() -> &'static str {
    "clamav-freshclam"
}

/// The Postfix system user on the RHEL family.
///
/// See [`MailFacts::postfix_user`](crate::MailFacts::postfix_user) for what the value is for and how far it is verified.
#[must_use]
pub fn postfix_user() -> &'static str {
    "postfix"
}

/// The group owning Postfix's drop directory on the RHEL family.
///
/// See [`MailFacts::postdrop_group`](crate::MailFacts::postdrop_group) for what the value is for and how far it is verified.
#[must_use]
pub fn postdrop_group() -> &'static str {
    "postdrop"
}

/// Absolute path of the `postdrop` program.
///
/// See [`MailFacts::postdrop_program`](crate::MailFacts::postdrop_program) for what the value is for and how far it is verified.
#[must_use]
pub fn postdrop_program() -> &'static str {
    "/usr/sbin/postdrop"
}

/// The Dovecot system user on the RHEL family.
///
/// See [`MailFacts::dovecot_user`](crate::MailFacts::dovecot_user) for what the value is for and how far it is verified.
#[must_use]
pub fn dovecot_user() -> &'static str {
    "dovecot"
}

/// The user Dovecot's login processes run as on the RHEL family.
///
/// See [`MailFacts::dovecot_login_user`](crate::MailFacts::dovecot_login_user) for what the value is for and how far it is verified.
#[must_use]
pub fn dovecot_login_user() -> &'static str {
    "dovenull"
}

/// The Rspamd system user on the RHEL family.
///
/// See [`MailFacts::rspamd_user`](crate::MailFacts::rspamd_user) for what the value is for and how far it is verified.
#[must_use]
pub fn rspamd_user() -> &'static str {
    "_rspamd"
}

/// The ClamAV scanning daemon's user on the RHEL family.
///
/// See [`MailFacts::clamd_user`](crate::MailFacts::clamd_user) for what the value is for and how far it is verified.
#[must_use]
pub fn clamd_user() -> &'static str {
    "clamscan"
}

/// Absolute path of `named-checkconf` on the RHEL family.
///
/// Taken from the RHEL packaging spec (research_notes/dns-stack-inventory.md section 2.3). UNMEASURED on a host, and on EL9 the `bind9.18` package's file split is not verified; the installer step checks existence after the install and fails by name.
#[must_use]
pub fn dns_checkconf_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/named-checkconf")
}

/// Absolute path of `named-checkzone` on the RHEL family; the same file-list
/// source and the same measurement status as [`dns_checkconf_binary`].
#[must_use]
pub fn dns_checkzone_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/named-checkzone")
}

/// Absolute path of `rndc` on the RHEL family; the same file-list source and
/// the same measurement status as [`dns_checkconf_binary`].
#[must_use]
pub fn dns_control_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/rndc")
}

/// The system user BIND drops to on the RHEL family: `named`.
#[must_use]
pub fn dns_user() -> &'static str {
    "named"
}

/// The system group BIND runs in on the RHEL family: `named`, the group of the
/// user above.
#[must_use]
pub fn dns_group() -> &'static str {
    "named"
}

/// The daemon's working directory and zone-list directory on the RHEL family.
///
/// Below `/var/named`, which SELinux labels for `named`. Whether `named_t` may WRITE the `maran` subdirectory is UNMEASURED: `installer/lib/89-dns.sh` labels the state directory `named_cache_t` when SELinux tooling is present, and the polygon has not proved it with an AVC check.
#[must_use]
pub fn dns_state_directory() -> &'static str {
    "/var/named/maran"
}

/// The directory zone files are written into on the RHEL family: `zones/`
/// below [`dns_state_directory`].
#[must_use]
pub fn dns_zone_directory() -> &'static str {
    "/var/named/maran/zones"
}

/// The directory the zone files of signed zones are written into on the RHEL family:
/// `signed-zones/` below [`dns_state_directory`].
#[must_use]
pub fn dns_signed_zone_directory() -> &'static str {
    "/var/named/maran/signed-zones"
}

/// The directory a signed zone's key files are written into on the RHEL family:
/// `keys/` below [`dns_state_directory`].
#[must_use]
pub fn dns_key_directory() -> &'static str {
    "/var/named/maran/keys"
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
