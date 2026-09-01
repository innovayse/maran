//! WriteFile: one file into a customer's home, as the customer.

use crate::files::model::write_file_input::WriteFileInput;
use crate::files::{FilesHost, FilesOpError};

/// Writes one file inside an account's home and reports how many bytes landed.
///
/// The only caller today is an ACME HTTP-01 challenge: a token file under the
/// site's document root that a certificate authority fetches once and that is
/// removed again by [`super::delete_entry`]. The file is inside a customer's
/// home, so it is written by a process that has dropped to that account —
/// never by the root daemon (rules/security.md item 3).
///
/// Two steps, both as the account:
///
/// 1. **Create the directories.** `.well-known/acme-challenge/` does not exist
///    until the first issuance asks for it, and the panel has no other way to
///    bring it into being.
/// 2. **Write the file**, through a descriptor walk that refuses a symlink at
///    every level.
///
/// **The walk IS the containment, and there is deliberately no second check.**
/// `resolve_in_home` — the call rules/security.md item 2 names — is not here,
/// and its absence is the design rather than an omission. The walk starts at
/// `/home/<account>`, follows no symlink at any level, and traverses a component
/// list that provably holds no `..`, no `/` and no empty component; a descent
/// with those three properties **cannot end outside the home**. There is no path
/// it can reach that `resolve_in_home` would have refused, so the call could not
/// fail, and no test could tell its presence from its absence.
///
/// An earlier version had it here and described it as defence in depth. Two
/// rounds of review turned that into a rule worth stating: **a defensive call
/// that cannot fail is decoration, and decoration in a security-critical
/// function is worse than nothing**, because the next reader sees a containment
/// call and reasons about the function as though something were being contained
/// by it. The comment explaining that it was inert would have been one refactor
/// away from being stale rather than one refactor away from being true. The same
/// judgement deleted an unreachable second-header check in this change's service
/// layer, and an `IgnoreQueryFilters()` no mutation could distinguish from its
/// own absence in the panel.
///
/// [`super::delete_entry`] **does** call `resolve_in_home`, and that is not an
/// inconsistency between the two operations — it is a difference between them.
/// A removal has to LOCATE an entry that already exists, and only a resolution
/// can tell "there is nothing there" from "the child refused it". A write does
/// not have to locate anything: the walk constructs the path as it goes, level
/// by level, creating what is missing. Making both operations claim the same
/// shape would hide a real difference; the explanation lives on `delete_entry`,
/// where the call actually does something.
///
/// Idempotent as `files.proto` requires: writing the same content twice leaves
/// the same end state, and writing different content replaces it. There is no
/// "already exists" answer, because a challenge that is re-issued must be
/// overwritten — the authority is about to fetch the NEW token.
///
/// There is no mode check here either, and its absence is the same kind of
/// design: `mode` is a
/// [`FileMode`](maran_agent_core::validation::file_mode::FileMode), which cannot
/// be constructed from anything but plain permission bits.
///
/// # Errors
///
/// Returns [`FilesOpError::Privilege`] when the account cannot be resolved or
/// the privilege drop fails; [`FilesOpError::HomeUnusable`] when the account's
/// home is not a directory it owns; [`FilesOpError::DirectoryUnusable`] when a
/// level cannot be created, is a symlink, or is not a directory the account
/// owns; and [`FilesOpError::WriteFailed`] when the content cannot be placed.
pub fn write_file(host: &dyn FilesHost, input: &WriteFileInput) -> Result<u64, FilesOpError> {
    host.create_parents_as_account(&input.account, &input.path)?;

    host.write_as_account(&input.account, &input.path, &input.contents, input.mode)?;

    // What was asked for, not what the child claimed: the child reports success
    // or failure and nothing else, and `write_all` succeeded means every byte
    // went out. The panel compares this against what it sent.
    Ok(input.contents.len() as u64)
}

#[cfg(test)]
#[path = "../tests/files/write_file_tests.rs"]
mod tests;
