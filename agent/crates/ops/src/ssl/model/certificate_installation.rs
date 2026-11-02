//! What a domain's certificate store entry amounts to.

/// What is installed for a domain, as one of the three states it can be in.
///
/// One value and not a `present` flag beside an `is_self_signed_placeholder`
/// flag: a placeholder that is not installed is not a thing, and a pair of
/// booleans could say it. Here the fourth combination cannot be written down.
///
/// "Installed" means BOTH halves, the chain and the private key, are there:
/// every caller points a TLS daemon at the two paths, and a daemon handed a
/// certificate whose key is missing refuses to start.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CertificateInstallation {
    /// At least one half of the material is missing, so no daemon can serve it.
    Absent,
    /// Both halves are installed and the marker file beside them says they are
    /// one of this agent's own self-signed placeholders, the certificate a
    /// customer's client will warn about.
    SelfSignedPlaceholder,
    /// Both halves are installed and nothing marks them as a placeholder.
    Installed,
}

impl CertificateInstallation {
    /// Whether both halves of the material are installed, a placeholder or not.
    #[must_use]
    pub fn is_present(self) -> bool {
        !matches!(self, Self::Absent)
    }

    /// Whether the installed material is one of the agent's own placeholders.
    #[must_use]
    pub fn is_self_signed_placeholder(self) -> bool {
        matches!(self, Self::SelfSignedPlaceholder)
    }
}
