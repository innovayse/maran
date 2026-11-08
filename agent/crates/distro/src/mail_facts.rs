//! The mail stack (Postfix, Dovecot, Rspamd, the key-value store, ClamAV): binaries, units,
//! users, paths, packages and the choices that differ per family.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").
//!
//! # How much of this has been verified
//!
//! Every answer comes from `research_notes/mail-stack-inventory.md`, which was written from
//! package archives and vendor documentation and not from a booted host. A fact the inventory
//! marks `[V]` is stated plainly; one it marks `[K]` (background knowledge, unchecked) or `[?]`
//! carries the word UNVERIFIED in its doc comment. The inventory's section 10 is the polygon task
//! that replaces every UNVERIFIED with a measurement, in the change that ships the mail
//! lifecycle (T2.5).
//!
//! # Not here
//!
//! Locations the agent owns and that are the same everywhere (`/etc/maran/mail`, the account
//! Maildirs) are `AgentPaths` constants. postsrsd is not here either: whether external
//! forwarding is rewritten (SRS) is an open owner decision, and the inventory finds three
//! configuration shapes across the families.

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;

use crate::adapter_binary::AdapterBinary;
use crate::dovecot_major::DovecotMajor;
use crate::key_value_server::KeyValueServer;

/// The mail stack: binaries, units, users, paths, packages and the choices that differ per family.
pub trait MailFacts: Send + Sync {
    /// Absolute path of `postfix`, for the process-execution allow-list.
    ///
    /// Run only as `postfix check` (validating a rendered tree) and never to start or stop the
    /// daemon, which belongs to the service manager. `/usr/sbin/postfix` on both families;
    /// UNVERIFIED.
    fn postfix_binary(&self) -> AdapterBinary;

    /// Absolute path of `postconf`, for the process-execution allow-list.
    ///
    /// Read-only use: `postconf -n` is what the status reports from, because the effective
    /// value is what the daemon obeys and the rendered template is only an intention.
    /// `/usr/sbin/postconf` on both families; UNVERIFIED.
    fn postconf_binary(&self) -> AdapterBinary;

    /// Absolute path of `postmap`, for the process-execution allow-list.
    ///
    /// Builds the indexed form of a rendered lookup table, in the map type
    /// [`Self::postfix_map_type`] names. `/usr/sbin/postmap` on both families; UNVERIFIED.
    fn postmap_binary(&self) -> AdapterBinary;

    /// Absolute path of `postqueue`, for the process-execution allow-list.
    ///
    /// Lists the queue (`-j`) and flushes it (`-f`). `/usr/sbin/postqueue` on both families;
    /// UNVERIFIED.
    fn postqueue_binary(&self) -> AdapterBinary;

    /// Absolute path of `postsuper`, for the process-execution allow-list.
    ///
    /// Deletes one queued message by a validated queue id. `/usr/sbin/postsuper` on both
    /// families; UNVERIFIED.
    fn postsuper_binary(&self) -> AdapterBinary;

    /// Absolute path of `doveadm`, for the process-execution allow-list.
    ///
    /// Quota and mailbox usage reads, and ending a mailbox's live sessions on suspension.
    /// `/usr/bin/doveadm` on both families (not `/usr/sbin`); UNVERIFIED.
    fn doveadm_binary(&self) -> AdapterBinary;

    /// Absolute path of `doveconf`, for the process-execution allow-list.
    ///
    /// `doveconf -n` validates a rendered tree and is what the status reads effective settings
    /// from. `/usr/bin/doveconf` on both families; UNVERIFIED.
    fn doveconf_binary(&self) -> AdapterBinary;

    /// Absolute path of `rspamadm`, for the process-execution allow-list.
    ///
    /// `rspamadm configtest` validates a rendered Rspamd tree. `/usr/bin/rspamadm` on both
    /// families; UNVERIFIED (the RHEL family has no distribution package, so the path is the
    /// vendor package's).
    fn rspamadm_binary(&self) -> AdapterBinary;

