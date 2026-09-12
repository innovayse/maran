//! The numeric identity a restored home is allowed to end up owned by.

/// An account's numeric identity, as the host's password database answers it.
///
/// A named pair rather than a `(u32, u32)`, because the two numbers are
/// interchangeable to the compiler and are not interchangeable to `chown`: a
/// transposition would hand a customer's home to a group id read as a user id,
/// and nothing in the type system would have objected.
///
/// It exists so that a restore can ASK THE SAME QUESTION TWICE and compare the
/// answers. A restore resolves the account's ids at its start and may run for
/// hours; the value it read is a fact about a moment, not a fact about the
/// account, and `chown` at the end of an hour-long operation is where that
/// distinction stops being academic.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AccountIdentity {
    /// The account's user id.
    pub uid: u32,

    /// The account's own group id — the group it was created with, not the web
    /// server's group the finished home is handed to.
    pub gid: u32,
}
