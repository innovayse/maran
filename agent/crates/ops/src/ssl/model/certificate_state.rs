//! What the agent knows about the certificate material installed for a domain.

/// The two facts a caller can learn about a domain's certificate material
/// without reading a single byte of it: whether it is there, and whether it is
/// one of this agent's own self-signed placeholders.
///
/// Written forward from the question a caller actually has — *"may I point a
/// daemon at this, and will the customer's client warn about it?"* — rather
/// than from what a certificate contains. Nothing here describes the
/// certificate's content, and that is the design and not an omission: see
/// [`certificate_state`](crate::ssl::certificate_state) for the incident that
/// fixed it that way.
///
/// The paths are carried even when nothing is installed, because a refusal that
/// cannot say WHERE the material would have to be is a refusal an operator
/// cannot act on. They are derived by
/// [`SiteCertificate::for_domain`](crate::sites::SiteCertificate::for_domain)
/// from a validated `Domain` and are therefore always inside the agent's own
/// store.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CertificateState {
    /// Absolute path of the full certificate chain, whether or not it exists.
    pub certificate_path: String,
    /// Absolute path of the private key, whether or not it exists.
    pub private_key_path: String,
    /// Whether BOTH halves of the material are installed.
    ///
    /// Both, and not the certificate alone: every caller of this type points a
    /// TLS daemon at the two paths above, and a daemon handed a certificate
    /// whose key is missing refuses to start. A `present` that answered for the
    /// certificate alone would be a check that cannot observe what it reports
    /// on (rules/testing.md) — it would report "there is material" for a state
    /// in which no daemon can serve any.
    pub present: bool,
    /// Whether the installed material is one of the agent's own self-signed
    /// placeholders — the certificate a customer's client will warn about.
    ///
    /// Decided by the presence of the marker FILE beside the material and by
    /// nothing else. False whenever [`Self::present`] is false: there is no
    /// material to describe, and answering anything else about absent bytes is
    /// an invention.
    pub is_self_signed_placeholder: bool,
}
