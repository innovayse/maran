//! The mail stack as the status reports it: the state and the readings beside it.

use crate::mail::model::mail_details::MailDetails;
use crate::mail::model::mail_state::MailState;

/// A [`MailState`] with the [`MailDetails`] read after it.
///
/// What every lifecycle rpc answers with, so none of them tells a caller "done"
/// without showing what the host does.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ObservedMail {
    /// Which daemons run and which ports are bound.
    pub state: MailState,
    /// Versions, the password scheme, the queue and Sieve.
    pub details: MailDetails,
}
