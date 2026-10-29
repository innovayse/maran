//! Why a name was refused as a managed hosting account.

/// Rejection reasons for [`super::managed_account::ManagedAccount`].
///
/// The variants exist so a caller can tell the refusals apart and for the wire
/// mapping; only the accounts area logs one. None of
/// them carries a path, an id or a rule, and the customer sees none of the
/// wording: naming which check failed would describe the shape of the rules to
/// whoever is probing them.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ManagedAccountError {
    /// The name is on the reserved list, or starts with the panel's own prefix.
    ///
    /// Raised before any host fact is read, so a reserved name costs no lookup
    /// and cannot be used to ask whether such a user exists.
    #[error("account name is reserved")]
    Reserved,
    /// The local password database has no usable row of exactly that name, or
    /// has more than one.
    ///
    /// A row that could not be read as a passwd line counts as absent, never as
    /// a pass. A user served only by a name service is absent here too.
    #[error("no such account in the local password database")]
    NotFound,
    /// The row's user id or primary group id is 0 or below the host's floor for
    /// a human account, which makes it a system identity.
    #[error("account has a system identity")]
    SystemIdentity,
    /// The row's home is not exactly `/home/<name>`.
    ///
    /// This is also what refuses an SFTP or FTPS login: it parses as an account
    /// name and shares its account's ids, but its home is a jail.
    #[error("account home is not the managed home")]
    WrongHome,
}
