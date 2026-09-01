//! Why a caller-supplied permission mode was refused.

/// Rejection reasons for [`super::file_mode::FileMode::parse`].
///
/// Text here is for the operator log. The variant carries no number: the caller
/// sent the mode and already has it, and a message quoting it adds nothing an
/// operator can act on (rules/rust.md "Errors").
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum FileModeError {
    /// The mode carried a bit that is not a plain permission bit: setuid,
    /// setgid or the sticky bit.
    ///
    /// Refused and never masked away. Masking would carry out a request the
    /// caller did not make and report success for it, and the request that
    /// reaches here — "create a setuid file in a customer's home, as root" — is
    /// one whose author should be told it was refused.
    #[error("the mode is not a plain permission mode")]
    NotAPlainPermissionMode,
}
