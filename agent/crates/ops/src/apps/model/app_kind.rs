//! What an app is for.

/// What an app is for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AppKind {
    /// The process that owns ports 80 and 443 for hosted sites.
    WebServer,
    /// A language runtime sites run on.
    Runtime,
}
