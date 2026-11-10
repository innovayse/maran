//! The two programs a Node.js site may be started with.

/// The program a site's start command begins with.
///
/// Closed: the unit's `ExecStart` names an absolute path, and that path comes from the
/// distribution adapter keyed by this value, never from the caller's text.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NodeProgram {
    /// The Node.js interpreter, running a script of the site.
    Node,
    /// The npm launcher, running a script of the site's `package.json`.
    Npm,
}
