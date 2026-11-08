//! The destinations of one alias, forwarder or catch-all.

use std::collections::HashSet;

use super::mail_address::MailAddress;
use super::mail_alias_targets_error::MailAliasTargetsError;

/// The most targets one alias may have. A fan-out is how one received message
/// becomes many sent ones, so the ceiling is what bounds a forwarder as an
/// amplifier.
pub const MAXIMUM_TARGETS: usize = 20;

/// A non-empty, bounded, duplicate-free list of [`MailAddress`] values, in the
/// order they were given.
///
/// This is the whole of "where does this alias go". Whether a target on another
/// host is ALLOWED is a host setting (`allow_external_forwarding`) that the
/// operation applies with [`MailAddress::domain`]; it is not a property of an
/// address's spelling.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MailAliasTargets(Vec<MailAddress>);

impl MailAliasTargets {
    /// Validates every candidate and wraps the list.
    ///
    /// # Errors
    ///
    /// - [`MailAliasTargetsError::Empty`] for an empty list.
    /// - [`MailAliasTargetsError::TooMany`] beyond [`MAXIMUM_TARGETS`]; checked
    ///   BEFORE any address is parsed, so an enormous list costs nothing.
    /// - [`MailAliasTargetsError::InvalidTarget`] for the first refused address.
    /// - [`MailAliasTargetsError::Duplicate`] for the first repeat.
    pub fn parse<S: AsRef<str>>(candidates: &[S]) -> Result<Self, MailAliasTargetsError> {
        if candidates.is_empty() {
            return Err(MailAliasTargetsError::Empty);
        }

        if candidates.len() > MAXIMUM_TARGETS {
            return Err(MailAliasTargetsError::TooMany {
                maximum: MAXIMUM_TARGETS,
            });
        }

        let mut seen = HashSet::new();
        let mut targets = Vec::with_capacity(candidates.len());

        for (index, candidate) in candidates.iter().enumerate() {
            let address = MailAddress::parse(candidate.as_ref())
                .map_err(|source| MailAliasTargetsError::InvalidTarget { index, source })?;

            if !seen.insert(address.clone()) {
                return Err(MailAliasTargetsError::Duplicate { index });
            }

            targets.push(address);
        }

        Ok(Self(targets))
    }

    /// The targets, in the order given.
    #[must_use]
    pub fn as_slice(&self) -> &[MailAddress] {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_alias_targets_tests.rs"]
mod tests;
