//! Whose hand wrote the database dumps inside an archive.

/// Whose hand wrote the SQL dumps inside an archive, and therefore how they may
/// be loaded (issue #127).
///
/// Read from the sidecar's `imported` flag, the agent's own record of how the
/// artifact got here, and never from the archive: a sender controls the
/// manifest inside the archive and nothing in it can say that a dump is
/// trustworthy.
///
/// The two values are the two ways a dump reaches the server. A dump this agent
/// wrote is loaded by the database superuser, which is safe only because the
/// text is this agent's own output. A dump someone else wrote is scanned and
/// loaded by a temporary user that holds one database
/// ([`load_dump_restricted`](crate::backup::load_dump_restricted)) or it is not
/// loaded at all; there is no value for "imported, but loaded with more".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum DumpOrigin {
    /// This host made the artifact. Its dumps are loaded as the database
    /// superuser.
    Own,

    /// The artifact was received from another host. Its dumps are text from
    /// someone else and are only ever loaded through the restricted loader.
    Imported,
}
