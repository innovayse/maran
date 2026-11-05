//! Whether the host needs a reboot to run what is already installed.

/// The answer of a reboot probe that ran to a conclusion (issue #163).
///
/// A probe that could not run is not a value of this type: it is a
/// [`crate::RebootProbeFailure`] or a failure of the spawn that ran it, and the
/// panel's "not measured" is built from that, never from `NotRequired`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RebootRequirement {
    /// The probe ran and found nothing that needs a reboot.
    NotRequired,
    /// The probe ran and found installed updates that only a reboot applies.
    Required,
}
