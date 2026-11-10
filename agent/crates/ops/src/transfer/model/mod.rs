//! The values the connection operations take and return: plain data, every
//! text field a validated type, no behaviour beyond construction.

pub mod dump_file;
pub mod dump_request;
pub mod dump_sink;
pub mod dump_step;
pub mod dump_summary;
pub mod generated_source_key;
pub mod opened_connection;
pub mod pull_phase;
pub mod pull_request;
pub mod pull_sink;
pub mod pull_summary;
pub mod rsync_slice_outcome;
pub mod scanned_host_key;
pub mod source_credential;
pub mod source_database_server;
pub mod source_disk;
pub mod source_endpoint;
pub mod source_kind;
pub mod source_web_server;
pub mod ssh_connection;
pub mod tested_source;
