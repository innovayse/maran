//! Filling a new archive from a plan.

use std::ffi::OsStr;
use std::fs::File;
use std::io::Read;

use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;

use super::archive_refusal::ArchiveRefusal;
use super::archive_sink::ArchiveSink;
use super::directory_cursor::DirectoryCursor;
use super::expansion_budget::ExpansionBudget;
use super::member_kind::MemberKind;
use super::source_plan::SourcePlan;

/// Flags a source file is opened with.
///
/// `O_NOFOLLOW` refuses a link planted since the plan was made, and `O_NONBLOCK`
/// keeps a FIFO planted at the name from blocking the open forever; the opened
/// descriptor is then required to be a regular file.
const SOURCE_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;

/// Writes every entry of `plan` into `sink`, reading each source through a descriptor.
///
/// The plan said what was there when it was made; this reads it again and trusts
/// none of it. A source is opened from its parent's descriptor without following
/// a link, `fstat`ed on the descriptor (a regular file, or the whole request is
/// refused), and read for exactly the size that `fstat` reported: a file that
/// grew is cut at that size and one that shrank is a failed read, so the archive
/// never holds a torn file. Bytes are charged against the request's cap as they
/// are admitted. The sink chooses every mode.
///
/// Returns how many entries were written. The caller finishes the sink.
///
/// # Errors
///
/// [`ArchiveRefusal::UnsupportedEntry`] for a source that is no longer a regular
/// file, [`ArchiveRefusal::NotFound`] for one that has gone, the budget's
/// [`ArchiveRefusal::EntryLimit`] and [`ArchiveRefusal::ByteLimit`], and the
/// sink's refusals.
pub(crate) fn write_archive(
    sink: &mut dyn ArchiveSink,
    plan: &SourcePlan,
    home: &File,
    uid: u32,
    bounds: &CreateBounds,
) -> Result<u64, ArchiveRefusal> {
    let root = home.try_clone().map_err(|_| ArchiveRefusal::HomeUnusable)?;
    let mut cursor = DirectoryCursor::new(root, uid);
    let mut budget = ExpansionBudget::for_creation(bounds);
    let mut written = 0_u64;

    for entry in &plan.entries {
        budget.admit_entry()?;
        let name = plan.name_of(entry)?;

        match entry.kind {
            MemberKind::Directory => sink.add_directory(&name)?,
            MemberKind::File => {
                let components: Vec<&str> = entry.path.split('/').collect();
                let (leaf, parents) = components
                    .split_last()
                    .ok_or(ArchiveRefusal::UnsupportedEntry)?;
                let parent = cursor.enter(parents, None)?;

                let source =
                    open_in_directory(parent, OsStr::new(leaf), SOURCE_FLAGS).map_err(|error| {
                        if error.kind() == std::io::ErrorKind::NotFound {
                            ArchiveRefusal::NotFound
                        } else {
                            ArchiveRefusal::UnsupportedEntry
                        }
                    })?;
                let metadata = source.metadata().map_err(|_| ArchiveRefusal::ReadFailed)?;
                if !metadata.is_file() {
                    return Err(ArchiveRefusal::UnsupportedEntry);
                }

                let size = metadata.len();
                budget.admit_bytes(size)?;
                let executable =
                    std::os::unix::fs::PermissionsExt::mode(&metadata.permissions()) & 0o111 != 0;
                sink.add_file(&name, executable, size, &mut source.take(size))?;
            }
        }
        written += 1;
    }

    Ok(written)
}
