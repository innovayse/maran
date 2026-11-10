//! Taking away the credential files whose site is gone.

use std::path::PathBuf;

use maran_distro::SiteWebServer;

use crate::sites::model::site_paths::SitePaths;

use crate::sites::{SiteHost, SitesOpError};

/// The extension every credential file the agent writes carries.
const AUTH_FILE_EXTENSION: &str = "htpasswd";

/// Removes every credential file whose site has no vhost, and answers how many
/// it removed.
///
/// # Why this exists
///
/// A site's credential file is written before its vhost and removed after it,
/// both steps of a process that can be killed between them, so a file can
/// outlive its site. It is not harmful on its own — nothing reads it and only
/// root and the web server's group can list its directory — but it holds
/// password hashes for a site that no longer exists, and the next site created
/// under that domain would find a stale file where its own belongs. Sweeping at
/// start-up closes what the ordering cannot, without giving the agent a state
/// of its own: the credential directory and the vhost directory are both read as
/// they are now.
///
/// # What it removes, and what it leaves
///
/// A file named `<domain>.htpasswd` for which no `<domain>.conf` exists in the
/// agent's vhost directory. Anything else in the credential directory — a name
/// of another shape — is left alone: the agent did not write it.
///
/// A vhost directory that cannot be listed removes NOTHING: a site the sweep
/// cannot see is not a site that is gone.
///
/// # Errors
///
/// Returns [`SitesOpError::ConfigUnreadable`] when the vhost directory cannot be
/// listed and [`SitesOpError::AuthFile`] when the credential directory cannot be
/// listed or a file cannot be removed. Files removed before the failure stay
/// removed, and a repeat converges.
pub fn reconcile_orphan_auth_files(host: &dyn SiteHost) -> Result<usize, SitesOpError> {
    let files = host.list_auth_files()?;
    if files.is_empty() {
        return Ok(0);
    }
    // Listed AFTER the credential files, so a site created in between has its
    // vhost in this list and its file is kept.
    let vhosts = host.list_config_paths()?;

    let mut removed = 0;
    for file in &files {
        if file
            .extension()
            .is_none_or(|ext| ext != AUTH_FILE_EXTENSION)
        {
            continue;
        }
        let Some(domain) = file.file_stem() else {
            continue;
        };
        // Built by formatting and not by `with_extension`: a domain holds dots,
        // and replacing the extension of `example.com` would name `example.conf`.
        // A site's vhost may sit in either web server's directory: the credential file is
        // an orphan only when neither holds one.
        let has_vhost = [
            SitePaths::include_directory(SiteWebServer::Nginx),
            SitePaths::include_directory(SiteWebServer::Apache),
        ]
        .iter()
        .any(|directory| {
            vhosts.contains(
                &PathBuf::from(directory).join(format!("{}.conf", domain.to_string_lossy())),
            )
        });
        if has_vhost {
            continue;
        }

        host.remove_auth_file(file)?;
        removed += 1;
    }

    Ok(removed)
}

#[cfg(test)]
#[path = "../tests/sites/reconcile_orphan_auth_files_tests.rs"]
mod tests;
