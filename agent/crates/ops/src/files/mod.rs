//! Files inside a hosting customer's home: writing one, and taking it away.
//!
//! **This area exists for exactly one caller today, and it is written no wider
//! than that caller needs.** An ACME HTTP-01 challenge is answered by putting a
//! token file at `<document root>/.well-known/acme-challenge/<token>` and
//! removing it once the authority has read it. `files.proto` declares nine
//! rpcs; two of them are implemented, and the other seven answer
//! `UNIMPLEMENTED` rather than being written against a caller that does not
//! exist yet (rules/architecture.md — the agent's command set is closed, and a
//! method nobody calls is a surface nobody reviews).
//!
//! Everything here is shaped by one fact: **the customer owns the directories
//! being walked.** They can, between any two syscalls the agent makes, replace
//! `sites/<domain>` with a symlink, put a FIFO where the token goes, or hardlink
//! somebody else's file to that name. So no operation resolves a path and then
//! reopens it: the account's home is opened once, and every level below it is
//! reached from the descriptor above with `openat` and `O_NOFOLLOW`. A
//! descriptor names an inode, and no rename moves an inode.
//!
//! **That walk is the containment, and it is the only one.** A descent that
//! begins at `/home/<account>`, follows no symlink at any level, and traverses a
//! `..`-free component list cannot end outside the home. rules/security.md item
//! 2 names `resolve_in_home` as the containment check, and this area satisfies
//! that item with something strictly stronger: `resolve_in_home` answers "where
//! does this path lead?" at one instant and leaves a window before the path is
//! used, which is the race its own documentation warns about; the walk has no
//! such window, because a descriptor names an inode and no rename moves an
//! inode.
//!
//! `resolve_in_home` survives in exactly one place — [`delete_entry`] — and for
//! one reason that has nothing to do with containment: a removal must be able to
//! answer "there was nothing there", and the forked child cannot, because its
//! outcome is an exit status. `write_file` used to call it too, described as
//! defence in depth; review established that it could not fail there, and a
//! defensive call that cannot fail is decoration. The full argument is on both
//! operations.
//!
//! And the second protection is the oldest one: every byte is written by a
//! process that has dropped to the account through `fork_as_account`.
//! Containment without a dropped uid is a check an attacker races; a dropped uid
//! without containment writes wherever the account can reach.

mod delete_entry;
#[cfg(test)]
#[path = "../tests/files/fake_files_host.rs"]
pub(crate) mod fake_files_host;
mod files_host;
mod files_op_error;
pub mod model;
// Private: the hardened walk, write and unlink a privileged file operation
// needs. `ProcessFilesHost` is their only caller, and nothing outside this area
// should be able to start one by another route.
mod open_parent_directory;
mod process_files_host;
mod remove_in_home;
mod write_file;
mod write_in_home;

pub use delete_entry::delete_entry;
pub use files_host::FilesHost;
pub use files_op_error::FilesOpError;
pub use model::delete_entry_input::DeleteEntryInput;
pub use model::write_file_input::WriteFileInput;
pub use process_files_host::ProcessFilesHost;
pub use write_file::write_file;
