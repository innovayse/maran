//! The certificate material a site's TLS block points at.

use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::domain::Domain;

/// Where a site's installed certificate and private key are on disk.
///
/// Present on [`super::create_site_input::CreateSiteInput`] rather than being
/// discovered while rendering, because whether a site has a certificate is a
/// fact the caller knows and the renderer must not guess: rendering a TLS
/// block for a certificate that is not there yet produces a config `nginx -t`
/// rejects, and skipping one for a certificate that is there silently drops
/// the site back to plain HTTP on the next unrelated edit.
///
/// The paths are DERIVED from the domain and cannot be supplied. They are a
/// pure function of it — the agent owns
/// [`AgentPaths::CERTIFICATE_DIRECTORY`] and puts every certificate it obtains
/// in the same place — so a settable field would be freedom with no use for
/// it, and every use of that freedom would be a caller-chosen string reaching
/// `ssl_certificate {{ … }};` in a root-written config. There is no
/// constructor but [`SiteCertificate::for_domain`], so the type is a promise
/// that both paths are inside the agent's own directory and named after a
/// validated `Domain`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SiteCertificate {
    /// Absolute path of the full certificate chain.
    certificate_path: PathBuf,
    /// Absolute path of the private key.
    key_path: PathBuf,
}

impl SiteCertificate {
    /// The certificate material for `domain`, in the agent's own directory.
    #[must_use]
    pub fn for_domain(domain: &Domain) -> Self {
        let directory = PathBuf::from(AgentPaths::CERTIFICATE_DIRECTORY).join(domain.as_str());

        Self {
            certificate_path: directory.join("fullchain.pem"),
            key_path: directory.join("privkey.pem"),
        }
    }

    /// Absolute path of the full certificate chain.
    #[must_use]
    pub fn certificate_path(&self) -> &Path {
        &self.certificate_path
    }

    /// Absolute path of the private key.
    #[must_use]
    pub fn key_path(&self) -> &Path {
        &self.key_path
    }
}
