//! The pieces one backup is physically made of: a dump per database, the
//! manifest, the archive over both, and the digest of each.
//!
//! Everything here is crate-private. They are steps of an operation, not
//! operations: each one exists as its own unit because each is a decision worth
//! reading on its own (which flags a dump runs with, how a home is measured,
//! which digest is taken and over what), and none of them is something a caller
//! outside this area should be able to start.

pub(crate) mod archive_home;
pub(crate) mod checksum_file;
pub(crate) mod dump_database;
pub(crate) mod extract_databases_as_root;
pub(crate) mod extract_home_as_account;
pub(crate) mod measure_home;
pub(crate) mod read_manifest;
pub(crate) mod replace_database;
pub(crate) mod scan_members;
