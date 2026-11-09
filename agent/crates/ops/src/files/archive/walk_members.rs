//! Walking the members of an archive, whichever format it is.

use std::fs::File;

use super::archive_refusal::ArchiveRefusal;
use super::data_access::DataAccess;
use super::expansion_budget::ExpansionBudget;
use super::member_visitor::MemberVisitor;
use super::walk_tar_gz_members::walk_tar_gz_members;
use super::walk_zip_members::walk_zip_members;
use crate::files::model::archive_format::ArchiveFormat;

/// Hands every member of `file` to `visit`, judged and counted.
///
/// The one place the format is chosen, so the first pass (which only judges) and
/// the second (which writes) cannot read an archive differently: both go through
/// the same walk, the same name parser, the same registry and the same budget
/// logic. The second pass does not trust the first. The file belongs to the
/// customer, who may change it between the two, so everything the first pass
/// established is established again by the second.
///
/// # Errors
///
/// Whatever the format's walk refuses with, and whatever `visit` returns.
pub(crate) fn walk_members(
    file: &File,
    length: u64,
    format: ArchiveFormat,
    access: DataAccess,
    budget: &mut ExpansionBudget,
    visit: &mut MemberVisitor<'_>,
) -> Result<(), ArchiveRefusal> {
    match format {
        ArchiveFormat::Zip => walk_zip_members(file, length, access, budget, visit),
        ArchiveFormat::TarGz => walk_tar_gz_members(file, access, budget, visit),
    }
}
