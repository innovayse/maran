//! One account found on a source onto its wire payload.

use maran_ops::transfer::inventory::model::source_account_record::SourceAccountRecord;
use maran_ops::transfer::inventory::model::source_database_record::SourceDatabaseRecord;
use maran_ops::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use maran_ops::transfer::inventory::model::source_site_record::SourceSiteRecord;

use crate::proto;

/// The wire form of an account, with its sites and databases.
///
/// An unmeasured size is the wire's zero with the matching `*_known` flag false;
/// an absent PHP version is the empty string. Every name is a validated type's
/// text, and the home and document roots are display strings the panel never uses
/// as a path on this host.
#[must_use]
pub fn to_wire_source_account(account: &SourceAccountRecord) -> proto::SourceAccount {
    proto::SourceAccount {
        name: account.name.as_str().to_owned(),
        home: account.home.clone(),
        disk_bytes: account.disk_bytes.unwrap_or(0),
        disk_known: account.disk_bytes.is_some(),
        sites: account.sites.iter().map(wire_site).collect(),
        databases: account.databases.iter().map(wire_database).collect(),
        cron_entry_count: account.cron_entry_count,
        mailbox_count: account.mailbox_count,
    }
}

/// The wire form of one site.
fn wire_site(site: &SourceSiteRecord) -> proto::SourceSite {
    proto::SourceSite {
        domain: site.domain.as_str().to_owned(),
        aliases: site
            .aliases
            .iter()
            .map(|alias| alias.as_str().to_owned())
            .collect(),
        backend: match site.backend {
            SourceSiteBackend::Static => proto::SourceSiteBackend::Static,
            SourceSiteBackend::Php => proto::SourceSiteBackend::Php,
            SourceSiteBackend::Proxy => proto::SourceSiteBackend::Proxy,
        } as i32,
        php_version: site.php_version.clone().unwrap_or_default(),
        document_root: site.document_root.clone(),
    }
}

/// The wire form of one database.
fn wire_database(database: &SourceDatabaseRecord) -> proto::SourceDatabase {
    proto::SourceDatabase {
        name: database.name.as_str().to_owned(),
        size_bytes: database.size_bytes.unwrap_or(0),
        size_known: database.size_bytes.is_some(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/to_wire_source_account_tests.rs"]
mod tests;
