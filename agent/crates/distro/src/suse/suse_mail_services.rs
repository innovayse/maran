//! The mail stack's binaries, units, identities and packages on the SUSE family.
//!
//! The mail stack is not offered on this family until measured (issue #59): `dovecot_major` and
//! `key_value_server` answer `None` for every release, and the installer skips the mail step.
//! The answers below exist so the trait is complete. Nothing here was measured by us: every value
//! is read from the distribution's documentation and stays UNVERIFIED until the first SUSE
//! polygon run, and each one is confirmed in the pinned openSUSE Leap 16 image with
//! `rpm -qf <path>` for a binary, `rpm -ql <package> | grep service` for a unit and
//! `getent passwd <user>` or `getent group <group>` for an identity, once the package is
//! installed. For what each value is for and how far it is verified generally, see the
//! matching [`MailFacts`](crate::MailFacts) method.

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;

use crate::adapter_binary::AdapterBinary;
use crate::dovecot_major::DovecotMajor;
use crate::key_value_server::KeyValueServer;

/// Absolute path of `postfix` on the SUSE family.
///
/// See [`MailFacts::postfix_binary`](crate::MailFacts::postfix_binary).
#[must_use]
pub fn postfix_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postfix")
}

/// Absolute path of `postconf` on the SUSE family.
///
/// See [`MailFacts::postconf_binary`](crate::MailFacts::postconf_binary).
#[must_use]
pub fn postconf_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postconf")
}

/// Absolute path of `postmap` on the SUSE family.
///
/// See [`MailFacts::postmap_binary`](crate::MailFacts::postmap_binary).
#[must_use]
pub fn postmap_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postmap")
}

/// Absolute path of `postqueue` on the SUSE family.
///
/// See [`MailFacts::postqueue_binary`](crate::MailFacts::postqueue_binary).
#[must_use]
pub fn postqueue_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postqueue")
}

/// Absolute path of `postsuper` on the SUSE family.
///
/// See [`MailFacts::postsuper_binary`](crate::MailFacts::postsuper_binary).
#[must_use]
pub fn postsuper_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/postsuper")
}

/// Absolute path of `doveadm` on the SUSE family.
///
/// See [`MailFacts::doveadm_binary`](crate::MailFacts::doveadm_binary).
#[must_use]
pub fn doveadm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/doveadm")
}

/// Absolute path of `doveconf` on the SUSE family.
///
/// See [`MailFacts::doveconf_binary`](crate::MailFacts::doveconf_binary).
#[must_use]
pub fn doveconf_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/doveconf")
}

/// Absolute path of `rspamadm` on the SUSE family.
///
/// See [`MailFacts::rspamadm_binary`](crate::MailFacts::rspamadm_binary).
#[must_use]
pub fn rspamadm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/rspamadm")
}

/// Name of the Postfix unit on the SUSE family.
///
/// See [`MailFacts::postfix_service`](crate::MailFacts::postfix_service).
#[must_use]
pub fn postfix_service() -> &'static str {
    "postfix"
}

/// Name of the Dovecot unit on the SUSE family.
///
/// See [`MailFacts::dovecot_service`](crate::MailFacts::dovecot_service).
#[must_use]
pub fn dovecot_service() -> &'static str {
    "dovecot"
}

/// Name of the Rspamd unit on the SUSE family.
///
/// See [`MailFacts::rspamd_service`](crate::MailFacts::rspamd_service).
#[must_use]
pub fn rspamd_service() -> &'static str {
    "rspamd"
}

/// Name of the ClamAV scanning daemon's unit on the SUSE family.
///
/// A plain `clamd`, unlike the RHEL family's instance unit. See
/// [`MailFacts::clamd_service`](crate::MailFacts::clamd_service).
#[must_use]
pub fn clamd_service() -> &'static str {
    "clamd"
}

/// Name of the ClamAV signature updater's unit on the SUSE family.
///
/// See [`MailFacts::freshclam_service`](crate::MailFacts::freshclam_service).
#[must_use]
pub fn freshclam_service() -> &'static str {
    "freshclam"
}

/// The Postfix system user on the SUSE family.
///
/// See [`MailFacts::postfix_user`](crate::MailFacts::postfix_user).
#[must_use]
pub fn postfix_user() -> &'static str {
    "postfix"
}

/// The group owning Postfix's drop directory on the SUSE family: `maildrop`, not the `postdrop`
/// the other families use.
///
/// See [`MailFacts::postdrop_group`](crate::MailFacts::postdrop_group).
#[must_use]
pub fn postdrop_group() -> &'static str {
    "maildrop"
}

