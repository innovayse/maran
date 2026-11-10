//! The web server found on a source.

/// A web server a probe recognised. "None found, or the probe could not run"
/// is `None` in an `Option`, not a value of this enum.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SourceWebServer {
    /// nginx.
    Nginx,
    /// Apache.
    Apache,
}
