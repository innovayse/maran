//! Writing the members of an archive into a customer's tree, one descriptor at a time.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io::{self, Read};
use std::os::unix::fs::PermissionsExt;

use maran_agent_core::privs::create_file_in_directory::create_file_in_directory;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;

use super::archive_member::ArchiveMember;
use super::archive_refusal::ArchiveRefusal;
use super::copy_member_data::copy_member_data;
use super::directory_cursor::DirectoryCursor;
use super::expansion_budget::ExpansionBudget;
use super::extraction_ledger::ExtractionLedger;
use super::member_kind::MemberKind;
use super::refuse_reserved_member::refuse_reserved_member;
use super::write_refusal::write_refusal;
use crate::files::model::archive_report::ArchiveReport;

/// Mode a file is created with, before its content is in and its final mode is set.
const CREATE_MODE: libc::mode_t = 0o600;

/// Mode of an extracted file the archive marked executable.
const EXECUTABLE_MODE: u32 = 0o755;

/// Mode of every other extracted file.
const PLAIN_MODE: u32 = 0o644;

/// The writing half of an extraction: a cursor, a ledger and a budget.
///
/// Modes are not taken from the archive. A file becomes 0644, or 0755 when the
/// archive set any execute bit; a directory it makes becomes 0755. A setuid,
/// setgid, sticky or world-writable mode in an archive therefore has no effect
/// on the account's tree, whatever the archive says.
///
/// Nothing is overwritten: a file is created with `O_CREAT | O_EXCL`, which
/// refuses a regular file, a directory and a symlink (resolving or not) at the
/// name alike. A directory that already exists is merged into, after being
/// opened without following a link and found to be the account's.
///
/// The budget here counts the bytes this writer ACTUALLY produces, separately
/// from the walk's, which counts what the headers declare.
#[derive(Debug)]
pub(crate) struct ExtractionWriter {
    /// The components of the destination below the home.
    destination: Vec<String>,
    /// Where the writer stands in the tree; rooted at the home.
    cursor: DirectoryCursor,
    /// Everything made so far.
    ledger: ExtractionLedger,
    /// The bytes produced so far, against the request's caps.
    budget: ExpansionBudget,
    /// The home, kept so a rollback can start from it.
    home: File,
    /// The uid the tree must belong to.
    uid: u32,
    /// Members written so far.
    members: u64,
}

impl ExtractionWriter {
    /// A writer for an extraction into `destination` below `home`.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::HomeUnusable`] when the home descriptor cannot be duplicated.
    pub(crate) fn new(
        home: File,
        uid: u32,
        destination: &[String],
        bounds: &ExtractBounds,
        archive_length: u64,
    ) -> Result<Self, ArchiveRefusal> {
        let root = home.try_clone().map_err(|_| ArchiveRefusal::HomeUnusable)?;
        Ok(Self {
            destination: destination.to_vec(),
            cursor: DirectoryCursor::new(root, uid),
            ledger: ExtractionLedger::new(),
            budget: ExpansionBudget::for_extraction(bounds, archive_length),
            home,
            uid,
            members: 0,
        })
    }

    /// Brings the destination into being, creating the levels that are missing.
    ///
    /// # Errors
    ///
    /// As [`DirectoryCursor::enter`] with creation.
    pub(crate) fn prepare(&mut self) -> Result<(), ArchiveRefusal> {
        let components: Vec<&str> = self.destination.iter().map(String::as_str).collect();
        self.cursor.enter(&components, Some(&mut self.ledger))?;
        Ok(())
    }

    /// Writes one member.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::AlreadyExists`] when a name is taken,
    /// [`ArchiveRefusal::DirectoryUnusable`] for a directory that is not the
    /// account's, the budget's refusals and [`ArchiveRefusal::SizeMismatch`] as
    /// [`copy_member_data`] documents them, and the write refusals.
    pub(crate) fn write(
        &mut self,
        member: &ArchiveMember,
        data: &mut dyn Read,
    ) -> Result<(), ArchiveRefusal> {
        refuse_reserved_member(self.destination.is_empty(), member)?;

        let mut components: Vec<&str> = self.destination.iter().map(String::as_str).collect();
        components.extend(member.name.components());

        match member.kind {
            MemberKind::Directory => {
                self.cursor.enter(&components, Some(&mut self.ledger))?;
            }
            MemberKind::File => {
                let (leaf, parents) = components.split_last().ok_or(ArchiveRefusal::UnsafeName)?;
                let parent = self.cursor.enter(parents, Some(&mut self.ledger))?;

                let mut file = create_file_in_directory(parent, OsStr::new(leaf), CREATE_MODE)
                    .map_err(|error| {
                        if error.kind() == io::ErrorKind::AlreadyExists {
                            ArchiveRefusal::AlreadyExists
                        } else {
                            write_refusal(&error)
                        }
                    })?;
                // Recorded the moment it exists, so a failure while filling it
                // takes it away again.
                self.ledger.record_file(components.join("/"));

                copy_member_data(data, &mut file, &mut self.budget, member.declared_size)?;
                let mode = if member.executable {
                    EXECUTABLE_MODE
                } else {
                    PLAIN_MODE
                };
                file.set_permissions(Permissions::from_mode(mode))
                    .map_err(|error| write_refusal(&error))?;
            }
        }

        self.members += 1;
        Ok(())
    }

    /// What was written.
    pub(crate) fn report(&self) -> ArchiveReport {
        ArchiveReport {
            entries: self.members,
            bytes: self.budget.bytes(),
        }
    }

    /// Takes away everything this writer made.
    pub(crate) fn roll_back(self) {
        self.ledger.roll_back(&self.home, self.uid);
    }
}
