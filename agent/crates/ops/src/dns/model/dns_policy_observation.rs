//! A three-valued reading of one property of the live daemon configuration.

/// Whether a property the panel promises holds in the configuration the daemon
/// is started against.
///
/// Three values and not a `bool`, because the question has three honest
/// answers: the configuration states the property, it states the opposite, or it
/// could not be read at all. Folding the last into `false` would report an
/// unreadable file as a security finding, and folding it into `true` would
/// certify a configuration nobody read.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DnsPolicyObservation {
    /// The configuration could not be read, so nothing is certified.
    NotObserved,
    /// The configuration states the property the panel promises.
    InForce,
    /// The configuration was read and does not state it, or states the opposite.
    NotInForce,
}
