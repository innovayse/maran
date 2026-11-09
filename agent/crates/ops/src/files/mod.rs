//! Files inside a hosting customer's home.
//!
//! Writing one, taking it away, and — read-only — listing a directory, reading a
//! file and searching names. The write side serves the ACME HTTP-01 challenge
//! (a token file at `<document root>/.well-known/acme-challenge/<token>`); the
//! browse side ([`BrowseHost`]) serves the file manager and creates, changes and
//! deletes nothing. `files.proto` declares more rpcs than are implemented; the
//! rest answer `UNIMPLEMENTED` rather than being written against a caller that
//! does not exist yet (rules/architecture.md — a method nobody calls is a
//! surface nobody reviews).
//!
//! The file manager's mutations — create a directory, move, copy, change
//! permissions, delete a tree, and the per-account trash with its retention
//! purge — are methods of [`FilesHost`] beside the ACME write and removal. They share the walk below and
//! add `tree/`, the descriptor-only recursion over a customer's tree; the
//! threat note is
//! `docs/superpowers/notes/2026-11-07-file-manager-mutations-threat-note.md`.
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

mod account_home;
mod archive;
mod archive_host;
mod browse_host;
mod change_permissions;
mod child_upload;
mod chmod_in_home;
mod copy_entry;
mod copy_in_home;
mod create_archive;
mod create_directory;
mod create_directory_in_home;
mod decode_search_outcome;
mod decode_trash_listing;
mod delete_entry;
mod empty_trash;
mod empty_trash_in_home;
mod encode_search_outcome;
mod encode_trash_listing;
mod entry_open_flags;
mod extract_archive;
#[cfg(test)]
#[path = "../tests/files/fake_archive_host.rs"]
pub(crate) mod fake_archive_host;
#[cfg(test)]
#[path = "../tests/files/fake_browse_host.rs"]
pub(crate) mod fake_browse_host;
#[cfg(test)]
#[path = "../tests/files/fake_files_host.rs"]
pub(crate) mod fake_files_host;
mod feed_stop;
mod feed_upload;
mod files_error_of_privilege;
mod files_host;
mod files_op_error;
mod files_refusal;
mod force_delete_entry;
mod force_delete_in_home;
mod fork_files_work;
mod fork_upload_work;
#[cfg(test)]
#[path = "../tests/files/home_fixture.rs"]
pub(crate) mod home_fixture;
mod io_failure;
mod judge_movable_source;
mod list_directory;
mod list_in_home;
mod list_trash;
mod list_trash_in_home;
pub mod model;
mod move_entry;
mod move_in_home;
mod mutable_path_components;
mod new_trash_entry_id;
mod open_directory_in_home;
mod open_home;
mod open_level;
mod open_mutable_parent;
// Private: the hardened walk, write and unlink a privileged file operation
// needs. `ProcessFilesHost` is their only caller, and nothing outside this area
// should be able to start one by another route.
mod open_parent_directory;
mod open_private_directory;
mod open_trash_subdirectory;
mod parse_trashed_original;
mod process_archive_host;
mod process_browse_host;
mod process_files_host;
mod process_mutations;
mod purge_expired_trash;
mod purge_in_home;
mod read_file;
mod read_in_home;
mod read_trash_original;
mod refuse_taken_name;
mod remove_in_home;
mod remove_trash_entry;
mod rename_failure;
mod representable_name;
mod restore_in_home;
mod restore_trash_entry;
mod search_entries;
mod search_in_home;
mod stream_opened_file;
mod trash_entry;
mod trash_in_home;
mod trashed_original_in_home;
mod tree;
mod work_patience;
mod write_file;
mod write_in_home;

pub use archive_host::ArchiveHost;
pub use browse_host::BrowseHost;
pub use change_permissions::change_permissions;
pub use copy_entry::copy_entry;
pub use create_archive::create_archive;
pub use create_directory::create_directory;
pub use delete_entry::delete_entry;
pub use empty_trash::empty_trash;
pub use extract_archive::extract_archive;
pub use files_host::FilesHost;
pub use files_op_error::FilesOpError;
pub use force_delete_entry::force_delete_entry;
pub use list_directory::list_directory;
pub use list_trash::list_trash;
pub use model::change_permissions_input::ChangePermissionsInput;
pub use model::copy_entry_input::CopyEntryInput;
pub use model::create_directory_input::CreateDirectoryInput;
pub use model::delete_entry_input::DeleteEntryInput;
pub use model::force_delete_entry_input::ForceDeleteEntryInput;
pub use model::list_directory_input::ListDirectoryInput;
pub use model::move_entry_input::MoveEntryInput;
pub use model::purge_expired_trash_input::PurgeExpiredTrashInput;
pub use model::read_file_input::ReadFileInput;
pub use model::restore_trash_entry_input::RestoreTrashEntryInput;
pub use model::trash_entry_input::TrashEntryInput;
pub use model::write_file_input::WriteFileInput;
pub use move_entry::move_entry;
pub use process_archive_host::ProcessArchiveHost;
pub use process_browse_host::ProcessBrowseHost;
pub use process_files_host::ProcessFilesHost;
pub use purge_expired_trash::purge_expired_trash;
pub use read_file::read_file;
pub use restore_trash_entry::restore_trash_entry;
pub use search_entries::search_entries;
pub use trash_entry::trash_entry;
pub use write_file::write_file;
