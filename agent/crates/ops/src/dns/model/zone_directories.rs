//! The three directories a zone's files can be in.

use std::path::{Path, PathBuf};

use maran_distro::adapter::DistroAdapter;

use crate::dns::model::zone_signing::ZoneSigning;

/// Where zone files and signing keys live: the plain zone directory, the
/// directory of signed zones' files and the key directory.
///
/// A struct and not three parameters because every zone operation now needs all
/// three (a zone moves between the first two when its signing changes, and its
/// key files are in the third), and because the paths are parameters at all only
/// so a test can use directories it can create. [`Self::of`] is the one place a
/// distribution's facts become these paths.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ZoneDirectories {
    /// The plain zone directory: root-owned, read-only to the daemon.
    zones: PathBuf,
    /// The directory of signed zones' files: the daemon writes beside them.
    signed_zones: PathBuf,
    /// The directory of signing keys.
    keys: PathBuf,
}

impl ZoneDirectories {
    /// Wraps the three directories.
    #[must_use]
    pub fn new(zones: PathBuf, signed_zones: PathBuf, keys: PathBuf) -> Self {
        Self {
            zones,
            signed_zones,
            keys,
        }
    }

    /// The directories of `distro`'s family.
    #[must_use]
    pub fn of(distro: &dyn DistroAdapter) -> Self {
        Self::new(
            PathBuf::from(distro.dns_zone_directory()),
            PathBuf::from(distro.dns_signed_zone_directory()),
            PathBuf::from(distro.dns_key_directory()),
        )
    }

    /// The plain zone directory.
    #[must_use]
    pub fn zones(&self) -> &Path {
        &self.zones
    }

    /// The directory of signed zones' files.
    #[must_use]
    pub fn signed_zones(&self) -> &Path {
        &self.signed_zones
    }

    /// The directory of signing keys.
    #[must_use]
    pub fn keys(&self) -> &Path {
        &self.keys
    }

    /// The directory a zone with `signing` has its file in: the signed one for
    /// both signed phases, the plain one otherwise.
    #[must_use]
    pub fn directory_for(&self, signing: &ZoneSigning) -> &Path {
        if signing.key().is_some() {
            &self.signed_zones
        } else {
            &self.zones
        }
    }
}

#[cfg(test)]
#[path = "../../tests/dns/model/zone_directories_tests.rs"]
mod tests;
