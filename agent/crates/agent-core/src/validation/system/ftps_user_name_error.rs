//! Why an FTPS user name was refused.

/// Rejection reasons for [`super::ftps_user_name::FtpsUserName::for_account`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum FtpsUserNameError {
    /// Nothing was requested.
    #[error("ftps user name is empty")]
    Empty,
    /// A character outside `[a-z0-9]` was requested — the separator included, so
    /// a request cannot forge another account's prefix.
    #[error("ftps user name contains an unexpected character: {character:?}")]
    UnexpectedCharacter {
        /// The first offending character, so the operator log says which one.
        character: char,
    },
    /// The prefixed name exceeds the thirty-two byte `useradd` limit.
    #[error("ftps user name is {length} bytes, over the 32-byte useradd limit")]
    TooLong {
        /// The length of the prefixed name that was refused.
        length: usize,
    },
}