    /// Name of the Postfix systemd unit.
    ///
    /// `postfix` on both families. On the Debian family this is a wrapper whose real daemons
    /// are the `postfix@-` instance unit; whether masking the wrapper also holds the instance
    /// back is UNVERIFIED and is a measurement the lifecycle suite owes.
    fn postfix_service(&self) -> &'static str;

    /// Name of the Dovecot systemd unit.
    ///
    /// `dovecot` on both families. The RHEL package also ships `dovecot-init.service` and
    /// `dovecot.socket` (from the package's spec); whether those need masking beside it is
    /// UNVERIFIED.
    fn dovecot_service(&self) -> &'static str;

    /// Name of the Rspamd systemd unit.
    ///
    /// `rspamd` on both families; UNVERIFIED.
    fn rspamd_service(&self) -> &'static str;

    /// Name of the ClamAV scanning daemon's unit.
    ///
    /// `clamav-daemon` on the Debian family, `clamd@scan` on the RHEL family (a template unit
    /// instantiated as `scan`); both UNVERIFIED. Used only when the administrator turns the
    /// antivirus on.
    fn clamd_service(&self) -> &'static str;

    /// Name of the ClamAV signature updater's unit.
    ///
    /// `clamav-freshclam` on both families; UNVERIFIED.
    fn freshclam_service(&self) -> &'static str;

    /// The system user Postfix runs as.
    ///
    /// `postfix` on both families; UNVERIFIED.
    fn postfix_user(&self) -> &'static str;

    /// The group that owns Postfix's drop directory (`postdrop`).
    ///
    /// `postdrop` on both families; UNVERIFIED.
    fn postdrop_group(&self) -> &'static str;

    /// The system user Dovecot's auth and LMTP processes run as.
    ///
    /// `dovecot` on both families; the Debian family does NOT create a `vmail` user (UNVERIFIED),
    /// which does not matter here because mail is delivered under each account's own uid.
    fn dovecot_user(&self) -> &'static str;

    /// The unprivileged user Dovecot's login processes run as.
    ///
    /// `dovenull` on both families; UNVERIFIED.
    fn dovecot_login_user(&self) -> &'static str;

    /// The system user Rspamd runs as.
    ///
    /// `_rspamd` on the Debian family and, UNVERIFIED, on the RHEL family's vendor package.
    fn rspamd_user(&self) -> &'static str;

    /// The system user the ClamAV scanning daemon runs as.
    ///
    /// `clamav` on the Debian family, `clamscan` on the RHEL family; both UNVERIFIED.
    fn clamd_user(&self) -> &'static str;

    /// Absolute path of Postfix's configuration directory.
    ///
    /// `/etc/postfix` on both families; UNVERIFIED.
    fn postfix_config_directory(&self) -> &'static str;

    /// Absolute path of Dovecot's configuration directory.
    ///
    /// `/etc/dovecot` on both families; UNVERIFIED. What is inside it depends on
    /// [`Self::dovecot_major`].
    fn dovecot_config_directory(&self) -> &'static str;

    /// Absolute path of Rspamd's configuration directory.
    ///
    /// `/etc/rspamd` on both families, with overrides in `local.d/` and `override.d/`;
    /// UNVERIFIED.
    fn rspamd_config_directory(&self) -> &'static str;

    /// Absolute path of the ClamAV scanning daemon's configuration file.
    ///
    /// `/etc/clamav/clamd.conf` on the Debian family, `/etc/clamd.d/scan.conf` on the RHEL
    /// family; both UNVERIFIED.
    fn clamd_config_file(&self) -> &'static str;

