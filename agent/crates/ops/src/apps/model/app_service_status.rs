//! What the service manager reports about an app's service.

/// What the service manager reports about an app's service.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AppServiceStatus {
    /// The service is running.
    Active,
    /// The service is installed and not running.
    Inactive,
    /// The service is in a failed state.
    Failed,
    /// The app has no service: Node.js is an interpreter, and a site's own unit is what runs.
    None,
}
