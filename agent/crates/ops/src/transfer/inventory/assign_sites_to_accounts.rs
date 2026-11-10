//! Attributing virtual hosts to the accounts whose homes hold their files.

use std::collections::{HashMap, HashSet};

use super::model::parsed_vhost::ParsedVhost;
use super::model::passwd_entry::PasswdEntry;
use super::model::site_assignment::SiteAssignment;
use super::model::source_site_backend::SourceSiteBackend;
use super::model::source_site_record::SourceSiteRecord;

/// Gives each virtual host to the account whose home contains its document root.
///
/// Containment is by whole path components: `/home/acme/public_html` is inside
/// `/home/acme`, `/home/acme2/www` is not. When homes nest, the deepest home
/// that contains the root wins. The document root and the homes were validated as
/// remote paths before they got here, so there is no `..` and no `//` to
/// misread. A home of `/` owns nothing (it would own everything). Two accounts
/// with the same home: the first one in `accounts` owns it.
///
/// A virtual host with no document root, or whose root lies in no account's home
/// (`/var/www/html`), is returned in `unowned`; it is never guessed onto an
/// account. A primary name seen twice across the list keeps its first virtual
/// host (the web servers' own order).
///
/// A PHP site whose configuration names no version takes the version the source
/// has installed WHEN exactly one is installed (`installed_php`), and keeps none
/// otherwise: a guess between two versions would be a made-up fact.
///
/// The result has one entry per account in `per_account`, in the order of
/// `accounts`.
#[must_use]
pub fn assign_sites_to_accounts(
    vhosts: Vec<ParsedVhost>,
    accounts: &[&PasswdEntry],
    installed_php: &[String],
) -> SiteAssignment {
    let mut owners: HashMap<&str, usize> = HashMap::new();
    for (index, account) in accounts.iter().enumerate() {
        let home = trimmed(account.home.as_str());
        if !home.is_empty() {
            owners.entry(home).or_insert(index);
        }
    }

    let sole_php_version = match installed_php {
        [only] => Some(only.as_str()),
        _ => None,
    };

    let mut per_account: Vec<Vec<SourceSiteRecord>> = vec![Vec::new(); accounts.len()];
    let mut unowned = Vec::new();
    let mut seen: HashSet<String> = HashSet::new();
    for vhost in vhosts {
        if !seen.insert(vhost.primary.as_str().to_owned()) {
            continue;
        }
        let owner = vhost
            .document_root
            .as_ref()
            .and_then(|root| owner_of(trimmed(root.as_str()), &owners));
        let (Some(index), Some(root)) = (owner, vhost.document_root.as_ref()) else {
            unowned.push(vhost.primary);
            continue;
        };
        let php_version = match (vhost.backend, vhost.php_version) {
            (SourceSiteBackend::Php, None) => sole_php_version.map(str::to_owned),
            (_, version) => version,
        };
        if let Some(sites) = per_account.get_mut(index) {
            sites.push(SourceSiteRecord {
                domain: vhost.primary,
                aliases: vhost.aliases,
                backend: vhost.backend,
                php_version,
                document_root: root.as_str().to_owned(),
            });
        }
    }
    SiteAssignment {
        per_account,
        unowned,
    }
}

/// The account whose home is the deepest ancestor of `root` (or `root` itself).
fn owner_of(root: &str, owners: &HashMap<&str, usize>) -> Option<usize> {
    let mut candidate = root;
    loop {
        if let Some(index) = owners.get(candidate) {
            return Some(*index);
        }
        match candidate.rfind('/') {
            Some(position) if position > 0 => candidate = candidate.get(..position)?,
            _ => return None,
        }
    }
}

/// A path without trailing slashes; the root path becomes empty.
fn trimmed(path: &str) -> &str {
    path.trim_end_matches('/')
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/assign_sites_to_accounts_tests.rs"]
mod tests;
