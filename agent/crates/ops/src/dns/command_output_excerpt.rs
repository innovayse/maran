//! What a tool printed, bounded, for an operator's log.

use maran_agent_core::command_outcome::CommandOutcome;

/// The most characters of a tool's output kept in an error.
const MAX_CHARACTERS: usize = 4096;

/// Joins what `outcome` printed on standard error and standard output, cut at
/// 4096 characters. Operator text only: it can name a file and a line of the
/// agent's own rendering, and is never sent on to a hosting customer.
#[must_use]
pub(crate) fn command_output_excerpt(outcome: &CommandOutcome) -> String {
    let mut text = outcome.stderr.trim().to_owned();
    if !outcome.stdout.trim().is_empty() {
        if !text.is_empty() {
            text.push('\n');
        }
        text.push_str(outcome.stdout.trim());
    }
    text.chars().take(MAX_CHARACTERS).collect()
}
