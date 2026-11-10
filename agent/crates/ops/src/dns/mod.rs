//! The authoritative DNS daemon (BIND 9) on this host: turning it on and off,
//! observing it, and the zones it serves.
//!
//! [`enable_dns`] renders the daemon's configuration, has the daemon's own
//! checker read it at its real path, restarts the unit and asks the daemon, over
//! its loopback control channel, whether it is alive; [`disable_dns`] stops the
//! unit and takes it out of the boot sequence; [`get_dns_status`] observes the
//! host. [`put_zone`], [`delete_zone`], [`get_zone`] (with [`describe_zone`],
//! which adds how the daemon signs the zone) and [`list_zones`] are the zone
//! rpcs, [`reconcile_zone_files`] the start-up pass that re-adds zone files the
//! daemon lost, and [`rndc_arguments`] the one spelling of an `rndc` command line
//! they all share.
//!
//! DNSSEC (issue #452) rides inside [`put_zone`] as [`ZoneSigning`], with no rpc of
//! its own: a signed zone's file goes into the signed-zone directory, its key
//! files into the key directory (`write_signing_keys`, `remove_signing_keys`), and
//! the three directories travel together as [`ZoneDirectories`].
//! [`probe_dnssec_support`] asks the daemon's own checker whether this build can
//! sign at all.
//!
//! The daemon arrives switched off (`installer/lib/89-dns.sh`), and this area
//! never opens a firewall port: port 53 is opened by the administrator's own
//! action, as for FTPS.

mod command_output_excerpt;
mod delete_zone;
mod describe_zone;
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
mod is_printable_token;
mod keyed_zones;
mod list_zone_files;
mod list_zones;
pub mod model;
mod parse_showzone_mode;
mod parse_signing_list;
mod parse_zone_serial;
mod plan_zone_restoration;
mod probe_dnssec_support;
mod process_dns_host;
mod process_zone_host;
mod put_zone;
mod read_signing_state;
mod read_zone_mode;
mod reconcile_zone_files;
mod record_rdata;
mod remove_signed_copy;
mod remove_signing_keys;
mod render_zone;
mod retire_superseded_files;
mod rndc_arguments;
mod wait_for_control_channel;
mod write_signing_keys;
mod zone_host;
mod zone_load;
mod zone_statement_for;
mod zone_write_error;

pub use delete_zone::delete_zone;
pub use describe_zone::describe_zone;
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
pub use model::key_publication::KeyPublication;
pub use model::listed_zone::ListedZone;
pub use model::put_zone_outcome::PutZoneOutcome;
pub use model::record_data::RecordData;
pub use model::signing_mode::SigningMode;
pub use model::zone_content::ZoneContent;
pub use model::zone_content_fault::ZoneContentFault;
pub use model::zone_description::ZoneDescription;
pub use model::zone_directories::ZoneDirectories;
pub use model::zone_limits::ZoneLimits;
pub use model::zone_reading::ZoneReading;
pub use model::zone_reconciliation::ZoneReconciliation;
pub use model::zone_record::ZoneRecord;
pub use model::zone_signing::ZoneSigning;
pub use model::zone_signing_observation::ZoneSigningObservation;
pub use model::zone_soa::ZoneSoa;
pub use probe_dnssec_support::probe_dnssec_support;
pub use process_dns_host::ProcessDnsHost;
pub use put_zone::put_zone;
pub use reconcile_zone_files::reconcile_zone_files;
pub use record_rdata::record_rdata;
pub use render_zone::render_zone;
pub use rndc_arguments::rndc_arguments;
pub use zone_host::ZoneHost;
