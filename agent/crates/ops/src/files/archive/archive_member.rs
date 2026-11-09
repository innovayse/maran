//! One member of an archive that passed every check of the read.

use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;

use super::member_kind::MemberKind;

/// A member as the walk hands it to its visitor.
///
/// Nothing in it came from the archive crate's own path handling: the name was
/// built from the raw bytes by [`ArchiveMemberName::parse`], and the kind was
/// decided here from the entry type or the unix mode, never from a name's
/// trailing separator alone.
#[derive(Debug, Clone)]
pub(crate) struct ArchiveMember {
    /// Where the member goes, below the destination.
    pub(crate) name: ArchiveMemberName,
    /// File or directory.
    pub(crate) kind: MemberKind,
    /// Whether the archive set any execute bit; the only part of the archive's
    /// mode that is kept (files become 0644 or 0755).
    pub(crate) executable: bool,
    /// The size the archive's header declares; zero for a directory. Counted
    /// against the byte cap before anything is written, and checked again
    /// against what is actually produced.
    pub(crate) declared_size: u64,
}
