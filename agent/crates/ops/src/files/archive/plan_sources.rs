//! Measuring the sources of an archive before anything is written.

use std::ffi::OsStr;
use std::fs::File;
use std::io;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;

use super::archive_refusal::ArchiveRefusal;
use super::common_parent_length::common_parent_length;
use super::directory_cursor::DirectoryCursor;
use super::expansion_budget::ExpansionBudget;
use super::member_kind::MemberKind;
use super::planned_entry::PlannedEntry;
use super::source_plan::SourcePlan;

/// Walks every source and lists what the archive will hold, or refuses the request.
///
/// This is the half of a creation that decides WHETHER, and it runs before the
/// archive file exists. Everything is judged by `fstatat` with
/// `AT_SYMLINK_NOFOLLOW`, so a link is seen as a link and never opened:
///
/// - a symbolic link, FIFO, socket or device anywhere in a source refuses the
///   WHOLE request ([`ArchiveRefusal::UnsupportedEntry`]) instead of being
///   skipped, because an archive that quietly lacks what the customer selected
///   is a backup that fails on the day it is needed;
/// - a name an archive cannot hold (a backslash, a control character, bytes that
///   are not UTF-8) refuses it the same way;
/// - more entries than the request allowed is [`ArchiveRefusal::EntryLimit`], and
///   a tree deeper than a name may be is [`ArchiveRefusal::TooDeep`].
///
/// Sizes are NOT measured here; the write pass counts the bytes it reads and
/// stops at the cap, so the figure cannot drift from what is archived. Names are
/// visited in sorted order, so two runs over the same tree write the same
/// archive. A directory is walked from a descriptor chain that never leaves the
/// home and follows no link, and holds no more than one chain open at a time.
///
/// # Errors
///
/// The refusals above, [`ArchiveRefusal::NotFound`] for an absent source, and
/// [`ArchiveRefusal::DirectoryUnusable`] for a level that cannot be descended.
pub(crate) fn plan_sources(
    home: &File,
    uid: u32,
    sources: &[BrowsePath],
    bounds: &CreateBounds,
) -> Result<SourcePlan, ArchiveRefusal> {
    let strip = common_parent_length(sources);
    let root = home.try_clone().map_err(|_| ArchiveRefusal::HomeUnusable)?;
    let mut cursor = DirectoryCursor::new(root, uid);
    let mut budget = ExpansionBudget::for_creation(bounds);
    let mut entries: Vec<PlannedEntry> = Vec::new();

    for source in sources {
        let components: Vec<&str> = source.components().iter().map(String::as_str).collect();
        let (leaf, parents) = components
            .split_last()
            .ok_or(ArchiveRefusal::UnsupportedEntry)?;
        let parent = cursor.enter(parents, None)?;
        let kind = member_kind_of(stat_in_directory(parent, OsStr::new(leaf)))?;

        budget.admit_entry()?;
        let path = components.join("/");
        entries.push(PlannedEntry {
            path: path.clone(),
            kind,
        });

        if kind == MemberKind::Directory {
            list_tree(&mut cursor, path, bounds, &mut budget, &mut entries)?;
        }
    }

    let plan = SourcePlan { entries, strip };
    // Every name is parsed here as well as at write time, so a name an archive
    // cannot hold refuses the request before the archive file exists.
    for entry in &plan.entries {
        plan.name_of(entry)?;
    }
    Ok(plan)
}

/// Lists the tree below `root`, directories before what they hold.
///
/// # Errors
///
/// As [`plan_sources`].
fn list_tree(
    cursor: &mut DirectoryCursor,
    root: String,
    bounds: &CreateBounds,
    budget: &mut ExpansionBudget,
    entries: &mut Vec<PlannedEntry>,
) -> Result<(), ArchiveRefusal> {
    let limit = usize::try_from(bounds.max_entries().saturating_add(2)).unwrap_or(usize::MAX);
    let mut pending = vec![root];

    while let Some(directory_path) = pending.pop() {
        let components: Vec<&str> = directory_path.split('/').collect();
        let directory = cursor.enter(&components, None)?;

        let listing = list_directory_names(directory, limit)
            .map_err(|_| ArchiveRefusal::DirectoryUnusable)?;
        if listing.exceeded {
            return Err(ArchiveRefusal::EntryLimit);
        }

        let mut names = Vec::with_capacity(listing.names.len());
        for raw in &listing.names {
            let raw: &OsStr = raw.as_ref();
            if raw == OsStr::new(".") || raw == OsStr::new("..") {
                continue;
            }
            let text = raw.to_str().ok_or(ArchiveRefusal::UnsupportedEntry)?;
            names.push(text.to_owned());
        }
        names.sort_unstable();

        for name in names {
            let kind = match stat_in_directory(directory, OsStr::new(&name)) {
                // Gone since it was listed: a race the customer won, not an entry.
                Err(error) if error.kind() == io::ErrorKind::NotFound => continue,
                other => member_kind_of(other)?,
            };

            budget.admit_entry()?;
            let path = format!("{directory_path}/{name}");
            if kind == MemberKind::Directory {
                pending.push(path.clone());
            }
            entries.push(PlannedEntry { path, kind });
        }
    }

    Ok(())
}

/// The kind of an entry a creation accepts, or the refusal for anything else.
fn member_kind_of(
    stat: io::Result<maran_agent_core::privs::entry_stat::EntryStat>,
) -> Result<MemberKind, ArchiveRefusal> {
    match stat {
        Ok(stat) => match stat.kind {
            EntryKind::File => Ok(MemberKind::File),
            EntryKind::Directory => Ok(MemberKind::Directory),
            EntryKind::Symlink | EntryKind::Other => Err(ArchiveRefusal::UnsupportedEntry),
        },
        Err(error) if error.kind() == io::ErrorKind::NotFound => Err(ArchiveRefusal::NotFound),
        Err(_) => Err(ArchiveRefusal::DirectoryUnusable),
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/plan_sources_tests.rs"]
mod tests;
