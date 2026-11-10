//! Websites: their document roots inside a customer's home, and the nginx
//! vhosts the agent owns in `/etc/maran/nginx/sites/`.
//!
//! Every operation is idempotent and re-validates its inputs (rules/rust.md
//! "Validation first"): creating a site that exists is
//! [`SitesOpError::AlreadyExists`], deleting one that is gone is
//! [`SitesOpError::NotFound`], and enabling an enabled site or disabling a
//! disabled one changes nothing and succeeds. The panel retries after a
//! timeout, so these are the normal path, not the edge.
//!
//! Two rules shape everything here. A configuration reaches disk only through
//! `crate::safe_write`, and a path inside `/home/<account>/` is touched only by
//! a process that has dropped to that account (rules/security.md).

pub(crate) mod converge_php_site;
mod create_site;
mod create_vhost;
// The site tests' in-memory host and inputs, declared ONCE here rather than in
// each `*_tests.rs`: a file loaded as a module twice is compiled twice, and
// each copy's unused half is then dead code in the other's eyes. Private to
// this module, which is exactly the subtree every test in the area lives in.
mod delete_site;
pub mod log_sink;
// Private: the hardened reading a root-side tail of a customer file needs.
// `ProcessSiteHost` is its only caller, and nothing outside this area should
// be able to start one by another route.
mod disable_site;
mod enable_site;
mod ensure_traversable_directory;
#[cfg(test)]
#[path = "../tests/sites/fake_site_host.rs"]
pub(crate) mod fake_site_host;
#[cfg(test)]
#[path = "../tests/sites/fake_site_log_host.rs"]
pub(crate) mod fake_site_log_host;
mod follow_log;
mod inspect_account_sites;
mod is_site_log_name;
pub(crate) mod is_suspended_vhost;
mod log_file_identity;
pub mod model;
pub(crate) mod nginx_validator;
mod open_site_log;
mod open_site_log_directory;
mod parse_access_log_line;
mod parse_group_id;
mod process_site_host;
mod process_site_log_host;
mod prune_site_logs;
mod read_traffic_file;
mod reconcile_orphan_auth_files;
mod reload_web_server;
mod remove_site_logs;
mod remove_vhost;
// Visible to the crate, not to the world: `crate::ssl` rewires a site's vhost
// when it installs or removes a certificate, and it must produce the SAME text
// this area produces — a second rendering of a vhost is a second opinion about
// what the site serves, and the halves drift on the one a browser reaches.
pub(crate) mod remove_site_pool;
pub(crate) mod render_legacy_suspended_vhost;
pub(crate) mod render_suspended_vhost;
pub(crate) mod render_vhost;
pub(crate) mod resolved_site_paths;
mod rotated_log_name;
mod site_host;
mod site_log_directory;
mod site_log_file;
mod site_log_host;
mod site_maintenance_host;
mod sites_op_error;
mod summarise_site_traffic;
pub(crate) mod sync_site_auth_file;
mod tail_site_log;
mod traffic_budget;
mod traffic_run_slot;
mod traffic_run_slots;
mod traffic_tally;
mod update_site_php_version;
pub(crate) mod write_site_pool;
pub(crate) mod write_vhost;

pub use create_site::create_site;
pub use delete_site::delete_site;
pub use disable_site::disable_site;
pub use enable_site::enable_site;
pub use inspect_account_sites::inspect_account_sites;
pub use log_sink::LogSink;
pub use model::account_site_suspension::AccountSiteSuspension;
pub use model::create_site_input::CreateSiteInput;
pub use model::created_site::CreatedSite;
pub use model::log_tail_request::LogTailRequest;
pub use model::php_switch::PhpSwitch;
pub use model::prune_logs_input::PruneLogsInput;
pub use model::pruned_logs::PrunedLogs;
pub use model::site_certificate::SiteCertificate;
pub use model::site_identity::SiteIdentity;
pub use model::site_kind::SiteKind;
pub use model::site_log_kind::SiteLogKind;
pub use model::site_options::SiteOptions;
pub use model::site_options_error::SiteOptionsError;
pub use model::site_paths::SitePaths;
pub use model::site_serving::SiteServing;
pub use model::site_suspension_fact::SiteSuspensionFact;
pub use model::tail_end::TailEnd;
pub use model::traffic_cursor::TrafficCursor;
pub use model::traffic_date::TrafficDate;
pub use model::traffic_day::TrafficDay;
pub use model::traffic_path_count::TrafficPathCount;
pub use model::traffic_status_count::TrafficStatusCount;
pub use model::traffic_summary::TrafficSummary;
pub use model::traffic_summary_input::TrafficSummaryInput;
pub use process_site_host::ProcessSiteHost;
pub use process_site_log_host::ProcessSiteLogHost;
pub use prune_site_logs::prune_site_logs;
pub use reconcile_orphan_auth_files::reconcile_orphan_auth_files;
pub use reload_web_server::reload_web_server;
pub use remove_site_logs::remove_site_logs;
pub use site_host::SiteHost;
pub use site_log_directory::SiteLogDirectory;
pub use site_log_file::SiteLogFile;
pub use site_log_host::SiteLogHost;
pub use site_maintenance_host::SiteMaintenanceHost;
pub use sites_op_error::SitesOpError;
pub use summarise_site_traffic::summarise_site_traffic;
pub use tail_site_log::{MAXIMUM_HISTORY_LINES, tail_site_log};
pub use update_site_php_version::update_site_php_version;
