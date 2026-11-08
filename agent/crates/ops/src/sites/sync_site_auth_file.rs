//! Bringing a site's credential file to what its options say.

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;
use maran_templates::nginx::auth_line::AuthLine;
use maran_templates::nginx::site_auth_file::SiteAuthFile;

use crate::sites::model::auth_file_placement::AuthFilePlacement;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::nginx_validator::nginx_validator;
use crate::sites::render_vhost::render_failed;
use crate::sites::{SiteHost, SitesOpError};

/// Makes the credential file of `input`'s site equal what its basic-auth users
/// say: written when the site has users, removed when it has none.
///
/// Every operation that writes the site's own vhost calls this FIRST, so a vhost
/// that names `auth_basic_user_file` never reaches nginx before the file does —
/// a vhost reloaded ahead of its file would answer every request with a 500.
/// The converse (a file without a vhost) is harmless: nothing reads it, only
/// root and the web server's group can list its directory, and the start-up
/// sweep (`reconcile_orphan_auth_files`) removes it.
///
/// Idempotent, and the property that keeps a retry from being a storm: the file
/// is rendered and compared with the one on disk, and written only when it
/// differs. A file that is already right costs one read and no `nginx -t`.
/// Rewriting does not reload nginx, which reads the file on every request.
///
/// The hashes are exposed here and only here, into a buffer that goes straight
/// to the write; nothing about the users reaches an error text.
///
/// # Errors
///
/// Returns [`SitesOpError::Render`] when the template fails,
/// [`SitesOpError::ConfigUnreadable`] when the existing file exists and cannot
/// be read, [`SitesOpError::AuthFile`] when the directory cannot be prepared or
/// a stale file cannot be removed, and what
/// [`SiteHost::write_auth_file`] documents for the write itself.
pub(crate) fn sync_site_auth_file(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    input: &CreateSiteInput,
) -> Result<(), SitesOpError> {
    let target = AgentPaths::site_auth_file(&input.domain);
    let users = input.options.basic_auth();
    if users.is_empty() {
        return host.remove_auth_file(&target);
    }

    let lines: Vec<AuthLine<'_>> = users
        .iter()
        .map(|user| AuthLine {
            name: user.name().as_str(),
            hash: user.password_hash().expose(),
        })
        .collect();
    let contents = SiteAuthFile { users: &lines }
        .render_config()
        .map_err(render_failed)?;

    if host.read_config(&target)?.as_deref() == Some(contents.as_str()) {
        return Ok(());
    }

    let nginx = distro.nginx_binary();
    let getent = distro.getent_binary();
    let placement = AuthFilePlacement {
        group: distro.web_server_group(),
        getent: &getent,
    };
    host.write_auth_file(&target, &contents, &placement, &nginx_validator(&nginx))
}

#[cfg(test)]
#[path = "../tests/sites/sync_site_auth_file_tests.rs"]
mod tests;
