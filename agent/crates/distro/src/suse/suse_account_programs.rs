//! The programs and spool directories account management uses on the SUSE family.
//!
//! Nothing here was measured by us: every value is read from the distribution's documentation
//! and stays UNVERIFIED until the first SUSE polygon run. The distribution is usr-merged, so a
//! program under `/usr/sbin` also resolves from `/sbin`; the documented `/usr` path is what is
//! answered (the crate's one merged-`/usr` rule, stated on [`crate::DistroAdapter`]). The
//! confirming command for a binary is `rpm -qf <path>` in the pinned openSUSE Leap 16 image,
//! unless a comment names another.

use crate::adapter_binary::AdapterBinary;

/// Absolute path of `useradd`, for the process-execution allow-list.
///
/// An [`AdapterBinary`] and not text: the agent executes it as root, so only this crate may name
/// it. UNVERIFIED: the `shadow` package is read to install the suite in `/usr/sbin`.
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

/// Absolute path of `getent`, for the process-execution allow-list.
///
/// Read-only, and the one instrument that answers what `passwd -S` cannot: `getent shadow <name>`
/// prints the account's RAW shadow password field, which tells a login locked over a real
/// password from one that never had a password. It belongs to the C library (`glibc`), so it
/// cannot be absent from a host that runs anything at all.
#[must_use]
pub fn getent_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/getent")
}

/// Absolute path of `setquota`, for the process-execution allow-list.
///
/// UNVERIFIED: the `quota` package is read to ship its administrative half in `/usr/sbin` and
/// its reporting half in `/usr/bin`; it is not necessarily installed by default.
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

/// Absolute path of `pkill`, for the process-execution allow-list.
///
/// UNVERIFIED: from the `procps` package.
#[must_use]
pub fn pkill_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/pkill")
}

/// Absolute path of `pgrep`, for the process-execution allow-list.
///
/// From the same package as `pkill`, so no extra dependency is declared for it.
#[must_use]
pub fn pgrep_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/pgrep")
}

/// Absolute path of `atq`, for the process-execution allow-list.
///
/// UNVERIFIED: from the `at` package, which is not necessarily installed by default; a host
/// without it has no `at` jobs, and the caller treats the absent binary as "nothing to remove".
#[must_use]
pub fn atq_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/atq")
}

/// Absolute path of `atrm`, for the process-execution allow-list.
///
/// From the `at` package, the same one that ships `atq`, and optional in the same way.
#[must_use]
pub fn atrm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/atrm")
}

/// Absolute path of `loginctl`, for the process-execution allow-list.
///
/// From the `systemd` package, which this agent already requires.
#[must_use]
pub fn loginctl_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/loginctl")
}

/// Absolute path of `ipcrm`, for the process-execution allow-list.
///
/// From `util-linux`, a base package.
#[must_use]
pub fn ipcrm_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/ipcrm")
}

/// Directory the `at` daemon keeps its job files in on this family.
///
/// Present only where the `at` package is installed. UNVERIFIED: this family spools to
/// `/var/spool/atjobs`; confirm with `rpm -ql at | grep spool`.
#[must_use]
pub fn at_spool_directory() -> &'static str {
    "/var/spool/atjobs"
}

/// The real directory per-user mail spool files live in on this family.
///
/// UNVERIFIED: `/var/spool/mail` is read to be the real directory, with `/var/mail` a link to
/// it; confirm with `readlink -f /var/mail` and `ls -ld /var/spool/mail`. The REAL directory is
/// what is answered, so it can be opened without following a link.
#[must_use]
pub fn mail_spool_directory() -> &'static str {
    "/var/spool/mail"
}

/// Absolute path of `passwd`, for the process-execution allow-list.
///
/// UNVERIFIED: read to ship in the `shadow` package on this family, with the rest of the suite
/// and not from a package of its own as on the RHEL family; confirm with `rpm -qf /usr/bin/passwd`.
#[must_use]
pub fn passwd_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/passwd")
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
/// The one program the agent hands a password to; how it is handed over is the calling
/// operation's business, not this file's.
#[must_use]
pub fn chpasswd_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/chpasswd")
}

/// Absolute path of the host's local password database.
///
/// The file the agent reads to find out which SFTP logins an account actually has. The shadow
/// suite's own file, in the location every supported system uses, asked of the adapter all the
/// same: a path this crate does not own is a platform fact wherever the families happen to agree.
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
