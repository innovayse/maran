//! The property tag of a CAA record.

use crate::validation::dns::caa_tag_error::CaaTagError;

/// The three CAA properties the panel offers: `issue`, `issuewild` and `iodef`.
///
/// A closed set, so a tag is a choice and never text written into the file.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CaaTag {
    /// Who may issue certificates for the name.
    Issue,
    /// Who may issue wildcard certificates for the name.
    IssueWild,
    /// Where an authority reports a violation.
    Iodef,
}

impl CaaTag {
    /// Parses `candidate`, ignoring case.
    ///
    /// # Errors
    ///
    /// [`CaaTagError::Unknown`] for anything but the three tags.
    pub fn parse(candidate: &str) -> Result<Self, CaaTagError> {
        match candidate.to_ascii_lowercase().as_str() {
            "issue" => Ok(Self::Issue),
            "issuewild" => Ok(Self::IssueWild),
            "iodef" => Ok(Self::Iodef),
            _ => Err(CaaTagError::Unknown),
        }
    }

    /// The tag as the zone file spells it.
    #[must_use]
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Issue => "issue",
            Self::IssueWild => "issuewild",
            Self::Iodef => "iodef",
        }
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/caa_tag_tests.rs"]
mod tests;
