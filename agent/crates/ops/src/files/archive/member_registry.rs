//! The names an archive has used, so no two members can collide.

use std::collections::HashMap;

use super::archive_member::ArchiveMember;
use super::archive_refusal::ArchiveRefusal;
use super::member_kind::MemberKind;

/// Every name and every directory prefix seen so far, with what it is.
///
/// Two members that share a name, or a member written below a name another
/// member made a file, are how an archive makes the writer's second pass behave
/// differently from the first, so the walk refuses both as it goes. A directory
/// named twice, or named after files were written into it implicitly, is not a
/// collision: both lead to the same directory.
#[derive(Debug, Default)]
pub(crate) struct MemberRegistry {
    /// What each path (a member's own, or an implied parent) is.
    seen: HashMap<String, MemberKind>,
}

impl MemberRegistry {
    /// An empty registry.
    pub(crate) fn new() -> Self {
        Self::default()
    }

    /// Records `member`, or refuses it.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::FileAsDirectory`] when a parent of the member, or the
    /// member itself as a directory, is already a file, and
    /// [`ArchiveRefusal::DuplicateMember`] when a file's name is already taken.
    pub(crate) fn admit(&mut self, member: &ArchiveMember) -> Result<(), ArchiveRefusal> {
        let name = member.name.as_str();

        // Every proper prefix is a directory: a file there cannot hold the member.
        for (index, _) in name.match_indices('/') {
            let prefix = &name[..index];
            match self.seen.get(prefix) {
                Some(MemberKind::File) => return Err(ArchiveRefusal::FileAsDirectory),
                Some(MemberKind::Directory) => {}
                None => {
                    self.seen.insert(prefix.to_owned(), MemberKind::Directory);
                }
            }
        }

        match (self.seen.get(name), member.kind) {
            (None, kind) => {
                self.seen.insert(name.to_owned(), kind);
                Ok(())
            }
            (Some(MemberKind::Directory), MemberKind::Directory) => Ok(()),
            (Some(MemberKind::File), MemberKind::Directory)
            | (Some(MemberKind::Directory), MemberKind::File) => {
                Err(ArchiveRefusal::FileAsDirectory)
            }
            (Some(MemberKind::File), MemberKind::File) => Err(ArchiveRefusal::DuplicateMember),
        }
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/member_registry_tests.rs"]
mod tests;
