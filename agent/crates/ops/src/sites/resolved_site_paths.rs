//! The one place a site's paths are derived AND proved.

use maran_agent_core::validation::domain::Domain;
use maran_agent_core::validation::name::AccountName;

use crate::sites::model::site_paths::SitePaths;
use crate::sites::{SiteHost, SitesOpError};

/// Derives every path for `domain` under `account` and replaces the document
/// root with the canonical one the filesystem reports.
///
/// Every operation in the area calls this, and that is the point rather than
/// tidiness. `create_site` rendered the RESOLVED root while `enable_site` and
/// `disable_site` rendered the NAMED one, which is the same text on an
/// ordinary host and different text as soon as it is not — `/home` a symlink,
/// or a bind-mounted home layout, both perfectly normal. The two renderings
/// then differ, the `current == contents` comparison that makes enable and
/// disable idempotent never matches, and every retry rewrites the vhost and
/// reloads nginx: precisely the reload storm the comparison exists to prevent.
///
/// # Errors
///
/// Returns [`SitesOpError::UnsafeDocumentRoot`] when the document root does
/// not exist or resolves outside the account's home.
pub(crate) fn resolved_site_paths(
    host: &dyn SiteHost,
    account: &AccountName,
    domain: &Domain,
) -> Result<SitePaths, SitesOpError> {
    let mut paths = SitePaths::for_site(account, domain);
    paths.document_root =
        host.resolve_in_account_home(account, &SitePaths::document_root_in_home(domain))?;

    Ok(paths)
}
