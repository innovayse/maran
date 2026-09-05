//! Every absolute binary path the distro adapters declare, checked against the
//! filesystem of a real host of that family.
//!
//! The adapters' `*_binary()` answers are `&'static str` literals. The distro
//! crate's own adapter tests compare them to a second copy of the same strings
//! written into a table whose doc comment calls itself "a record of what was
//! verified on a real Debian host" — but that comparison never touches a
//! filesystem, so it cannot notice a path that is wrong, and it cannot notice a
//! tool the host does not have. It caught nothing when `/usr/bin/quota` existed
//! on neither polygon image, because a string equals a string either way.
//!
//! This suite is the assertion that CAN fail. It asks the adapter the same
//! question the agent asks it — `adapter_for(detect()?.family)`, the production path —
//! and then stats what comes back. A path moved to another directory, a tool
//! dropped from the image, or a family that puts its tools somewhere else all
//! fail here by name, on each family in turn, because the answer is taken from
//! the adapter rather than restated beside it.
//!
//! What it does NOT settle: that each program does what the agent expects of
//! it. `/usr/sbin/setquota` on these images is `docker/polygon/setquota-stand-in.sh`,
//! which accepts everything and does nothing — so what is proven for that one
//! entry is the PATH, not the behaviour, and the stand-in's own comment says so.
//! `/usr/bin/quota` is the real tool from the family's `quota` package.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;
use std::path::Path;

use maran_distro::{DistroAdapter, adapter_for, detect};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// Refuses to go on unless this process is inside a polygon image.
///
/// A panic and not a quiet `return`: this suite is `#[ignore]`d, so the only way
/// to reach it is to ask for it by name, and a skip would then report as a pass
/// — which is the exact failure this suite exists to end (rules/testing.md: "no
/// tests found" is a failure, never a pass).
///
/// # Panics
///
/// Panics when the polygon marker is absent.
fn require_polygon() {
    let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
    assert!(
        !marker.is_empty(),
        "this suite checks the binary paths of a real host of a supported \
         family, and must run inside a polygon container: {POLYGON_MARKER} is \
         not set. See docker/README.md."
    );
}

/// The adapter for the family this container actually is.
///
/// Detection rather than a hard-coded adapter, so the suite exercises the same
/// pairing the agent makes at startup: an image whose `/etc/os-release` and
/// whose tool layout disagreed would be a real defect and is caught here.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// Each declared path, paired with the accessor that produced it.
///
/// The name is carried alongside so a failure says WHICH accessor is wrong
/// rather than only which path is missing; a bare list of strings would leave a
/// reader grepping for the literal.
fn declared_binaries(distro: &'static dyn DistroAdapter) -> [(&'static str, &'static str); 15] {
    [
        ("nginx_binary", distro.nginx_binary()),
        ("openssl_binary", distro.openssl_binary()),
        ("mysql_client_binary", distro.mysql_client_binary()),
        ("useradd_binary", distro.useradd_binary()),
        ("userdel_binary", distro.userdel_binary()),
        ("usermod_binary", distro.usermod_binary()),
        ("setquota_binary", distro.setquota_binary()),
        ("quota_binary", distro.quota_binary()),
        ("id_binary", distro.id_binary()),
        ("chmod_binary", distro.chmod_binary()),
        ("chgrp_binary", distro.chgrp_binary()),
        ("chpasswd_binary", distro.chpasswd_binary()),
        ("crontab_binary", distro.crontab_binary()),
        ("sh_binary", distro.sh_binary()),
        ("nft_binary", distro.nft_binary()),
    ]
}

/// Every path a distro adapter declares names an executable file on this host.
///
/// The agent spawns these by absolute path and nothing else: a path that names
/// nothing is a request that fails at exec time, on a customer's server, in
/// whichever feature reaches it first.
#[test]
#[ignore = "requires a polygon container"]
fn every_declared_binary_path_names_an_executable_file() {
    require_polygon();
    let distro = polygon_distro();

    for (accessor, path) in declared_binaries(distro) {
        let metadata = std::fs::metadata(Path::new(path)).unwrap_or_else(|error| {
            panic!(
                "DistroAdapter::{accessor}() declares {path}, and this host has \
                 nothing there ({error}). The agent execs that exact path: \
                 either the declaration is wrong for this family, or the \
                 polygon image is missing the package that installs it."
            )
        });
        assert!(
            metadata.is_file(),
            "DistroAdapter::{accessor}() declares {path}, which exists but is \
             not a regular file. The agent execs it."
        );
        assert!(
            metadata.permissions().mode() & 0o111 != 0,
            "DistroAdapter::{accessor}() declares {path}, which exists but has \
             no execute bit. The agent execs it."
        );
    }
}
