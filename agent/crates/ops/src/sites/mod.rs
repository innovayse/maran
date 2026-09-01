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

mod create_site;
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
#[cfg(test)]
#[path = "../tests/sites/fake_site_host.rs"]
pub(crate) mod fake_site_host;
mod follow_log;
pub mod model;
mod process_site_host;
mod reload_web_server;
mod remove_vhost;
// Visible to the crate, not to the world: `crate::ssl` rewires a site's vhost
// when it installs or removes a certificate, and it must produce the SAME text
// this area produces — a second rendering of a vhost is a second opinion about
// what the site serves, and the halves drift on the one a browser reaches.
pub(crate) mod remove_site_pool;
pub(crate) mod render_vhost;
pub(crate) mod resolved_site_paths;
mod site_host;
mod site_maintenance_host;
mod sites_op_error;
mod tail_site_log;
mod update_site_php_version;
pub(crate) mod write_site_pool;
pub(crate) mod write_vhost;

pub use create_site::create_site;
pub use delete_site::delete_site;
pub use disable_site::disable_site;
pub use enable_site::enable_site;
pub use log_sink::LogSink;
pub use model::create_site_input::CreateSiteInput;
pub use model::created_site::CreatedSite;
pub use model::log_tail_request::LogTailRequest;
pub use model::php_switch::PhpSwitch;
pub use model::site_certificate::SiteCertificate;
pub use model::site_identity::SiteIdentity;
pub use model::site_kind::SiteKind;
pub use model::site_log_kind::SiteLogKind;
pub use model::site_paths::SitePaths;
pub use model::tail_end::TailEnd;
pub use process_site_host::ProcessSiteHost;
pub use reload_web_server::reload_web_server;
pub use site_host::SiteHost;
pub use site_maintenance_host::SiteMaintenanceHost;
pub use sites_op_error::SitesOpError;
pub use tail_site_log::{MAXIMUM_HISTORY_LINES, tail_site_log};
pub use update_site_php_version::update_site_php_version;
