//! How a supervised child ended, before an entry point gives the end a meaning.

use crate::command_outcome::CommandOutcome;

/// How the shared supervision loop ended.
///
/// The loop does not know whether reaching the deadline is a failure (an
/// ordinary bounded call) or the success case (a call run for a window), so it
/// reports the plain fact and each entry point decides.
#[derive(Debug)]
pub(crate) enum Supervised {
    /// The child ended and both of its streams reached end-of-file.
    Finished(CommandOutcome),
    /// The deadline passed first; the whole process group was killed.
    Expired,
}
