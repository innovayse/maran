//! The second step of an inventory read: the sites.

use super::assign_sites_to_accounts::assign_sites_to_accounts;
use super::inventory_run::InventoryRun;
use super::model::account_draft::AccountDraft;
use super::model::passwd_entry::PasswdEntry;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::model::vhost_budget::VhostBudget;
use super::parse_apache_vhosts::parse_apache_vhosts;
use super::parse_nginx_vhosts::parse_nginx_vhosts;
use super::parse_php_versions::parse_php_versions;
use super::read_vhost_files::read_vhost_files;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Reads the virtual hosts of both web servers and attributes each to the
/// account whose home holds its document root.
///
/// Both servers are always asked: the listing of a server that is not installed
/// is empty, and the inventory does not decide from the source's own word which
/// one is real. A virtual host that belongs to no account (a document root
/// outside every home, or none) is a `SiteUnreadable` warning naming its
/// validated primary domain, and is not attached to anyone.
///
/// # Errors
///
/// Every connection error the shell returns.
pub fn read_sites(
    run: &mut InventoryRun<'_>,
    drafts: &mut [AccountDraft],
) -> Result<(), TransferOpError> {
    let installed_php = match run.listing(
        &RemoteCommand::ListPhpVersions,
        SourceWarningRecord::about_nothing(SourceWarningKind::SiteUnreadable),
    )? {
        Some(listing) => {
            let parsed = parse_php_versions(&listing.text);
            run.warn_each(parsed.warnings);
            parsed.items
        }
        None => Vec::new(),
    };

    let mut budget = VhostBudget::new();
    let nginx_files = read_vhost_files(
        run,
        &RemoteCommand::ListNginxVhostFiles,
        &|path| RemoteCommand::ReadNginxVhostFile { path },
        &mut budget,
    )?;
    let apache_files = read_vhost_files(
        run,
        &RemoteCommand::ListApacheVhostFiles,
        &|path| RemoteCommand::ReadApacheVhostFile { path },
        &mut budget,
    )?;

    let nginx = parse_nginx_vhosts(&nginx_files);
    let apache = parse_apache_vhosts(&apache_files);
    run.warn_each(nginx.warnings);
    run.warn_each(apache.warnings);
    let mut vhosts = nginx.items;
    vhosts.extend(apache.items);

    let entries: Vec<&PasswdEntry> = drafts.iter().map(|draft| &draft.entry).collect();
    let assignment = assign_sites_to_accounts(vhosts, &entries, &installed_php);
    for (draft, sites) in drafts.iter_mut().zip(assignment.per_account) {
        draft.sites = sites;
    }
    for domain in &assignment.unowned {
        run.warn(SourceWarningRecord::about_domain(
            SourceWarningKind::SiteUnreadable,
            domain,
        ));
    }
    Ok(())
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_sites_tests.rs"]
mod tests;
