//! CreateSite: the first operation that changes a customer's server.

use maran_distro::DistroAdapter;

use crate::php::PhpHost;
use crate::php::model::php_override::PhpOverride;
use crate::sites::create_vhost::create_vhost;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::created_site::CreatedSite;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::render_vhost::render_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::write_site_pool::write_site_pool;
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
/// The site's two logs do NOT live in the home and their directory is created
/// as root, in `/var/log/maran/sites/<account>`. The web server's master
/// process runs as root and opens those files without `O_NOFOLLOW`, so a
/// directory the customer owned was a root compromise one symlink away; see
/// [`SitePaths`] and
/// `docs/superpowers/notes/2026-09-09-site-logs-threat-note.md`.
///
/// # Errors
///
/// Returns [`SitesOpError::RepeatedHostName`] when an alias equals the domain
/// or another alias, before anything is read or written.
/// Returns [`SitesOpError::AlreadyExists`] when a vhost for the domain is
/// already present, and [`SitesOpError::ConfigUnreadable`] when one is present
/// but unreadable — which is deliberately not treated as "no site here",
/// because that reading would overwrite a live vhost. Returns
/// [`SitesOpError::DocumentRoot`] when the directories cannot be created as
/// the account and [`SitesOpError::UnsafeDocumentRoot`] when the created root
/// resolves outside the home, and [`SitesOpError::LogDirectory`] when the
/// root-owned log directory cannot be created — which is fatal rather than
/// cosmetic, because `nginx -t` opens the error-log target. Returns [`SitesOpError::Render`] when a template
/// fails, [`SitesOpError::PhpVersionNotInstalled`] when a PHP site names a
/// version this host does not have — refused before any vhost is written, so
/// nothing is left half-created — [`SitesOpError::NginxValidation`] when
/// `nginx -t` rejects the result
/// — with no vhost left behind — [`SitesOpError::ServerNameTaken`] when the
/// domain or an alias is already served by another vhost on this host, or the
/// site's own vhost appeared while this creation was running — again with no
/// vhost left behind — [`SitesOpError::ReloadFailed`] when the
/// reload refuses it, and [`SitesOpError::ConfigWrite`] for any other failure
/// of the write protocol. Returns [`SitesOpError::Bound`] when `nginx -t` or the reload
/// outlived its deadline and was killed, again with no vhost left behind; a bound is no
/// verdict on the vhost, so it is never one of the refusals above.
///
/// A refusal at the vhost leaves what came before it: the document root, the
/// log directory and, for a PHP site, the account's pool for that version. None
/// of them serves a request on its own, the pool is shared by the account's
/// other sites on that version, and a repeated creation converges on all three.
pub fn create_site(
    host: &dyn SiteHost,
    php_host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    input: &CreateSiteInput,
    max_children: u32,
    overrides: &[PhpOverride],
) -> Result<CreatedSite, SitesOpError> {
    // Every host name the vhost will serve, the domain first. Asked about
    // twice: here, for a name the request itself repeats, and at the vhost,
    // for a name some other file on this host already serves.
    let served_names: Vec<&str> = std::iter::once(&input.domain)
        .chain(&input.aliases)
        .map(|name| name.as_str())
        .collect();

    // Before anything is read or written (rules/rust.md "Validation first"):
    // nginx words a name repeated inside one server block exactly as it words
    // a name two vhosts share, so a repetition left to the vhost check would be
    // refused there as another site's claim, with the document root already
    // created.
    if let Some(name) = first_repeated_name(&served_names) {
        return Err(SitesOpError::RepeatedHostName {
            name: name.to_owned(),
        });
    }

    // Named, not yet resolved: the document root does not exist until the
    // block below creates it, and a path can only be proved contained once it
    // is there.
    let named = SitePaths::for_site(&input.account, &input.domain);

    if host.read_config(&named.config_path)?.is_some() {
        return Err(SitesOpError::AlreadyExists {
            domain: input.domain.as_str().to_owned(),
        });
    }

    // The document root, and only the document root, in a drop to the account:
    // it is a customer path and root must not touch it. The site's LOG
    // directory used to be created in this same call, which is exactly what
    // made it the customer's — and the root nginx master then opened files in
    // it without `O_NOFOLLOW`. It is created as root, outside every home, by
    // `resolved_site_paths` below
    // (`docs/superpowers/notes/2026-09-09-site-logs-threat-note.md`).
    host.create_directories_as_account(&input.account, &[&named.document_root])?;

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

    // A creation, not a replacement: the vhost is introduced through the half
    // of the config-write protocol that refuses a target already there and
    // refuses `nginx -t`'s warning that one of the site's names is served by
    // another vhost — the warning nginx prints and then exits 0 on. The check
    // above is the idempotent answer for an ordinary retry; this one is the
    // answer under the config-tree lock, against the tree that holds every
    // other vhost, and it is what a concurrent creation naming one of the same
    // hosts meets (`create_vhost` says why the check is nginx's warning and not
    // a scan of the directory).
    let contents = render_vhost(input, &paths)?;
    create_vhost(host, distro, &paths.config_path, &contents, &served_names)?;

    Ok(CreatedSite {
        document_root: paths.document_root.display().to_string(),
        config_path: paths.config_path.display().to_string(),
    })
}

/// The first of `names` that appears earlier in `names` too, or `None` when
/// every name is distinct.
///
/// Exact comparison is the whole comparison: every name is a `Domain`, which
/// is lowercased at parse, so two spellings of one host cannot both survive to
/// here. Quadratic on purpose — a site's names are a handful, and a set would
/// say nothing a reader needs.
fn first_repeated_name<'a>(names: &[&'a str]) -> Option<&'a str> {
    names
        .iter()
        .enumerate()
        .find(|&(index, name)| names[..index].contains(name))
        .map(|(_, name)| *name)
}

#[cfg(test)]
#[path = "../tests/sites/create_site_tests.rs"]
mod tests;
