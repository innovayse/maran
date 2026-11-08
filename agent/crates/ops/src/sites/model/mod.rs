//! Inputs the site operations accept and values they return, one type per file
//! (rules/rust.md "Operation anatomy").

pub mod account_site_suspension;
pub mod auth_file_placement;
pub mod basic_auth_user;
pub mod canonical_host;
pub mod canonical_split;
pub mod create_site_input;
pub mod created_site;
pub mod ip_access;
pub mod ip_access_mode;
pub mod log_tail_request;
pub mod parsed_access_line;
pub mod php_switch;
pub mod prune_logs_input;
pub mod pruned_logs;
pub mod read_progress;
pub mod redirect_match;
pub mod redirect_rule;
pub mod redirect_status;
pub mod site_certificate;
pub mod site_identity;
pub mod site_kind;
pub mod site_log_kind;
pub mod site_options;
pub mod site_options_error;
pub mod site_paths;
pub mod site_serving;
pub mod site_suspension_fact;
pub mod tail_end;
pub mod traffic_cursor;
pub mod traffic_date;
pub mod traffic_day;
pub mod traffic_path_count;
pub mod traffic_status_count;
pub mod traffic_summary;
pub mod traffic_summary_input;
