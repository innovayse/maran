//! The three answers the panel accepts about a managed unit.

/// What the agent found out about one managed unit.
///
/// Three values and not a `bool`, because a monitor that can only say
/// "running" or "stopped" has to invent one of them whenever it does not know —
/// and the invented answer is always the alarming one. The panel's alerting
/// mails an operator about whatever is called stopped, so [`Self::Stopped`] is
/// a claim this area makes only when it has evidence, and [`Self::Unknown`] is
/// what it says the rest of the time.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ServiceState {
    /// The unit is up: systemd reports it active, or actively reloading.
    Running,

    /// The unit is down and nothing is waiting to bring it up.
    ///
    /// The one state worth waking somebody for, which is exactly why it is not
    /// the fallback: it is reported for a unit systemd calls `failed`, and for
    /// an inactive unit that has no listening socket standing in for it.
    Stopped,

    /// The agent could reach the service manager but cannot call the unit
    /// either up or down.
    ///
    /// A socket-activated service that nothing has connected to yet, a unit
    /// mid-transition, and a unit that is not installed on this host all land
    /// here. None of them is an outage, and none of them is proof of health.
    Unknown,
}
