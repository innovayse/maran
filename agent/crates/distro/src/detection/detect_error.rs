//! Why distribution detection refused to proceed.

/// Reasons [`crate::detection::detect::detect`] fails.
///
/// Detection failing is a refusal to install, not a warning to work around: an
/// agent that guessed a family would run apt commands on a dnf host and leave a
/// half-configured server behind.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DetectError {
    /// `/etc/os-release` is missing or unreadable.
    #[error("cannot read /etc/os-release")]
    Unreadable,
    /// The distribution is outside the supported matrix (spec §4).
    #[error("unsupported distro: {id}")]
    Unsupported {
        /// The os-release ID that was refused; echoed so the operator learns
        /// which host they pointed the installer at.
        id: String,
    },
}
