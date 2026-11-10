//! Replacing the whole content of one zone.

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
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::model::zone_reading::ZoneReading;
use crate::dns::read_zone_mode::read_zone_mode;
use crate::dns::render_zone::render_zone;
use crate::dns::retire_superseded_files::retire_superseded_files;
use crate::dns::rndc_arguments::rndc_arguments;
use crate::dns::write_signing_keys::write_signing_keys;
use crate::dns::zone_host::ZoneHost;
use crate::dns::zone_load::ZoneLoad;
use crate::dns::zone_statement_for::zone_statement_for;
use crate::safe_write::model::{Reload, Validator};

/// Replaces the whole content of `content`'s zone with the file rendered from it,
/// and makes the daemon serve it, signed or not as `content` says.
///
/// The sequence is the config-write protocol (rules/rust.md "Config writes"):
/// render, write beside the target and swap in at mode 0644, validate with the
/// daemon's own zone checker at the real path, then load it and restore the
/// previous file (or remove the new one) if either refuses. A zone the checker
/// refuses is therefore never loaded. The free-space floor is asked before
/// anything is created.
///
/// **Which directory, which command.** A signed or withdrawing zone's file is
/// written into the signed-zone directory and an unsigned one's into the plain
/// directory ([`ZoneDirectories::directory_for`]). The daemon is told with
/// `rndc addzone` when it does not know the zone, with `rndc modzone` when it
/// knows the zone and the mode its statement shows differs from the wanted one (or
/// could not be read), and with `rndc reload` when the mode is the same
/// (`ZoneLoad::choose`). The statement is rendered by a template; for an
/// unsigned zone it is byte-identical to the text this function built before
/// signing existed.
///
/// **Keys.** A signed zone's key files are written FIRST, so the daemon finds them
/// when it loads the zone; they are inert until a statement names the key
/// directory, so a failure after them leaves files that harm nothing and that a
/// repeat converges over. After the load succeeds the files the new mode makes
/// obsolete are removed (`retire_superseded_files`): the old copy in the other
/// directory, and every key file of an UNSIGNED zone.
///
/// Idempotent and replace-whole: the same content converges to the same file and
/// the same served zone, and a record absent from `content` is gone. Two calls
/// that race on one NEW zone both see it unknown; the second's `addzone` is
/// refused by the daemon, its file is restored to the first's, and a retry takes
/// the reload path. The panel serialises per zone, so this is a stated edge and
/// not a lock.
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
/// [`DnsError::ControlRefused`] when the daemon cannot be asked or refuses to add,
/// modify or reload the zone, [`DnsError::ZoneStateUnreadable`] when the daemon
/// does not report the zone afterwards, and [`DnsError::SpawnFailed`],
/// [`DnsError::Bound`], [`DnsError::ConfigWrite`] or
/// [`DnsError::ZoneDirectoryUnreadable`] for the mechanical failures. None carries
/// a byte of a key.
pub fn put_zone(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    directories: &ZoneDirectories,
    floor: &FreeSpaceFloor,
    content: &ZoneContent,
) -> Result<PutZoneOutcome, DnsError> {
    let signing = content.signing();
    let directory = directories.directory_for(signing);
    floor
        .admit(host.available_bytes(directory))
        .map_err(floor_error)?;

    let rendered = render_zone(content)?;
    let zone = content.zone();
    let file_name = ZoneFileName::of(zone);
    let target = directory.join(file_name.as_str());
    let target_text = target.to_str().ok_or_else(|| DnsError::ConfigWrite {
        reason: "the zone directory is not valid UTF-8".to_owned(),
    })?;

    // Asked before the write because it decides the load command. A daemon that
    // cannot be asked refuses the whole call here, before a file is touched.
    let reading = get_zone(host, distro, zone)?;
    let created = matches!(reading, ZoneReading::NotServed);
    let observed = match reading {
        ZoneReading::NotServed => None,
        ZoneReading::Served { .. } => read_zone_mode(host, distro, zone),
    };
    let load = ZoneLoad::choose(&reading, observed, signing.mode());

    if let Some(key) = signing.key() {
        write_signing_keys(host, distro, directories.keys(), zone, key)?;
    }

    let checkzone = distro.dns_checkzone_binary();
    let rndc = distro.dns_control_binary();
    let port = AgentPaths::DNS_CONTROL_PORT.to_string();
    let statement = zone_statement_for(&target, directories.keys(), signing.mode())?;
    let load_arguments = rndc_arguments(&port, &load.command(zone.as_str(), &statement));
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
            arguments: &load_arguments,
        },
    )?;

    retire_superseded_files(host, directories, zone, &file_name, signing)?;

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
