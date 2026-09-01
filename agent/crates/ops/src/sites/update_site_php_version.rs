//! UpdateSitePhpVersion: pointing a site at a different PHP version.

use maran_distro::DistroAdapter;

use crate::php::{PhpHost, is_installed};
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::php_switch::PhpSwitch;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::remove_site_pool::remove_site_pool;
use crate::sites::render_vhost::render_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::write_site_pool::write_site_pool;
use crate::sites::write_vhost::write_vhost;
use crate::sites::{SiteHost, SitesOpError};

/// Switches `input`'s site to `version`: ensures the new pool exists,
/// re-renders the vhost against the new socket, reloads, and — when the caller
/// says the account has no other site left on the old version — takes the old
/// pool away.
///
/// The order matters and is not the obvious one. The POOL is written first and
/// the vhost second, because between the two writes the site is briefly
/// pointed at the old socket while the new pool already listens — which serves
/// every request correctly. Reversed, the window is a vhost pointing at a
/// socket no master has bound yet, and every request in it is a 502.
///
/// `overrides` are the customer's current whitelisted settings, which the caller
/// re-supplies because the new version's pool is written from scratch: the old
/// version's pool file is a different file in a different directory and
/// nothing carries its contents forward.
///
/// Refuses a version that is not installed rather than installing it: the
/// contract makes that `VALIDATION_FAILED`, and installing takes minutes and
/// streams progress, so it is its own operation. Setting the version a site
/// already has is a success that writes nothing — the panel retries after a
/// timeout, and a reload of nginx and php-fpm per retry is a storm.
///
/// # Errors
///
/// Returns [`SitesOpError::NotFound`] when no vhost for the domain exists, and
/// [`SitesOpError::PhpVersionNotInstalled`] when `version` is absent from this
/// host. Returns [`SitesOpError::ConfigWrite`] carrying the PHP area's failure
/// when the pool cannot be written, and this area's usual
/// [`SitesOpError::NginxValidation`], [`SitesOpError::ReloadFailed`] and
/// [`SitesOpError::Render`] for the vhost half.
pub fn update_site_php_version(
    host: &dyn SiteHost,
    php_host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    input: &CreateSiteInput,
    switch: &PhpSwitch<'_>,
) -> Result<(), SitesOpError> {
    let PhpSwitch {
        version,
        max_children,
        overrides,
        remove_previous_pool,
    } = *switch;
    let paths = resolved_site_paths(host, &input.account, &input.domain)?;

    if host.read_config(&paths.config_path)?.is_none() {
        return Err(SitesOpError::NotFound {
            domain: input.domain.as_str().to_owned(),
        });
    }

    if !is_installed(php_host, distro, version) {
        return Err(SitesOpError::PhpVersionNotInstalled {
            version: version.as_str().to_owned(),
        });
    }

    // The no-op case, decided against the site's own record of what it is
    // rather than against a marker file beside it — the same way `enable_site`
    // and `disable_site` decide, so all three converge on retry.
    if input.kind
        == (SiteKind::Php {
            version: version.clone(),
        })
    {
        return Ok(());
    }

    write_site_pool(php_host, distro, input, version, max_children, overrides)?;

    let switched = CreateSiteInput {
        kind: SiteKind::Php {
            version: version.clone(),
        },
        ..input.clone()
    };
    let contents = render_vhost(&switched, &paths)?;

    write_vhost(host, distro, &paths.config_path, &contents)?;

    // LAST, and only after the vhost is on the new socket and nginx has
    // reloaded. The whole sequence is written so that the site is servable at
    // every instant: the new pool is bound before the vhost moves, and the old
    // pool is only taken away once nothing points at it. Removing it any
    // earlier — before the pool write, or between the pool write and the vhost
    // — is a window in which the live vhost names a dead socket.
    //
    // And only when the caller says so. A pool is shared by every site of this
    // account on the version being left behind, so removing it because THIS
    // site moved would take the account's other sites off the air. The panel
    // holds the rows that answer that question; see `remove_site_pool`.
    if remove_previous_pool && let SiteKind::Php { version: previous } = &input.kind {
        remove_site_pool(php_host, distro, &input.account, previous)?;
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sites/update_site_php_version_tests.rs"]
mod tests;
