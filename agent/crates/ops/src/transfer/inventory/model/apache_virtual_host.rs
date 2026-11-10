//! What one Apache `VirtualHost` section says that an inventory needs.

use super::source_site_backend::SourceSiteBackend;

/// The facts of one `VirtualHost` section, unvalidated.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ApacheVirtualHost {
    /// The first `ServerName`, quotes and port removed.
    pub server_name: Option<String>,
    /// The words of every `ServerAlias`, quotes removed.
    pub aliases: Vec<String>,
    /// The first `DocumentRoot`, quotes removed.
    pub document_root: Option<String>,
    /// The strongest backend any directive of the section shows.
    pub backend: SourceSiteBackend,
    /// The PHP version a handler line names.
    pub php_version: Option<String>,
}
