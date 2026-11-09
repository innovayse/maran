//! The authoritative DNS daemon (BIND 9) on this host: turning it on and off,
//! observing it, and the zones it serves.
//!
//! [`enable_dns`] renders the daemon's configuration, has the daemon's own
//! checker read it at its real path, restarts the unit and asks the daemon, over
//! its loopback control channel, whether it is alive; [`disable_dns`] stops the
//! unit and takes it out of the boot sequence; [`get_dns_status`] observes the
//! host. [`put_zone`], [`delete_zone`], [`get_zone`] and [`list_zones`] are the
//! zone rpcs, [`reconcile_zone_files`] the start-up pass that re-adds zone files
//! the daemon lost, and [`rndc_arguments`] the one spelling of an `rndc` command
//! line they all share.
//!
//! The daemon arrives switched off (`installer/lib/89-dns.sh`), and this area
//! never opens a firewall port: port 53 is opened by the administrator's own
//! action, as for FTPS.

mod command_output_excerpt;
mod delete_zone;
mod disable_dns;
mod dns_error;
mod dns_host;
mod enable_dns;
#[cfg(test)]
#[path = "../tests/dns/fake_dns_host.rs"]
pub(crate) mod fake_dns_host;
#[cfg(test)]
#[path = "../tests/dns/fake_zone_host.rs"]
pub(crate) mod fake_zone_host;
mod get_dns_status;
mod get_zone;
mod list_zone_files;
mod list_zones;
pub mod model;
mod parse_zone_serial;
mod process_dns_host;
mod process_zone_host;
mod put_zone;
mod reconcile_zone_files;
mod record_rdata;
mod render_zone;
mod rndc_arguments;
mod wait_for_control_channel;
mod zone_host;
mod zone_write_error;

pub use delete_zone::delete_zone;
pub use disable_dns::disable_dns;
pub use dns_error::DnsError;
pub use dns_host::DnsHost;
pub use enable_dns::enable_dns;
pub use get_dns_status::get_dns_status;
pub use get_zone::get_zone;
pub use list_zone_files::list_zone_files;
pub use list_zones::list_zones;
pub use model::delete_zone_outcome::DeleteZoneOutcome;
pub use model::dns_policy_observation::DnsPolicyObservation;
pub use model::dns_state::DnsState;
pub use model::dns_unit::DnsUnit;
pub use model::listed_zone::ListedZone;
pub use model::put_zone_outcome::PutZoneOutcome;
pub use model::record_data::RecordData;
pub use model::zone_content::ZoneContent;
pub use model::zone_content_fault::ZoneContentFault;
pub use model::zone_limits::ZoneLimits;
pub use model::zone_reading::ZoneReading;
pub use model::zone_reconciliation::ZoneReconciliation;
pub use model::zone_record::ZoneRecord;
pub use model::zone_soa::ZoneSoa;
pub use process_dns_host::ProcessDnsHost;
pub use put_zone::put_zone;
pub use reconcile_zone_files::reconcile_zone_files;
pub use record_rdata::record_rdata;
pub use render_zone::render_zone;
pub use rndc_arguments::rndc_arguments;
pub use zone_host::ZoneHost;
