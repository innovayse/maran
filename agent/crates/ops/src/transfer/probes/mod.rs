//! Parsers of the fixed probes' answers: pure functions over untrusted text,
//! each bounded and each answering "unknown" instead of guessing.

pub mod parse_database_server;
pub mod parse_effective_uid;
pub mod parse_home_filesystem;
pub mod parse_mail_found;
pub mod parse_os_release;
pub mod parse_panel_version;
pub mod parse_web_server;
