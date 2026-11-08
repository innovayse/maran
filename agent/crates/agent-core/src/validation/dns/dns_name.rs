//! A fully qualified name a record points at.

use crate::validation::dns::dns_name_error::DnsNameError;
use crate::validation::dns::label_fault::LabelFault;
use crate::validation::dns::label_fault_of::label_fault_of;

/// The longest name DNS allows, without the trailing dot.
const MAX_LENGTH: usize = 253;

/// A fully qualified name with at least two labels: the target of a CNAME, an MX
/// or an NS record, an SRV target, and the two names of an SOA.
///
/// Always absolute. A trailing dot is accepted and dropped; the zone file is
/// written with one ([`Self::fully_qualified`]), so a target can never be read as
/// relative to the zone and silently become `target.example.com.example.com.`.
/// Labels are ASCII letters, digits, `-` and `_` (no wildcard), lowercased. A
/// final label made only of digits is refused, which is what stops an IPv4
/// address typed where a name belongs (`MX 10 192.0.2.1` is a classic mistake).
/// A single label is refused: it would name a top-level domain.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DnsName(String);

impl DnsName {
    /// Parses `candidate`.
    ///
    /// # Errors
    ///
    /// - [`DnsNameError::Empty`] for an empty name.
    /// - [`DnsNameError::TooLong`] beyond 253 characters.
    /// - [`DnsNameError::InvalidLabel`] for an empty or over-long label or a hyphen
    ///   at its edge.
    /// - [`DnsNameError::IllegalCharacter`] for any character outside the alphabet
    ///   (a newline, a space, `;`, `"`, `\`, `/`, `*`, `$`, NUL).
    /// - [`DnsNameError::SingleLabel`] for fewer than two labels.
    /// - [`DnsNameError::NumericFinalLabel`] when the last label is all digits.
    pub fn parse(candidate: &str) -> Result<Self, DnsNameError> {
        let name = candidate.strip_suffix('.').unwrap_or(candidate);

        if name.is_empty() {
            return Err(DnsNameError::Empty);
        }

        if name.len() > MAX_LENGTH {
            return Err(DnsNameError::TooLong);
        }

        let mut count = 0_usize;
        for label in name.split('.') {
            count += 1;
            match label_fault_of(label) {
                Some(LabelFault::IllegalCharacter(character)) => {
                    return Err(DnsNameError::IllegalCharacter { character });
                }
                Some(_) => {
                    return Err(DnsNameError::InvalidLabel {
                        label: label.chars().take(63).collect(),
                    });
                }
                None => {}
            }
        }

        if count < 2 {
            return Err(DnsNameError::SingleLabel);
        }

        let last = name.rsplit('.').next().unwrap_or_default();
        if last.chars().all(|c| c.is_ascii_digit()) {
            return Err(DnsNameError::NumericFinalLabel);
        }

        Ok(Self(name.to_ascii_lowercase()))
    }

    /// The validated name, lowercase, without a trailing dot.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// The name with the trailing dot, as a zone file writes an absolute name.
    #[must_use]
    pub fn fully_qualified(&self) -> String {
        format!("{}.", self.0)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/dns_name_tests.rs"]
mod tests;
