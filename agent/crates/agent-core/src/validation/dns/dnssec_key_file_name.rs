//! The names of the files a zone's signing key is kept in.

use crate::validation::dns::dnssec_key_file_name_error::DnssecKeyFileNameError;
use crate::validation::dns::dnssec_key_material::DnssecKeyMaterial;
use crate::validation::dns::zone_name::ZoneName;

/// What a file name may be at most, in bytes.
const MAX_FILE_NAME: usize = 255;

/// The longest suffix a key file carries, `.private`.
const LONGEST_SUFFIX: &str = ".private";

/// The suffix of the public key file.
const PUBLIC_SUFFIX: &str = ".key";

/// The suffix of the private key file.
const PRIVATE_SUFFIX: &str = ".private";

/// The suffix of the file the daemon keeps a key's timing state in.
const STATE_SUFFIX: &str = ".state";

/// The text between the zone and the tag: a dot, the `+` the tools write, the
/// algorithm as three digits (13 is `013`) and a `+`.
const ALGORITHM_PART: &str = ".+013+";

/// `K<zone>.+013+<tag>`, the base name of a key's files, built from a validated
/// [`ZoneName`] and the RECOMPUTED key tag of a [`DnssecKeyMaterial`] and from
/// nothing else.
///
/// The tag is written as five digits, zero-padded, as the daemon's own tools do.
/// A zone name holds only ASCII letters, digits, `-` and `.`, so the base name has
/// no `/`, no `..` component and no leading dot; the one remaining way a name can
/// be unusable is its length, which [`Self::of`] refuses. The reverse direction
/// ([`Self::parse`], for reading a keys directory back) accepts only the exact
/// spelling the forward direction writes, so a stray file in the directory is
/// skipped and never mistaken for a key.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DnssecKeyFileName {
    /// The zone the key signs.
    zone: ZoneName,
    /// The key tag in the name.
    key_tag: u16,
    /// The base name, `K<zone>.+013+<tag>`.
    base: String,
}

impl DnssecKeyFileName {
    /// The base name of `key` for `zone`.
    ///
    /// # Errors
    ///
    /// Returns [`DnssecKeyFileNameError::TooLong`] when the longest of the key's
    /// files would exceed a file name's 255 bytes.
    pub fn of(zone: &ZoneName, key: &DnssecKeyMaterial) -> Result<Self, DnssecKeyFileNameError> {
        Self::from_parts(zone, key.key_tag())
    }

    /// Reads a key back out of a directory entry's name.
    ///
    /// # Errors
    ///
    /// Returns [`DnssecKeyFileNameError::NotAKeyFile`] when `file_name` does not
    /// end in `.key`, `.private` or `.state`, when what is before it is not
    /// `K<zone>.+013+<five digit tag>`, or when it is not the spelling
    /// [`Self::of`] writes. A name too long to have been written is the same
    /// refusal, since this crate never wrote it.
    pub fn parse(file_name: &str) -> Result<Self, DnssecKeyFileNameError> {
        let base = [PUBLIC_SUFFIX, PRIVATE_SUFFIX, STATE_SUFFIX]
            .iter()
            .find_map(|suffix| file_name.strip_suffix(suffix))
            .ok_or(DnssecKeyFileNameError::NotAKeyFile)?;
        let rest = base
            .strip_prefix('K')
            .ok_or(DnssecKeyFileNameError::NotAKeyFile)?;
        let (zone_text, tag_text) = rest
            .rsplit_once(ALGORITHM_PART)
            .ok_or(DnssecKeyFileNameError::NotAKeyFile)?;

        if tag_text.len() != 5 || !tag_text.bytes().all(|byte| byte.is_ascii_digit()) {
            return Err(DnssecKeyFileNameError::NotAKeyFile);
        }
        let key_tag: u16 = tag_text
            .parse()
            .map_err(|_| DnssecKeyFileNameError::NotAKeyFile)?;
        let zone = ZoneName::parse(zone_text).map_err(|_| DnssecKeyFileNameError::NotAKeyFile)?;
        let canonical =
            Self::from_parts(&zone, key_tag).map_err(|_| DnssecKeyFileNameError::NotAKeyFile)?;

        if canonical.base != base {
            return Err(DnssecKeyFileNameError::NotAKeyFile);
        }

        Ok(canonical)
    }

    /// Builds the base name from a zone and a tag.
    fn from_parts(zone: &ZoneName, key_tag: u16) -> Result<Self, DnssecKeyFileNameError> {
        let base = format!("K{}{ALGORITHM_PART}{key_tag:05}", zone.as_str());

        if base.len() + LONGEST_SUFFIX.len() > MAX_FILE_NAME {
            return Err(DnssecKeyFileNameError::TooLong);
        }

        Ok(Self {
            zone: zone.clone(),
            key_tag,
            base,
        })
    }

    /// The zone this key signs.
    #[must_use]
    pub fn zone(&self) -> &ZoneName {
        &self.zone
    }

    /// The key tag in the name.
    #[must_use]
    pub fn key_tag(&self) -> u16 {
        self.key_tag
    }

    /// The base name, `K<zone>.+013+<tag>`, with no extension.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.base
    }

    /// The public key file, `<base>.key`.
    #[must_use]
    pub fn public_file(&self) -> String {
        format!("{}{PUBLIC_SUFFIX}", self.base)
    }

    /// The private key file, `<base>.private`.
    #[must_use]
    pub fn private_file(&self) -> String {
        format!("{}{PRIVATE_SUFFIX}", self.base)
    }

    /// The timing-state file the daemon keeps beside the key, `<base>.state`.
    #[must_use]
    pub fn state_file(&self) -> String {
        format!("{}{STATE_SUFFIX}", self.base)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/dnssec_key_file_name_tests.rs"]
mod tests;
