//! Server migration, the connection half (issue #197): registering a remote
//! source, learning what key it presents, proving the connection works and
//! opening the pinned channel the inventory (in `inventory/`) is read over.
//!
//! This agent's first outbound client, so the shape is deliberately narrow. It
//! connects ONLY to the one host an administrator registered; the address is
//! resolved once and the policy is applied to the RESOLVED address
//! ([`choose_source_address`]); the host key must equal the pinned one under
//! `StrictHostKeyChecking=yes` with a known_hosts file the agent writes itself
//! (`write_known_hosts`); every `ssh` argument list is built in one place
//! (`ssh_argv`); and what is run on the source is a CLOSED enum of read-only
//! commands with typed holes ([`RemoteCommand`]). No operation here takes a
//! command, a flag or a path template from a caller, and nothing writes to the
//! source.
//!
//! **The credential's life.** The private key arrives per call, is written
//! once to a `0600` file in a `0700` directory under the agent's root-only
//! scratch area ([`TransferHost::create_scratch`]), and that directory is
//! removed by the [`TransferScratch`] guard that owns it, whatever path the
//! operation takes out. No `tracing` event in this area carries a credential,
//! an argument vector or a captured stream, and [`TransferOpError`] cannot hold
//! one.
//!
//! The area's shape is the usual one: an injectable host ([`TransferHost`], the
//! real [`ProcessTransferHost`]), one error enum, `model/` for plain data, and
//! the pure parsers (`probes/`, `parse_scanned_host_keys`, the address-table
//! readers) tested against fixed text. Threat note:
//! `docs/superpowers/notes/2026-11-07-server-migration-threat-note.md`.

mod choose_source_address;
mod classify_ssh_failure;
mod database_loader;
mod dump_remote_database;
#[cfg(test)]
#[path = "../tests/transfer/fake_transfer_host.rs"]
pub(crate) mod fake_transfer_host;
#[cfg(test)]
#[path = "../tests/transfer/fake_transfer_work_host.rs"]
pub(crate) mod fake_transfer_work_host;
mod generate_source_key;
pub mod inventory;
mod known_hosts_alias;
mod known_hosts_line;
pub mod model;
mod open_source_connection;
mod open_source_shell;
mod parse_scanned_host_keys;
mod probe_source;
mod probes;
mod process_transfer_host;
mod process_transfer_work_host;
mod pull_account_files;
mod receiver_directory;
mod remote_command;
mod rsync_argv;
mod rsync_transport;
mod scan_host_key;
mod source_shell;
mod ssh_argv;
mod ssh_client_options;
mod ssh_key_fingerprint;
mod ssh_source_shell;
mod test_source;
#[cfg(test)]
#[path = "../tests/transfer/transfer_fixtures.rs"]
pub(crate) mod transfer_fixtures;
mod transfer_host;
mod transfer_op_error;
mod transfer_scratch;
mod transfer_work_host;
mod write_known_hosts;

pub use choose_source_address::choose_source_address;
pub use database_loader::DatabaseLoader;
pub use dump_remote_database::dump_remote_database;
pub use generate_source_key::generate_source_key;
pub use model::generated_source_key::GeneratedSourceKey;
pub use model::scanned_host_key::ScannedHostKey;
pub use model::source_credential::SourceCredential;
pub use model::source_database_server::SourceDatabaseServer;
pub use model::source_disk::SourceDisk;
pub use model::source_endpoint::SourceEndpoint;
pub use model::source_kind::SourceKind;
pub use model::source_web_server::SourceWebServer;
pub use model::tested_source::TestedSource;
pub use open_source_shell::open_source_shell;
pub use probe_source::probe_source;
pub use process_transfer_host::ProcessTransferHost;
pub use process_transfer_work_host::ProcessTransferWorkHost;
pub use pull_account_files::pull_account_files;
pub use remote_command::RemoteCommand;
pub use scan_host_key::scan_host_key;
pub use source_shell::SourceShell;
pub use ssh_source_shell::SshSourceShell;
pub use test_source::test_source;
pub use transfer_host::TransferHost;
pub use transfer_op_error::TransferOpError;
pub use transfer_scratch::TransferScratch;
pub use transfer_work_host::TransferWorkHost;
