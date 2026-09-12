//! The one place a site's paths are derived AND proved.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

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
/// It also ENSURES the site's root-owned log directory exists, and that is the
/// second reason every operation goes through here rather than through
/// `SitePaths::for_site`. `nginx -t` **opens** the `error_log` target, so a
/// vhost naming a directory that is not there is refused by the validator and
/// the whole operation fails with it. Six operations render a vhost —
/// `create_site`, `enable_site`, `disable_site`, `update_site_php_version`,
/// `install_certificate`, `remove_certificate` — and putting the creation in
/// the one function all six already call is what stops it being six chances to
/// forget one. It is idempotent, root-owned and `0750`
/// (`SiteHost::create_site_log_directory`), so calling it on the read-only path
/// `inspect_account_sites` takes costs an empty directory and no risk: nothing
/// under `/var/log/maran/sites` is reachable by any uid but root.
///
/// # Errors
///
/// Returns [`SitesOpError::UnsafeDocumentRoot`] when the document root does
/// not exist or resolves outside the account's home, and
/// [`SitesOpError::LogDirectory`] when the log directory cannot be created.
pub(crate) fn resolved_site_paths(
    host: &dyn SiteHost,
    account: &AccountName,
    domain: &Domain,
) -> Result<SitePaths, SitesOpError> {
    let mut paths = SitePaths::for_site(account, domain);
    paths.document_root =
        host.resolve_in_account_home(account, &SitePaths::document_root_in_home(domain))?;

    host.create_site_log_directory(account)?;

    Ok(paths)
}
