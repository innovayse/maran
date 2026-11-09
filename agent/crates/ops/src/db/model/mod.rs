//! Inputs and outputs of the database operations, one type per file
//! (rules/rust.md "Operation anatomy").

pub mod create_database_request;
pub mod database_privilege_change;
pub mod database_size_report;
pub mod database_summary;
pub mod database_user_request;
pub mod grant_repair_refusal;
pub mod grant_repair_report;
pub mod refused_grant;
pub mod repaired_grant;
