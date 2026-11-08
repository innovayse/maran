//! Test support, not a test: a private directory and an identity generated into it.
//!
//! Shared by the tests of `fleet/` because three of them need a real generated identity to read
//! back, and a certificate built by hand in each would be three fixtures that drift.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;
use std::path::Path;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;
use maran_agent_core::validation::fleet::node_address::NodeAddress;

use crate::config::node_identity_request::NodeIdentityRequest;
use crate::fleet::generate_node_identity::generate_node_identity;

/// The moment every generated test certificate is issued: 2027-01-15 08:00:00 UTC.
pub(crate) const FIXED_NOW_SECONDS: u64 = 1_800_000_000;

/// [`FIXED_NOW_SECONDS`] as a clock reading.
pub(crate) fn fixed_now() -> SystemTime {
    UNIX_EPOCH + Duration::from_secs(FIXED_NOW_SECONDS)
}

/// The uid the test process runs as, which is the owner the directories here are checked against.
pub(crate) fn own_uid() -> u32 {
    current_uid().unwrap()
}

/// A fresh temporary directory, owned by this process and set to `0700` explicitly: `tempfile`
/// creates it with the process umask applied to `0777`, so under a group-writable umask (`0002`)
/// it would be `0775` and the very check the tests exercise would refuse it.
pub(crate) fn private_directory() -> tempfile::TempDir {
    let directory = tempfile::tempdir().unwrap();
    std::fs::set_permissions(directory.path(), std::fs::Permissions::from_mode(0o700)).unwrap();
    directory
}

/// A request to generate an identity into `directory` for `address`.
pub(crate) fn request(directory: &Path, address: &str, replace: bool) -> NodeIdentityRequest {
    NodeIdentityRequest {
        directory: directory.to_path_buf(),
        address: NodeAddress::parse(address).unwrap(),
        replace,
    }
}

/// Generates an identity for `node1.example.com` into `directory` and returns its fingerprint.
pub(crate) fn generate_into(directory: &Path) -> CertificateFingerprint {
    generate_node_identity(
        &request(directory, "node1.example.com", false),
        own_uid(),
        fixed_now(),
    )
    .unwrap()
}
