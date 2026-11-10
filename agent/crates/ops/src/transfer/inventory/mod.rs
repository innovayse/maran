//! Reading what a registered source serves (issue #197): the accounts it
//! hosts, their sites, databases and crontabs, and how large their homes are.
//!
//! [`read_source_inventory()`] is the one entry point. It runs a CLOSED set of
//! read-only remote commands through a [`SourceShell`](crate::transfer::SourceShell)
//! and parses their output; everything else here is a parser, a validator or a
//! step of that read. The source is untrusted and the parsers are written for it:
//! every output, line, word, file, include chain, account and warning is bounded
//! ([`model::inventory_limits::InventoryLimits`]); a line or file that cannot be
//! read is a warning about that entry and never a panic or a partial read; every
//! name that becomes a panel object passes the validator a hand-typed name
//! passes, and one that fails is skipped, never repaired. `model/` holds the
//! plain data the parsers and steps pass between them.

pub mod apache_syntax_error;
pub mod assign_databases_to_accounts;
pub mod assign_sites_to_accounts;
pub mod build_parsed_vhost;
pub mod claim_names;
pub mod expand_nginx_includes;
#[cfg(test)]
#[path = "../../tests/transfer/inventory/fake_source_shell.rs"]
pub(crate) mod fake_source_shell;
#[cfg(test)]
#[path = "../../tests/transfer/inventory/inventory_fixtures.rs"]
pub(crate) mod inventory_fixtures;
pub mod inventory_run;
pub mod model;
pub mod nginx_syntax_error;
pub mod parse_apache_vhosts;
pub mod parse_crontab;
pub mod parse_home_usage;
pub mod parse_mailbox_count;
pub mod parse_mysql_listing;
pub mod parse_nginx_vhosts;
pub mod parse_passwd;
pub mod parse_php_versions;
pub mod parse_vhost_file_list;
pub mod php_version_in;
pub mod prefix_within;
pub mod read_accounts;
pub mod read_apache_virtual_hosts;
pub mod read_cron;
pub mod read_databases;
pub mod read_disk;
pub mod read_nginx_servers;
pub mod read_sites;
pub mod read_source_inventory;
pub mod read_vhost_files;
pub mod tokenize_nginx;
pub mod wildcard_matches;

pub use model::inventory_sink::InventorySink;
pub use model::inventory_step::InventoryStep;
pub use model::inventory_summary::InventorySummary;
pub use model::source_account_record::SourceAccountRecord;
pub use model::source_database_record::SourceDatabaseRecord;
pub use model::source_site_backend::SourceSiteBackend;
pub use model::source_site_record::SourceSiteRecord;
pub use model::source_warning_kind::SourceWarningKind;
pub use model::source_warning_record::SourceWarningRecord;
pub use read_source_inventory::read_source_inventory;
