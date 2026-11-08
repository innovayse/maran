//! The identifier of one message in the MTA's queue.

use super::mail_queue_id_error::MailQueueIdError;

/// The shortest id accepted. The MTA's short ids are ten hexadecimal digits;
/// the floor is also what keeps the queue tool's selector `ALL` (three
/// characters) from being a valid id.
const MINIMUM_LENGTH: usize = 10;

/// The longest id accepted. The MTA's long ids are 16 or so characters; the
/// ceiling leaves room for them and for nothing more.
const MAXIMUM_LENGTH: usize = 25;

/// A validated MTA queue id: ten to twenty-five ASCII letters and digits.
///
/// It is handed to the queue tool as the argument naming the one message to
/// delete. Letters and digits alone cannot start an option, cannot be a
/// selector word and cannot carry a separator, so one id can name one message
/// and never a set. The panel gets ids from the queue listing and sends one
/// back; this type is why an id that was invented rather than listed still
/// cannot do more than name a message that is not there.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct MailQueueId(String);

impl MailQueueId {
    /// Validates `candidate` and wraps it. Case is kept: the long ids are case
    /// sensitive.
    ///
    /// # Errors
    ///
    /// - [`MailQueueIdError::WrongLength`] outside `10..=25` characters.
    /// - [`MailQueueIdError::IllegalCharacter`] for anything but ASCII letters
    ///   and digits.
    pub fn parse(candidate: &str) -> Result<Self, MailQueueIdError> {
        let length = candidate.chars().count();
        if !(MINIMUM_LENGTH..=MAXIMUM_LENGTH).contains(&length) {
            return Err(MailQueueIdError::WrongLength {
                minimum: MINIMUM_LENGTH,
                maximum: MAXIMUM_LENGTH,
            });
        }

        if let Some(character) = candidate.chars().find(|c| !c.is_ascii_alphanumeric()) {
            return Err(MailQueueIdError::IllegalCharacter { character });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated id.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_queue_id_tests.rs"]
mod tests;
