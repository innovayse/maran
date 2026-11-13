//! The DNS daemon's binaries, identity and directories on the SUSE family (BIND).
//!
//! Provided as best knowledge. Nothing here was measured by us: every value is read from the
//! distribution's documentation and stays UNVERIFIED until the first SUSE polygon run. The
//! installer step checks existence after the install and fails by name. Confirm each binary with
//! `rpm -qf <path>`, and the identity with `getent passwd named` and `getent group named`, in the
//! pinned openSUSE Leap 16 image once the `bind` package is installed.

use crate::adapter_binary::AdapterBinary;

/// Absolute path of `named-checkconf` on the SUSE family.
///
/// Measured in the pinned Leap 16.1 image: `rpm -ql bind` lists it under `/usr/bin`, not
/// `/usr/sbin`.
#[must_use]
pub fn dns_checkconf_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/named-checkconf")
}

/// Absolute path of `named-checkzone` on the SUSE family; the same source and the same
/// measurement status as [`dns_checkconf_binary`].
#[must_use]
pub fn dns_checkzone_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/bin/named-checkzone")
}

/// Absolute path of `rndc` on the SUSE family; the same source and the same measurement status
/// as [`dns_checkconf_binary`].
#[must_use]
pub fn dns_control_binary() -> AdapterBinary {
    AdapterBinary::fixed("/usr/sbin/rndc")
}

/// The system user BIND drops to on the SUSE family: `named`.
#[must_use]
pub fn dns_user() -> &'static str {
    "named"
}

/// The system group BIND runs in on the SUSE family: `named`, the group of the user above.
#[must_use]
pub fn dns_group() -> &'static str {
    "named"
}

/// The daemon's working directory and zone-list directory on the SUSE family.
///
/// A `maran` subdirectory of `/var/lib/named`, the daemon's own working directory here, so the
/// zone-list store and the agent's directories stay apart from the distribution's own `master`,
/// `slave` and `dyn` directories beside it. UNVERIFIED: whether the daemon may WRITE the
/// subdirectory under SELinux is unmeasured; confirm with `ls -Zd /var/lib/named` and an AVC
/// check (`ausearch -m avc -c named`) after the first `rndc addzone`.
#[must_use]
pub fn dns_state_directory() -> &'static str {
    "/var/lib/named/maran"
}

/// The directory zone files are written into on the SUSE family: `zones/` below
/// [`dns_state_directory`].
#[must_use]
pub fn dns_zone_directory() -> &'static str {
    "/var/lib/named/maran/zones"
}

/// The directory the zone files of signed zones are written into on the SUSE family:
/// `signed-zones/` below [`dns_state_directory`].
#[must_use]
pub fn dns_signed_zone_directory() -> &'static str {
    "/var/lib/named/maran/signed-zones"
}

/// The directory a signed zone's key files are written into on the SUSE family: `keys/` below
/// [`dns_state_directory`].
#[must_use]
pub fn dns_key_directory() -> &'static str {
    "/var/lib/named/maran/keys"
}
