//! What is done to an app's service.

/// What [`crate::apps::control_app_service`] does to the service.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AppServiceAction {
    /// Starts the service.
    Start,
    /// Stops the service.
    Stop,
    /// Stops and starts the service.
    Restart,
    /// Asks the running service to re-read its configuration.
    Reload,
}

impl AppServiceAction {
    /// The `systemctl` subcommand of the action: one fixed word per variant, never a caller's text.
    #[must_use]
    pub fn subcommand(self) -> &'static str {
        match self {
            Self::Start => "start",
            Self::Stop => "stop",
            Self::Restart => "restart",
            Self::Reload => "reload",
        }
    }
}
