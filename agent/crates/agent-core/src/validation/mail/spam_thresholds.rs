//! The three scores at which the spam filter acts.

use super::spam_thresholds_error::SpamThresholdsError;

/// The highest score accepted, in whole points of the filter's scale. Far above
/// the scores real mail reaches; it exists so an extra digit is refused.
pub const MAXIMUM_SCORE: u32 = 100;

/// The greylist, add-header and reject scores of the spam filter, in whole
/// points, strictly increasing.
///
/// Whole points rather than fractions on purpose: a fractional score is a
/// decimal string in a configuration file, and an integer is digits only
/// (rules/security.md item 4). The scale is coarse enough for an operator's
/// choice and the filter's own fractional scores still compare against it.
///
/// The order is a property of the type, not a check a caller may forget: a
/// reject score below the add-header score would refuse mail the filter would
/// otherwise have filed.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct SpamThresholds {
    /// Score from which a message is delayed once.
    greylist: u32,
    /// Score from which a message is treated as spam.
    add_header: u32,
    /// Score from which a message is refused at the SMTP stage.
    reject: u32,
}

impl SpamThresholds {
    /// Validates the three scores and wraps them.
    ///
    /// # Errors
    ///
    /// - [`SpamThresholdsError::Zero`] when any score is `0`.
    /// - [`SpamThresholdsError::TooLarge`] when any score is above
    ///   [`MAXIMUM_SCORE`].
    /// - [`SpamThresholdsError::NotAscending`] unless
    ///   `greylist < add_header < reject`.
    pub fn parse(greylist: u32, add_header: u32, reject: u32) -> Result<Self, SpamThresholdsError> {
        let scores = [greylist, add_header, reject];

        if scores.contains(&0) {
            return Err(SpamThresholdsError::Zero);
        }

        if scores.iter().any(|score| *score > MAXIMUM_SCORE) {
            return Err(SpamThresholdsError::TooLarge {
                maximum: MAXIMUM_SCORE,
            });
        }

        if !(greylist < add_header && add_header < reject) {
            return Err(SpamThresholdsError::NotAscending);
        }

        Ok(Self {
            greylist,
            add_header,
            reject,
        })
    }

    /// The score from which a message is delayed once.
    #[must_use]
    pub fn greylist(&self) -> u32 {
        self.greylist
    }

    /// The score from which a message is treated as spam.
    #[must_use]
    pub fn add_header(&self) -> u32 {
        self.add_header
    }

    /// The score from which a message is refused at the SMTP stage.
    #[must_use]
    pub fn reject(&self) -> u32 {
        self.reject
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/spam_thresholds_tests.rs"]
mod tests;
