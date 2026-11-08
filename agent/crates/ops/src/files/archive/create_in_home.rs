//! Creating an archive in a customer's home, as the customer.

use std::ffi::OsStr;
use std::fs::Permissions;
use std::io;
use std::os::unix::fs::PermissionsExt;
use std::path::Path;

use maran_agent_core::privs::create_file_in_directory::create_file_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::rename_no_replace_between_directories::rename_no_replace_between_directories;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use super::archive_refusal::ArchiveRefusal;
use super::archive_sink::ArchiveSink;
use super::plan_sources::plan_sources;
use super::refuse_reserved_location::refuse_reserved_location;
use super::tar_gz_archive_sink::TarGzArchiveSink;
use super::write_archive::write_archive;
use super::write_refusal::write_refusal;
use super::zip_archive_sink::ZipArchiveSink;
use crate::files::model::archive_format::ArchiveFormat;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::create_archive_input::CreateArchiveInput;
use crate::files::open_directory_in_home::open_directory_in_home;
use crate::files::open_home::open_home;

/// Mode the temporary file is created with, before it is published.
const CREATE_MODE: libc::mode_t = 0o600;

/// Mode of the finished archive.
const ARCHIVE_MODE: u32 = 0o644;

/// Creates the archive `input` describes below `home`, as the account owning `uid`.
///
/// Runs inside the forked child. In order, and what each step refuses:
///
/// 0. **Refuse the trash** as a source or as the destination: the service layer
///    already does, and this is the second layer, so the operation holds the
///    line whoever calls it ([`refuse_reserved_location`]).
/// 1. **Measure the sources** ([`plan_sources`]) before any file exists: a link,
///    FIFO, socket or device anywhere in a source, a name an archive cannot hold,
///    a tree past the entry cap or the depth ceiling refuses the whole request.
/// 2. **Refuse a destination that is taken**, with `AlreadyExists`, before work.
/// 3. **Write to a temporary file** beside the destination, created
///    `O_CREAT | O_EXCL`, so the archive is never visible half made and a name a
///    customer occupied first cannot be written through.
/// 4. **Publish with a rename that cannot replace** (`RENAME_NOREPLACE`): a
///    destination created since step 2 is refused, not overwritten.
/// 5. **On any failure remove the temporary file.** A child killed before this
///    (its patience ran out) leaves a hidden `.maran-archive-*` file the account
///    owns and can delete; nothing else is left.
///
/// The destination being inside a source is refused by [`CreateArchiveInput`]
/// before the fork, so the archive cannot be asked to contain itself.
///
/// # Errors
///
/// Every [`ArchiveRefusal`] the steps above name.
pub(crate) fn create_in_home(
    home: &Path,
    input: &CreateArchiveInput,
    temporary_name: &str,
    uid: u32,
) -> Result<ArchiveReport, ArchiveRefusal> {
    refuse_reserved_location(input.destination())?;
    for source in input.sources() {
        refuse_reserved_location(source)?;
    }

    let home_directory =
        open_home(home, uid).map_err(|error| ArchiveRefusal::from_walk_error(&error))?;

    let destination = input.destination();
    let parent_path = destination
        .parent()
        .ok_or(ArchiveRefusal::UnsupportedEntry)?;
    let parent = open_directory_in_home(home, &parent_path, uid)
        .map_err(|error| ArchiveRefusal::from_walk_error(&error))?;
    let leaf = OsStr::new(destination.file_name());

    let plan = plan_sources(&home_directory, uid, input.sources(), input.bounds())?;

    match stat_in_directory(&parent, leaf) {
        Ok(_) => return Err(ArchiveRefusal::AlreadyExists),
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(_) => return Err(ArchiveRefusal::DirectoryUnusable),
    }

    let temporary = OsStr::new(temporary_name);
    let file = create_file_in_directory(&parent, temporary, CREATE_MODE)
        .map_err(|error| write_refusal(&error))?;

    let mut sink: Box<dyn ArchiveSink> = match input.format() {
        ArchiveFormat::Zip => Box::new(ZipArchiveSink::new(file)),
        ArchiveFormat::TarGz => Box::new(TarGzArchiveSink::new(file)),
    };

    let finished = write_archive(sink.as_mut(), &plan, &home_directory, uid, input.bounds())
        .and_then(|entries| {
            let file = sink.finish()?;
            file.set_permissions(Permissions::from_mode(ARCHIVE_MODE))
                .map_err(|error| write_refusal(&error))?;
            file.sync_all().map_err(|error| write_refusal(&error))?;
            let size = file
                .metadata()
                .map_err(|error| write_refusal(&error))?
                .len();
            Ok(ArchiveReport {
                entries,
                bytes: size,
            })
        })
        .and_then(|report| {
            rename_no_replace_between_directories(&parent, temporary, &parent, leaf).map_err(
                |error| {
                    if error.kind() == io::ErrorKind::AlreadyExists {
                        ArchiveRefusal::AlreadyExists
                    } else {
                        write_refusal(&error)
                    }
                },
            )?;
            Ok(report)
        });

    if finished.is_err() {
        // Best effort, and silent: the creation already failed for a reason worth
        // reporting, and replacing it with "cleanup failed" would hide it.
        let _ = remove_file_in_directory(&parent, temporary);
    }
    finished
}

#[cfg(test)]
#[path = "../../tests/files/archive/create_in_home_tests.rs"]
mod tests;