    /// The packages that install Postfix, Dovecot with IMAP, POP3, LMTP and Sieve, and Rspamd.
    ///
    /// The Debian family splits Dovecot into `dovecot-core`, `dovecot-imapd`, `dovecot-pop3d`,
    /// `dovecot-lmtpd` and `dovecot-sieve`; the RHEL family ships the protocols inside `dovecot`
    /// and Sieve as `dovecot-pigeonhole`. `postfix-lmdb` is on the list on both because the map
    /// type is `lmdb` ([`Self::postfix_map_type`]). The key-value store and ClamAV are NOT in it:
    /// see [`Self::key_value_server`] and [`Self::antivirus_packages`]. Rspamd comes from a
    /// distribution package on the Debian family and, UNVERIFIED and pending the owner's repository
    /// decision, from the vendor repository on the RHEL family where no distribution package
    /// exists; the package NAME is the same either way.
    fn mail_packages(&self) -> &'static [&'static str];

    /// The packages that install ClamAV, installed only when the administrator turns the
    /// antivirus on.
    ///
    /// `clamav`, `clamav-daemon`, `clamav-freshclam` on the Debian family; `clamav`, `clamd`,
    /// `clamav-update` on the RHEL family, where they come from EPEL; both UNVERIFIED.
    fn antivirus_packages(&self) -> &'static [&'static str];

    /// The Postfix lookup-table type every rendered map is written in.
    ///
    /// `lmdb` on both families, because the Berkeley DB backend behind `hash:` is removed from
    /// RHEL 10's Postfix and so is not portable; the map type is chosen once and the templates
    /// receive it as a value. The `postfix-lmdb` package it needs is in
    /// [`Self::mail_packages`]. That `lmdb` is available as a subpackage on every supported
    /// release is UNVERIFIED.
    fn postfix_map_type(&self) -> &'static str;

    /// The password scheme this family's Dovecot can verify and the panel should hash with.
    ///
    /// The Debian family's Dovecot depends on libsodium, so Argon2id is expected to be listed
    /// by `doveadm pw -l` (`research_notes/mail-stack-inventory.md` section 7; not run). The
    /// RHEL family's Dovecot is built WITHOUT libsodium according to its spec, so Argon2id is
    /// expected to be absent and `BLF-CRYPT` is answered instead. That fallback is an ASSUMPTION
    /// pending the owner's ruling on decision D4, because `rules/security.md` item 9 says
    /// Argon2id; the lifecycle suite owes a `doveadm pw -l` check on each family that fails
    /// loudly when the answer here is not listed.
    fn mail_password_scheme(&self) -> MailPasswordScheme;

    /// The Dovecot configuration generation a release ships, from the os-release `VERSION_ID`
    /// (`DistroInfo::version_id`), or `None` for a release this has no answer for.
    ///
    /// A release and not a family fact: Debian 13 ships Dovecot 2.4 while Debian 12 and Ubuntu
    /// ship 2.3. Answered by exact release for the Debian family (`12`, `22.04`, `24.04` are 2.3,
    /// `13` is 2.4) and by the major before the first dot for the RHEL family (`9`, `10` are
    /// 2.3). `None` for anything else, because rendering for a guessed generation writes a file
    /// the daemon refuses. Ubuntu 22.04 is UNVERIFIED (the inventory does not cover it), and a
    /// Debian-family release newer than the ones listed answers `None` until read.
    fn dovecot_major(&self, version_id: &str) -> Option<DovecotMajor>;

    /// The Redis-protocol server a release runs, from the os-release `VERSION_ID`, or `None`
    /// for a release this has no answer for.
    ///
    /// The Debian family ships `redis-server` on every listed release. The RHEL family ships
    /// Redis on 9 and Valkey on 10 (`research_notes/mail-stack-inventory.md` section 1, item 4);
    /// a later 9.x minor may already ship Valkey, which is UNVERIFIED. Redis 8's licence on
    /// Debian 13 is a review item for `maran licenses` and not decided here.
    fn key_value_server(&self, version_id: &str) -> Option<KeyValueServer>;
}

#[cfg(test)]
#[path = "tests/mail_facts_tests.rs"]
mod tests;
