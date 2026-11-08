//! The most files and directories an account may own on its home's filesystem.

use super::inode_limit_error::InodeLimitError;

/// The most files and directories an account may own: the inode half of its
/// disk quota.
///
/// It becomes the inode soft and hard limit of `setquota -u`, which counts in
/// 32-bit figures, so the largest accepted figure is `u32::MAX` and the wire's
/// `uint32` can never carry one the tool would refuse.
///
/// The inner value is private and [`InodeLimit::parse`] is the only
/// constructor, so holding one is proof the figure is in `1..=u32::MAX`. Zero is
/// refused rather than read as "no limit": `setquota` itself reads `0` as
/// unlimited, and an absent wire field decodes to `0`, so accepting it would
/// turn a caller that forgot the figure into an account with no ceiling. "No
/// limit" is a separate, typed answer the caller states on purpose (the
/// accounts area's `ResourceCeiling::Unlimited`), never a number, and
/// [`InodeLimit::UNLIMITED_ARGUMENT`] is the one place it is spelled for the
/// tool.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct InodeLimit(u32);

impl InodeLimit {
    /// How `setquota` is told "no inode limit": the figure `0`.
    ///
    /// Named here, beside the type that refuses zero, so the two facts that
    /// look contradictory sit on one page: the type refuses `0` because the
    /// tool would read it as unlimited, and the tool's argument for unlimited
    /// is exactly that `0`.
    pub const UNLIMITED_ARGUMENT: &'static str = "0";

    /// Validates `candidate` as an inode limit and wraps it.
    ///
    /// # Errors
    ///
    /// - [`InodeLimitError::Zero`] when `candidate` is `0`, which is what an
    ///   absent field decodes to and which `setquota` would read as unlimited.
    pub fn parse(candidate: u32) -> Result<Self, InodeLimitError> {
        if candidate == 0 {
            return Err(InodeLimitError::Zero);
        }

        Ok(Self(candidate))
    }

    /// The validated figure.
    #[must_use]
    pub fn value(&self) -> u32 {
        self.0
    }

    /// The figure as `setquota` takes it on the command line.
    ///
    /// Built from a validated integer, so the text is digits only: no
    /// newline, no option, no second argument (rules/security.md item 4).
    #[must_use]
    pub fn as_argument(&self) -> String {
        self.0.to_string()
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/inode_limit_tests.rs"]
mod tests;
