//! Why the admission service turned a fleet request away.

/// The reason a request from a fleet connection was refused, for the operator's log.
///
/// Never sent to the peer: the peer is told "not admitted" and nothing more, so a scanner learns
/// nothing about whether a pin exists.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum FleetAdmissionRefusal {
    /// No panel certificate is pinned, so no network peer is admitted.
    #[error("no panel certificate is pinned")]
    NothingPinned,
    /// The connection carries no peer certificate. The handshake refuses such a client, so this
    /// is a defence against a router wired to a connection that skipped it.
    #[error("the connection presented no certificate")]
    NoCertificate,
    /// The connection presented a certificate that is not the pinned one: a pin replaced after
    /// the handshake, or a different panel.
    #[error("the presented certificate is not the pinned panel certificate")]
    CertificateNotPinned,
}
