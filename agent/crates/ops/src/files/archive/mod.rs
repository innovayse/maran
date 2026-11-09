//! The steps ONE archive operation is physically made of, crate-private.
//!
//! An archive is the one thing in this agent whose bytes are parsed by someone
//! else's code, so everything here runs inside the child that has dropped to the
//! account, and the root parent sees nothing but a tiny strictly decoded answer
//! (`archive_answer`). The reading side judges an archive twice through the same
//! walk (`walk_members`): once to refuse it as a whole before a byte is written,
//! once to write, because the file belongs to the customer and may change in
//! between. The writing side reaches every directory by descriptor
//! (`directory_cursor`), creates files `O_EXCL` and records what it made
//! (`extraction_ledger`) so a failure can take away exactly that.
//!
//! None of the archive crates' own extraction helpers (`unpack`, `extract`,
//! `enclosed_name`) is called anywhere: they write or decide by path, and the
//! agent writes only what its own `ArchiveMemberName` accepted. The threat note
//! is `docs/superpowers/notes/2026-11-07-file-manager-archives-threat-note.md`;
//! its second review is OUTSTANDING (rules/security.md "Sensitive change
//! escalation").

pub(crate) mod archive_answer;
mod archive_member;
pub(crate) mod archive_refusal;
mod archive_sink;
mod classify_zip_entry;
mod common_parent_length;
mod copy_member_data;
mod counting_reader;
pub(crate) mod create_in_home;
mod created_entry;
mod data_access;
mod directory_cursor;
mod expansion_budget;
pub(crate) mod extract_in_home;
mod extraction_ledger;
mod extraction_writer;
mod marked_reader;
mod member_kind;
mod member_registry;
mod member_visitor;
mod open_archive;
mod opened_archive;
mod plan_sources;
mod planned_entry;
mod refuse_reserved_location;
mod refuse_reserved_member;
mod source_plan;
mod tar_gz_archive_sink;
mod walk_members;
mod walk_tar_gz_members;
mod walk_zip_members;
mod write_archive;
mod write_refusal;
mod zip_archive_sink;
mod zip_central_entry_count;

/// Test-only builders of archives, including hostile ones the archive crates refuse to write.
#[cfg(test)]
#[path = "../../tests/files/archive/archive_fixtures.rs"]
pub(crate) mod archive_fixtures;