/// Absolute path of the `postdrop` program.
///
/// See [`MailFacts::postdrop_program`](crate::MailFacts::postdrop_program) for what the value is for and how far it is verified.
#[must_use]
pub fn postdrop_program() -> &'static str {
    "/usr/sbin/postdrop"
}

/// The Dovecot system user on the SUSE family.
///
/// See [`MailFacts::dovecot_user`](crate::MailFacts::dovecot_user).
#[must_use]
pub fn dovecot_user() -> &'static str {
    "dovecot"
}

/// The user Dovecot's login processes run as on the SUSE family.
///
/// See [`MailFacts::dovecot_login_user`](crate::MailFacts::dovecot_login_user).
#[must_use]
pub fn dovecot_login_user() -> &'static str {
    "dovenull"
}

/// The Rspamd system user on the SUSE family.
///
/// See [`MailFacts::rspamd_user`](crate::MailFacts::rspamd_user).
#[must_use]
pub fn rspamd_user() -> &'static str {
    "_rspamd"
}

/// The ClamAV scanning daemon's user on the SUSE family: `vscan`, the user this distribution's
/// antivirus packages share.
///
/// See [`MailFacts::clamd_user`](crate::MailFacts::clamd_user).
#[must_use]
pub fn clamd_user() -> &'static str {
    "vscan"
}

/// The packages that install the mail stack on the SUSE family.
///
/// See [`MailFacts::mail_packages`](crate::MailFacts::mail_packages) for what the value is for and
/// how far it is verified. UNVERIFIED: these are best-knowledge names, and mail is not offered on
/// this family until they are measured (see [`dovecot_major`]); confirm each with
/// `zypper --non-interactive --no-refresh search --match-exact -t package <name>`.
#[must_use]
pub fn mail_packages() -> &'static [&'static str] {
    &[
        "postfix",
        "postfix-lmdb",
        "dovecot",
        "dovecot-pigeonhole",
        "rspamd",
    ]
}

/// The packages that install ClamAV on the SUSE family.
///
/// See [`MailFacts::antivirus_packages`](crate::MailFacts::antivirus_packages) for what the value
/// is for and how far it is verified. UNVERIFIED: the one `clamav` package is believed to carry
/// the scanning daemon and the signature updater; confirm with
/// `zypper --non-interactive --no-refresh info clamav`.
#[must_use]
pub fn antivirus_packages() -> &'static [&'static str] {
    &["clamav"]
}

/// The Postfix lookup-table type on the SUSE family.
///
/// See [`MailFacts::postfix_map_type`](crate::MailFacts::postfix_map_type) for what the value is
/// for and how far it is verified. UNVERIFIED: that `lmdb` is built into the distribution's
/// Postfix is confirmed with `postconf -m` in the pinned image.
#[must_use]
pub fn postfix_map_type() -> &'static str {
    "lmdb"
}

/// The mailbox password scheme on the SUSE family.
///
/// See [`MailFacts::mail_password_scheme`](crate::MailFacts::mail_password_scheme) for what the
/// value is for and how far it is verified. UNVERIFIED: `BLF-CRYPT` is the conservative choice
/// that needs no optional library; confirm what the packaged Dovecot lists with `doveadm pw -l`.
#[must_use]
pub fn mail_password_scheme() -> MailPasswordScheme {
    MailPasswordScheme::BlowfishCrypt
}

/// The Dovecot generation of a release of the SUSE family: `None` for EVERY release.
///
/// See [`MailFacts::dovecot_major`](crate::MailFacts::dovecot_major) for what the value is for.
/// The mail stack is not offered on this family until its packages and configuration generation
/// are measured in the pinned image, and rendering for a guessed generation writes a file the
/// daemon refuses, so no release is claimed (the mail stack refuses what it cannot lay out).
/// Confirm, when the stack is measured, with `doveconf --version` in the pinned Leap image.
#[must_use]
pub fn dovecot_major(_version_id: &str) -> Option<DovecotMajor> {
    None
}

/// The Redis-protocol server of a release of the SUSE family: `None` for EVERY release.
///
/// See [`MailFacts::key_value_server`](crate::MailFacts::key_value_server) for what the value is
/// for. Not offered until measured, for the reason given at [`dovecot_major`]. Confirm, when the
/// stack is measured, with `zypper --non-interactive --no-refresh search -t package valkey redis`
/// in the pinned Leap image.
#[must_use]
pub fn key_value_server(_version_id: &str) -> Option<KeyValueServer> {
    None
}

#[cfg(test)]
#[path = "../tests/suse/suse_mail_services_tests.rs"]
mod tests;
