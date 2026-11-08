//! What a walk hands each member to.

use std::io::Read;

use super::archive_member::ArchiveMember;
use super::archive_refusal::ArchiveRefusal;

/// The callback of a walk: one member and its bytes (empty when the walk was
/// asked to skip them). Returning a refusal ends the walk with it.
pub(crate) type MemberVisitor<'a> =
    dyn FnMut(&ArchiveMember, &mut dyn Read) -> Result<(), ArchiveRefusal> + 'a;
