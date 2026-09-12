//! Observing what this host actually serves for one account's sites.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::sites::model::account_site_suspension::AccountSiteSuspension;
use crate::sites::model::site_suspension_fact::SiteSuspensionFact;
use crate::sites::render_suspended_vhost::render_suspended_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::{SiteHost, SitesOpError};

/// The directive whose values name every hostname a server block answers for.
///
/// Read with its rendered indentation attached, because that is the shape this
/// agent writes and the only shape the comparison below can conclude anything
/// about; see [`server_names`].
const SERVER_NAME_DIRECTIVE: &str = "    server_name ";

/// Reports, per vhost this host serves for `account`, whether it is the
/// suspended stub.
///
/// # Why it asks the machine instead of taking a list
///
/// The caller could hand over the domains the panel remembers, and the answer
/// would then be a fact about the panel's rows rather than about what nginx
/// serves. The vhost the panel has FORGOTTEN is exactly the one still serving
/// a suspended customer's site, so this enumerates the vhost directory and
/// decides membership from the file's own text: every vhost this agent renders
/// names `/var/log/maran/sites/<account>/` in both of its log directives, and
/// one account's marker is not a prefix of another's because it carries the
/// trailing separator.
///
/// # Why the marker is the LOG directory and not the home
///
/// It was `/home/<account>/`, and that was wrong for a reason nothing observed
/// until the logs moved out of the home. A vhost names the account's home in
/// its `root` directive — but it names the **canonical** one, because
/// `resolved_site_paths` renders what `resolve_in_home` reports rather than
/// what was asked for. On a host where `/home` is a symlink or the homes are
/// bind-mounted — both perfectly ordinary — that text is not `/home/<account>/`
/// at all. The literal `/home/<account>/` in those files was coming from the
/// two log paths, which were NAMED rather than resolved. Removing the logs from
/// the home therefore made this enumeration return nothing on exactly those
/// hosts: an account whose sites are all still serving would have been reported
/// as serving none, which is the answer that certifies a suspension that never
/// happened.
///
/// The log directory is the better marker on its own merits, not merely the
/// available one: it is outside every home, so no home layout can change its
/// text; it is named and never resolved, because it is root-owned all the way
/// up and there is nothing to canonicalize away; and it appears in every vhost
/// this agent renders, serving and suspended alike.
///
/// The bound, stated rather than left to be discovered: a vhost that serves
/// the account's files without naming its log directory — a hand-written one,
/// or one whose logging was pointed somewhere else entirely — is invisible
/// here. Nothing this panel writes has that shape.
///
/// # Why "stubbed" is a byte comparison and not a marker
///
/// `serving_stub` is decided by rendering the suspended vhost for the site and
/// comparing it with the file, byte for byte — the same definition
/// `disable_site` already uses to be idempotent, so there is one definition of
/// suspended in the workspace. A marker file beside the config could outlive
/// the config it claimed to describe, and then "suspended" would be a fact
/// about the marker rather than about what nginx serves.
///
/// # Errors
///
/// Returns [`SitesOpError::ConfigUnreadable`] when a vhost exists but cannot
/// be read, which must not be silently treated as "no site here". A directory
/// that cannot be LISTED is not an error but a reported fact — see
/// [`AccountSiteSuspension::directory_readable`].
pub fn inspect_account_sites(
    host: &dyn SiteHost,
    account: &AccountName,
) -> Result<AccountSiteSuspension, SitesOpError> {
    let Ok(paths) = host.list_config_paths() else {
        return Ok(AccountSiteSuspension::default());
    };

    let marker = format!(
        "{}{}",
        AgentPaths::account_site_log_dir(account).display(),
        "/"
    );
    let mut sites = Vec::new();

    for path in paths {
        let Some(contents) = host.read_config(&path)? else {
            // Listed a moment ago and gone now: a concurrent delete_site, not
            // a site being served. Nothing to report about a file nginx no
            // longer has.
            continue;
        };
        if !contents.contains(&marker) {
            continue;
        }

        sites.push(SiteSuspensionFact {
            domain: file_stem(&path),
            serving_stub: serving_stub(host, account, &path, &contents),
        });
    }

    Ok(AccountSiteSuspension {
        directory_readable: true,
        sites,
    })
}

/// The domain a vhost file name spells, or the file name itself when it spells
/// none.
///
/// `SitePaths::for_site` names every vhost `<domain>.conf`, so the stem IS the
/// domain and the two cannot drift. A file whose stem is unreadable is still
/// reported — under whatever the name says — because it is a file in the
/// serving directory that mentions this account's home, and dropping it would
/// be the enumeration quietly deciding it had seen nothing.
fn file_stem(path: &Path) -> String {
    path.file_stem().map_or_else(
        || path.display().to_string(),
        |stem| stem.to_string_lossy().into_owned(),
    )
}

/// Decides whether `contents` is the suspended render for this site.
///
/// Returns `false` for every case in which the comparison text cannot be
/// produced: a file name that is not a domain, a document root that no longer
/// resolves inside the account's home, a `server_name` line that is not there
/// or is there more than once, a template that refuses to render. Each of
/// those is "this agent cannot observe the site", and the answer to that is
/// the one that refuses a suspension rather than certifying one
/// ([`SiteSuspensionFact::serving_stub`]).
fn serving_stub(host: &dyn SiteHost, account: &AccountName, path: &Path, contents: &str) -> bool {
    let Ok(domain) = Domain::parse(&file_stem(path)) else {
        return false;
    };
    let Ok(resolved) = resolved_site_paths(host, account, &domain) else {
        return false;
    };
    let Some((primary, aliases)) = server_names(contents) else {
        return false;
    };

    // The names are read out of the file and are NOT trusted: they only supply
    // a candidate for the render. A candidate read wrongly produces text that
    // does not match, which is reported as not stubbed — the comparison
    // decides, the parse only proposes. That is what lets a one-line reader
    // stand in for an nginx parser here without weakening the answer.
    render_suspended_vhost(&primary, &aliases, &resolved).is_ok_and(|expected| expected == contents)
}

/// Reads the primary name and the aliases out of a rendered `server_name` line.
///
/// Matched with its indentation and its terminating semicolon, and required to
/// occur exactly ONCE, because the only files this can conclude anything about
/// are the ones this agent renders — every other shape falls through to
/// [`SiteSuspensionFact::serving_stub`] being false. Returns [`None`] when the
/// line is absent, repeated, or carries no name at all.
fn server_names(contents: &str) -> Option<(String, Vec<String>)> {
    let mut matches = contents.lines().filter_map(|line| {
        line.strip_prefix(SERVER_NAME_DIRECTIVE)
            .and_then(|rest| rest.strip_suffix(';'))
    });

    let line = matches.next()?;
    if matches.next().is_some() {
        return None;
    }

    let mut names = line.split_whitespace().map(str::to_owned);
    let primary = names.next()?;

    Some((primary, names.collect()))
}

#[cfg(test)]
#[path = "../tests/sites/inspect_account_sites_tests.rs"]
mod tests;
