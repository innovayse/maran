//! Replacing the whole content of one zone.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::get_zone::get_zone;
use crate::dns::model::put_zone_outcome::PutZoneOutcome;
use crate::dns::model::zone_content::ZoneContent;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::render_zone::render_zone;
use crate::dns::rndc_arguments::rndc_arguments;
use crate::dns::zone_host::ZoneHost;
use crate::safe_write::model::{Reload, Validator};

/// Replaces the whole content of `content`'s zone with the file rendered from it,
/// and makes the daemon serve it.
///
/// The sequence is the config-write protocol (rules/rust.md "Config writes"):
/// render, write beside the target and swap in at mode 0644, validate with the
/// daemon's own zone checker at the real path, then load it — `rndc addzone` when
/// the daemon does not know the zone, `rndc reload <zone>` when it does — and
/// restore the previous file (or remove the new one) if either refuses. A zone
/// the checker refuses is therefore never loaded. The free-space floor is asked
/// before anything is created.
///
/// Idempotent and replace-whole: the same content converges to the same file and
/// the same served zone, and a record absent from `content` is gone. Two calls
/// that race on one NEW zone both see it unknown; the second's `addzone` is
/// refused by the daemon, its file is restored to the first's, and a retry takes
/// the reload path. The panel serialises per zone, so this is a stated edge and
/// not a lock.
///
/// `directory` is the zone directory from the distribution's facts; it is a
/// parameter so a test can write into one it can create.
///
/// `rndc reload <zone>` is acknowledged when the load is queued: a file the
/// checker accepted but the daemon then refuses to load shows only in the
/// daemon's log, and the serial read back afterwards is the observable that tells
/// the panel what it is now serving.
///
/// # Errors
///
/// Returns [`DnsError::ZoneBelowFloor`] or [`DnsError::ZoneUnmeasurable`] before
/// any write, [`DnsError::ZoneContent`] when the rendered file is too large,
/// [`DnsError::ZoneRejected`] when the checker refuses the zone,
/// [`DnsError::ControlRefused`] when the daemon cannot be asked or refuses to add
/// or reload the zone, [`DnsError::ZoneStateUnreadable`] when the daemon does not
/// report the zone afterwards, and [`DnsError::SpawnFailed`],
/// [`DnsError::Bound`] or [`DnsError::ConfigWrite`] for the mechanical failures.
pub fn put_zone(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    directory: &Path,
    floor: &FreeSpaceFloor,
    content: &ZoneContent,
) -> Result<PutZoneOutcome, DnsError> {
    floor
        .admit(host.available_bytes(directory))
        .map_err(floor_error)?;

    let rendered = render_zone(content)?;
    let zone = content.zone();
    let target = directory.join(ZoneFileName::of(zone).as_str());
    let target_text = target.to_str().ok_or_else(|| DnsError::ConfigWrite {
        reason: "the zone directory is not valid UTF-8".to_owned(),
    })?;

    // Asked before the write because it decides the load command. A daemon that
    // cannot be asked refuses the whole call here, before a file is touched.
    let created = matches!(get_zone(host, distro, zone)?, ZoneReading::NotServed);

    let checkzone = distro.dns_checkzone_binary();
    let rndc = distro.dns_control_binary();
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let statement = format!("{{ type primary; file \"{target_text}\"; }};");
    let reload_arguments = if created {
        rndc_arguments(&port, &["addzone", zone.as_str(), &statement])
    } else {
        rndc_arguments(&port, &["reload", zone.as_str()])
    };
    let check_arguments = ["-q", zone.as_str(), target_text];

    host.write_zone_file(
        &target,
        &rendered,
        &Validator {
            program: &checkzone,
            arguments: &check_arguments,
            class: CallClass::Validate,
        },
        &Reload {
            program: &rndc,
            arguments: &reload_arguments,
        },
    )?;

    match get_zone(host, distro, zone)? {
        ZoneReading::Served { serial } => Ok(PutZoneOutcome { created, serial }),
        ZoneReading::NotServed => Err(DnsError::ZoneStateUnreadable),
    }
}

/// Maps the free-space floor's refusal onto this area's list.
fn floor_error(refusal: FloorRefusal) -> DnsError {
    match refusal {
        FloorRefusal::BelowFloor { available, floor } => {
            DnsError::ZoneBelowFloor { available, floor }
        }
        _ => DnsError::ZoneUnmeasurable,
    }
}

#[cfg(test)]
#[path = "../tests/dns/put_zone_tests.rs"]
mod tests;
