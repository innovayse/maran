//! CreateSite: the first operation that changes a customer's server.

use maran_distro::DistroAdapter;

use crate::php::PhpHost;
use crate::php::model::php_override::PhpOverride;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::created_site::CreatedSite;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::render_vhost::render_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::write_site_pool::write_site_pool;
use crate::sites::write_vhost::write_vhost;
use crate::sites::{SiteHost, SitesOpError};

/// Creates a site: its document root inside the account's home, its php-fpm
/// pool when it is PHP-backed, and its vhost in the directory the agent owns.
///
/// `max_children` is the owning account's plan worker budget and `overrides`
/// are its whitelisted php.ini settings; both are the panel's to supply and
/// both are ignored for a site that is not PHP-backed, which owns no pool.
///
/// Idempotent in the sense the contract requires (rules/rust.md
/// "Idempotency"): a domain that is already configured is reported as
/// [`SitesOpError::AlreadyExists`] and NOTHING is rewritten. That is not
/// politeness about a duplicate request — the panel retries after a timeout,
/// and a retry that re-rendered the vhost would drop the TLS block an
/// unrelated certificate installation had added, or replace a document root
/// somebody already has traffic on.
///
/// The document root is created by a process that has dropped to the account
/// (rules/security.md: *direct `std::fs` on customer paths as root is
/// forbidden*), and only then proved to be inside that account's home — a
/// symlink planted where the directory was going passes any check made against
/// the path text, and fails the one made against the filesystem.
///
/// # Errors
///
/// Returns [`SitesOpError::AlreadyExists`] when a vhost for the domain is
/// already present, and [`SitesOpError::ConfigUnreadable`] when one is present
/// but unreadable — which is deliberately not treated as "no site here",
/// because that reading would overwrite a live vhost. Returns
/// [`SitesOpError::DocumentRoot`] when the directories cannot be created as
/// the account and [`SitesOpError::UnsafeDocumentRoot`] when the created root
/// resolves outside the home. Returns [`SitesOpError::Render`] when a template
/// fails, [`SitesOpError::PhpVersionNotInstalled`] when a PHP site names a
/// version this host does not have — refused before any vhost is written, so
/// nothing is left half-created — [`SitesOpError::NginxValidation`] when
/// `nginx -t` rejects the result
/// — with no vhost left behind — [`SitesOpError::ReloadFailed`] when the
/// reload refuses it, and [`SitesOpError::ConfigWrite`] for any other failure
/// of the write protocol.
pub fn create_site(
    host: &dyn SiteHost,
    php_host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    input: &CreateSiteInput,
    max_children: u32,
    overrides: &[PhpOverride],
) -> Result<CreatedSite, SitesOpError> {
    // Named, not yet resolved: the document root does not exist until the
    // block below creates it, and a path can only be proved contained once it
    // is there.
    let named = SitePaths::for_site(&input.account, &input.domain);

    if host.read_config(&named.config_path)?.is_some() {
        return Err(SitesOpError::AlreadyExists {
            domain: input.domain.as_str().to_owned(),
        });
    }

    // Both directories, in one drop to the account: the log directory is as
    // much a customer path as the document root, and nginx will open the log
    // files it names before it serves the first request.
    host.create_directories_as_account(
        &input.account,
        &[&named.document_root, &named.log_directory],
    )?;

    // Now that it exists, ask the filesystem where it really is, and render
    // the vhost with THAT path rather than with the one we asked for — through
    // the same derivation `enable_site` and `disable_site` use, so all three
    // render byte-identical text for the same site.
    let paths = resolved_site_paths(host, &input.account, &input.domain)?;

    // The pool BEFORE the vhost, and for the same reason a version switch
    // writes it first: the vhost about to be rendered names a socket by path,
    // and a vhost that reaches a live nginx before anything has bound that
    // socket answers every request with a 502. Only a PHP-backed site has a
    // pool — a static or reverse-proxied one names no `fastcgi_pass` at all —
    // so the other two kinds skip it rather than reloading a php-fpm master for
    // a site that never speaks to one.
    //
    // Until this call existed, `update_site_php_version` was the only writer of
    // a pool anywhere in the agent, which meant a PHP site that was created and
    // never switched to another version never worked at all: its vhost pointed
    // at a socket nothing had bound, and the panel reported the creation as a
    // success. Switching the site to a different version and back was the only
    // way to make it serve.
    if let SiteKind::Php { version } = &input.kind {
        write_site_pool(php_host, distro, input, version, max_children, overrides)?;
    }

    let contents = render_vhost(input, &paths)?;
    write_vhost(host, distro, &paths.config_path, &contents)?;

    Ok(CreatedSite {
        document_root: paths.document_root.display().to_string(),
        config_path: paths.config_path.display().to_string(),
    })
}

#[cfg(test)]
#[path = "../tests/sites/create_site_tests.rs"]
mod tests;
