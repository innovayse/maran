//! Everything one entry removal is told, already validated.

use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::relative_path::RelativePath;

/// A validated request to remove one file inside an account's home.
///
/// There is no `recursive` field, and its absence is the design rather than an
/// omission. `files.proto` declares the flag, and the agent refuses a request
/// that sets it (see `validated_delete` on the service side) because recursive
/// removal is not implemented: a request that reached this type has already
/// been narrowed to a single file, so no operation below can act on a flag it
/// was never given (rules/architecture.md — the agent implements what it has,
/// not what it might).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DeleteEntryInput {
    /// The account whose uid the removal runs as, and whose home it is
    /// contained in.
    pub account: AccountName,
    /// The file to remove, relative to that home.
    pub path: RelativePath,
}
