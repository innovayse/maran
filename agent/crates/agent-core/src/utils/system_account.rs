//! One row of the host's local password database.

/// A single account as the local password database records it.
///
/// The four fields anything in this repository asks a passwd row for. `gecos`
/// and the login shell are deliberately dropped rather than carried unread: a
/// field nobody reads is a field that goes wrong without anybody noticing, and
/// the file it came from is still there for whoever needs the rest.
///
/// The values are exactly what the file said, unvalidated on purpose. A row is
/// evidence about the host, not an input the agent is about to act on: the
/// name here may be a system user, a service account or something no
/// `AccountName` would accept, and it is the CALLER that decides which rows
/// are hosting accounts of its own. Validating here would silently drop the
/// very rows a caller is trying to tell apart.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SystemAccount {
    /// The login name — the first field of the row.
    pub name: String,
    /// The numeric user id.
    pub uid: u32,
    /// The numeric primary group id.
    pub gid: u32,
    /// The home directory, exactly as the row spells it.
    pub home: String,
}
