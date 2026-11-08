//! The name of the file a zone is kept in.

use crate::validation::dns::zone_file_name_error::ZoneFileNameError;
use crate::validation::dns::zone_name::ZoneName;

/// The suffix every zone file carries.
const SUFFIX: &str = ".db";

/// `<zone>.db`, derived from a validated [`ZoneName`] and from nothing else.
///
/// A [`ZoneName`] holds only ASCII letters, digits, `-` and `.` and is no longer
/// than 250 characters, so the derived name has no `/`, no `..` component, no
/// leading dot and fits a file name's 255 bytes: a zone name cannot name a file
/// outside the zone directory. The reverse direction ([`Self::parse`], for
/// listing the directory) accepts only the exact spelling the forward direction
/// produces, so a stray file in the directory is skipped and never mistaken for a
/// zone.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ZoneFileName {
    /// The zone this file belongs to.
    zone: ZoneName,
    /// The file name, `<zone>.db`.
    file_name: String,
}

impl ZoneFileName {
    /// The file name of `zone`.
    #[must_use]
    pub fn of(zone: &ZoneName) -> Self {
        Self {
            file_name: format!("{}{SUFFIX}", zone.as_str()),
            zone: zone.clone(),
        }
    }

    /// Reads a zone back out of a directory entry's name.
    ///
    /// # Errors
    ///
    /// - [`ZoneFileNameError::NotAZoneFile`] when `file_name` lacks the suffix or
    ///   its stem is not a valid zone name.
    /// - [`ZoneFileNameError::NotCanonical`] when the stem parses but is not the
    ///   spelling [`Self::of`] would write (uppercase, a trailing dot).
    pub fn parse(file_name: &str) -> Result<Self, ZoneFileNameError> {
        let stem = file_name
            .strip_suffix(SUFFIX)
            .ok_or(ZoneFileNameError::NotAZoneFile)?;
        let zone = ZoneName::parse(stem).map_err(|_| ZoneFileNameError::NotAZoneFile)?;
        let canonical = Self::of(&zone);

        if canonical.file_name != file_name {
            return Err(ZoneFileNameError::NotCanonical);
        }

        Ok(canonical)
    }

    /// The file name, `<zone>.db`.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.file_name
    }

    /// The zone this file belongs to.
    #[must_use]
    pub fn zone(&self) -> &ZoneName {
        &self.zone
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/zone_file_name_tests.rs"]
mod tests;
