//! The one host a database login of this panel may connect from.

/// The host part of every login's account, `'name'@'localhost'`.
///
/// Local only. A login that may connect from anywhere is a login that can be
/// brute-forced from anywhere, and nothing this panel hosts reaches the database
/// over the network. The users operations restate no literal of their own: every
/// statement they build, and the existence check they share, name this one.
pub(crate) const USER_HOST: &str = "localhost";
