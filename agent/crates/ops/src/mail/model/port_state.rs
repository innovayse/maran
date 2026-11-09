//! One mail port and how it is bound.

use crate::mail::model::port_binding::PortBinding;

/// The observed binding of one of the mail ports.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PortState {
    /// The port.
    pub port: u16,
    /// What the kernel reports for it.
    pub binding: PortBinding,
}
