//! One site found on a source.

use maran_agent_core::validation::web::domain::Domain;

use super::source_site_backend::SourceSiteBackend;

/// One site of an account, every name validated.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SourceSiteRecord {
    /// The primary host name.
    pub domain: Domain,
    /// The other host names the virtual host answers to.
    pub aliases: Vec<Domain>,
    /// What serves the site.
    pub backend: SourceSiteBackend,
    /// The PHP version as `major.minor`, when known.
    pub php_version: Option<String>,
    /// The document root on the source, display only; it already passed the
    /// remote-path grammar and is never used as a path on this host.
    pub document_root: String,
}
