//! One virtual host, read and validated.

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::web::domain::Domain;

use super::source_site_backend::SourceSiteBackend;

/// A virtual host whose names are domains and whose document root is a remote
/// path, not yet attributed to an account.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParsedVhost {
    /// The first host name that validated.
    pub primary: Domain,
    /// The other names that validated, without duplicates.
    pub aliases: Vec<Domain>,
    /// The document root, or `None` when the virtual host names none.
    pub document_root: Option<RemotePath>,
    /// What serves the site.
    pub backend: SourceSiteBackend,
    /// The PHP version as `major.minor` when the configuration names it.
    pub php_version: Option<String>,
}
