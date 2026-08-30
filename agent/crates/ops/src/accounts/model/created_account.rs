//! The result of creating an account.

/// What creating an account produced.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CreatedAccount {
    /// The absolute home directory that was created.
    pub home_directory: String,
    /// The numeric uid the system assigned.
    pub uid: u32,
}
