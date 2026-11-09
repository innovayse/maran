//! An account's inode quota in the form `setquota(8)` takes it.

use maran_agent_core::validation::system::inode_limit::InodeLimit;

use crate::accounts::model::resource_ceiling::ResourceCeiling;

/// The inode half of an account's disk quota.
///
/// A type beside [`super::quota_blocks::QuotaBlocks`] for the same reason: the
/// panel states a ceiling (`ResourceCeiling`, where "no limit" is a case and
/// never a number) and the tool takes a figure where `0` means no limit. This is
/// the one place the two meet, so the spelling of "unlimited" for `setquota` is
/// not repeated at a call site.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) struct QuotaInodes(ResourceCeiling<InodeLimit>);

impl QuotaInodes {
    /// Wraps the ceiling a caller stated.
    #[must_use]
    pub(super) fn new(ceiling: ResourceCeiling<InodeLimit>) -> Self {
        Self(ceiling)
    }

    /// The figure as `setquota` expects it on the command line, used for the
    /// soft and the hard limit alike.
    ///
    /// Digits only, from a validated integer or the tool's own spelling of "no
    /// limit": no newline, no option, no second argument (rules/security.md
    /// item 4).
    #[must_use]
    pub(super) fn as_argument(self) -> String {
        match self.0 {
            ResourceCeiling::Unlimited => InodeLimit::UNLIMITED_ARGUMENT.to_owned(),
            ResourceCeiling::Limited(limit) => limit.as_argument(),
        }
    }
}

#[cfg(test)]
#[path = "../tests/accounts/quota_inodes_tests.rs"]
mod tests;
