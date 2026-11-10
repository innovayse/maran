//! Bringing a PHP site's pool and vhost to what the agent renders today.

use maran_distro::DistroAdapter;

use crate::php::PhpHost;
use crate::php::model::pool_input::PoolInput;
use crate::php::model::pool_paths::PoolPaths;
use crate::php::render_pool::render_pool;
use crate::sites::is_suspended_vhost::is_suspended_vhost;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::php_switch::PhpSwitch;
use crate::sites::render_vhost::render_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::sync_site_auth_file::sync_site_auth_file;
use crate::sites::write_site_pool::write_site_pool;
use crate::sites::write_vhost::write_vhost;
use crate::sites::{SiteHost, SitesOpError};

/// Makes the pool and the vhost of a site already on `switch.version` equal to
/// what this agent renders for them now, writing only what differs.
///
/// This is the same-version meaning of `UpdateSitePhpVersion`, and it exists
/// because a site written by an earlier release keeps whatever that release
/// rendered — a pool listening on, and a vhost naming, a socket path the
/// current release no longer uses — until something rewrites it. Setting the
/// version a site already has used to be a pure no-op, so nothing ever did.
///
/// Each file is decided independently, by rendering the text and comparing it
/// byte for byte with the file on disk: the pool through
/// [`render_pool`] (the renderer the pool writer itself uses) and the vhost
/// through [`render_vhost`]. A file that already equals its render is not
/// touched, which means a site that is current costs two reads and no
/// validator and no reload — the property that keeps a retry, and a panel that
/// converges every site on every start, from being a reload storm.
///
/// The order is the version switch's, for the same reason: the POOL first, the
/// vhost second. Between the two writes the vhost still names the old socket
/// while the new pool already listens on the new one, and a reload of the
/// php-fpm master that binds the new socket is complete before nginx is told to
/// connect to it. A SUSPENDED site gets its pool converged and its stub left
/// alone, exactly as a version switch does (`sites::is_suspended_vhost`): the
/// vhost on disk is the stub, and re-rendering the site's own over it would put
/// the customer's content back on the air while the panel records the site as
/// suspended. Both writes go through `safe_write`, so a refusal at either
/// restores the previous bytes and nothing later is attempted — a pool that php-fpm
/// refuses leaves the vhost exactly as it was.
///
/// The caller has already checked that the version is installed and that the
/// site is on it; this function re-reads the vhost itself so it can be driven on
/// its own.
///
/// # Errors
///
/// Returns [`SitesOpError::NotFound`] when no vhost for the domain exists.
/// Returns [`SitesOpError::ConfigWrite`] carrying the PHP area's failure when
/// the pool cannot be rendered or written — including the account being busy —
/// and this area's usual [`SitesOpError::NginxValidation`],
/// [`SitesOpError::ReloadFailed`] and [`SitesOpError::Render`] for the vhost
/// half. A program killed at its deadline in either half is
/// [`SitesOpError::Bound`] and never a refusal.
pub(crate) fn converge_php_site(
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
        ..
    } = *switch;
    let paths = resolved_site_paths(host, &input.account, &input.domain, input.web_server)?;

    let Some(current) = host.read_config(&paths.config_path)? else {
        return Err(SitesOpError::NotFound {
            domain: input.domain.as_str().to_owned(),
        });
    };

    let pool_paths = PoolPaths::for_pool(distro, &input.account, version);
    let wanted_pool = render_pool(
        distro,
        &PoolInput {
            account: input.account.clone(),
            version: version.clone(),
            max_children,
            overrides: overrides.to_vec(),
        },
        &pool_paths,
    )?;

    if php_host.read_config(&pool_paths.config_path).as_deref() != Some(wanted_pool.as_str()) {
        write_site_pool(php_host, distro, input, version, max_children, overrides)?;
    }

    if is_suspended_vhost(&current, input, &paths)? {
        return Ok(());
    }

    let wanted_vhost = render_vhost(input, &paths)?;
    // After the pool and before the vhost: the vhost names the credential file,
    // and the options that changed are what this call is often made for.
    sync_site_auth_file(host, distro, input)?;
    if current != wanted_vhost {
        write_vhost(
            host,
            distro,
            input.web_server,
            &paths.config_path,
            &wanted_vhost,
        )?;
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sites/converge_php_site_tests.rs"]
mod tests;
