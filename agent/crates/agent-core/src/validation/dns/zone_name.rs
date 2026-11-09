//! The apex of a zone: the name the daemon is authoritative for.

use crate::validation::dns::zone_name_error::ZoneNameError;
use crate::validation::web::domain::Domain;

/// The least a zone apex has: a bare label such as `com` would make the daemon
/// authoritative for a whole top-level domain.
const MIN_LABELS: usize = 2;

/// A well-formed, non-reserved name with at least two labels, short enough to be
/// the stem of a file name.
///
/// The shape check, the lowercasing and the reserved set (`localhost`, the
/// special-use suffixes, an address-shaped final label) are [`Domain::parse`]'s —
/// one answer to "is this a hostname a tenant may claim" — and this type adds the
/// two things only a zone needs. It must have two labels. And it must leave room
/// for the file the zone is kept in: a file name is limited to 255 bytes, the
/// file is `<zone>.db`, and a DNS name may be 253, so a name of 251 to 253
/// characters would parse as a hostname and then fail as a path.
///
/// A trailing dot (`example.com.`) is accepted and dropped, because that is the
/// spelling a DNS tool prints; the stored value never carries it. The value
/// contains only ASCII letters, digits, `-` and `.`, which is what makes it safe
/// as a zone-file `$ORIGIN`, a `named-checkzone` argument, an `rndc` argument and
/// a file stem without further escaping.
///
/// Reverse zones (`...in-addr.arpa`) are not a feature of this panel; a numeric
/// final label is refused by [`Domain::parse`] and `arpa` zones are not offered.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct ZoneName(String);

impl ZoneName {
    /// The longest zone name accepted, in characters: 255 bytes of file name less
    /// the three of `.db`, less two spare so a rename suffix cannot reach the limit.
    pub const MAX_LENGTH: usize = 250;

    /// Parses `candidate` into a zone name.
    ///
    /// # Errors
    ///
    /// - [`ZoneNameError::Empty`] for an empty name, or one that is only a dot.
    /// - [`ZoneNameError::TooLong`] beyond [`Self::MAX_LENGTH`] characters.
    /// - [`ZoneNameError::NotAHostName`] with the reason [`Domain::parse`] gives:
    ///   an illegal character (a newline, a `/`, a `;`, a space), an empty or
    ///   over-long label, a hyphen at an edge of a label, an address-shaped final
    ///   label, `localhost`, or a special-use suffix.
    /// - [`ZoneNameError::SingleLabel`] for a name with fewer than two labels.
    pub fn parse(candidate: &str) -> Result<Self, ZoneNameError> {
        let name = candidate.strip_suffix('.').unwrap_or(candidate);

        if name.is_empty() {
            return Err(ZoneNameError::Empty);
        }

        if name.len() > Self::MAX_LENGTH {
            return Err(ZoneNameError::TooLong);
        }

        let domain = Domain::parse(name).map_err(ZoneNameError::NotAHostName)?;

        if domain.as_str().split('.').count() < MIN_LABELS {
            return Err(ZoneNameError::SingleLabel);
        }

        Ok(Self(domain.as_str().to_owned()))
    }

    /// The validated name, lowercase, without a trailing dot.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// The name as a zone file and `rndc` write an absolute name: with the
    /// trailing dot.
    #[must_use]
    pub fn fully_qualified(&self) -> String {
        format!("{}.", self.0)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/zone_name_tests.rs"]
mod tests;
