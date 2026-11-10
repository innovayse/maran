//! The [`ZoneHost`] that actually touches this machine.

use std::path::Path;

use maran_agent_core::privs::group_id::GroupId;

use crate::dns::dns_error::DnsError;
use crate::dns::process_dns_host::ProcessDnsHost;
use crate::dns::zone_host::ZoneHost;
use crate::dns::zone_write_error::zone_write_error;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{write_config_set, write_unchecked_config};

/// The mode a zone file lands at: readable by the daemon's unprivileged user,
/// writable by root alone.
const ZONE_FILE_MODE: u32 = 0o644;

/// The mode a signing key file lands at: readable by the daemon's group and by
/// root alone.
const KEY_FILE_MODE: u32 = 0o640;

impl ZoneHost for ProcessDnsHost {
    /// Hands the content to the config-write protocol, as a set of one file so
    /// the mode is part of the write and the name never points at a file that was
    /// briefly private or briefly wider.
    ///
    /// # Errors
    ///
    /// Maps the protocol's failure through `zone_write_error`.
    fn write_zone_file(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), DnsError> {
        let file = ConfigFile {
            target,
            contents,
            mode: ZONE_FILE_MODE,
        };
        write_config_set(self, &[file], validator, reload).map_err(zone_write_error)
    }

    /// Writes a key file at `0640` and hands it to the daemon's group.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ConfigWrite`] for every failure; the reason names the
    /// step and never the content.
    fn write_key_file(&self, target: &Path, contents: &str, group: &str) -> Result<(), DnsError> {
        let failed = |step: &str| DnsError::ConfigWrite {
            reason: format!("signing key file: {step}"),
        };

        let gid = GroupId::resolve(group)
            .map_err(|_| failed("the daemon's group could not be resolved"))?
            .gid();
        write_unchecked_config(&ConfigFile {
            target,
            contents,
            mode: KEY_FILE_MODE,
        })
        .map_err(zone_write_error)?;

        // `lchown`: a name swapped for a symlink between the rename and here is
        // handed over as the link and never followed.
        if std::os::unix::fs::lchown(target, None, Some(gid)).is_err() {
            if let Err(rollback) = std::fs::remove_file(target) {
                tracing::error!(
                    rollback = %rollback,
                    "a signing key file could not be removed after it could not be handed to the daemon's group"
                );
            }
            return Err(failed("the file could not be handed to the daemon's group"));
        }

        Ok(())
    }

    /// Lists the directory, treating one that does not exist as empty.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ZoneDirectoryUnreadable`] for every failure other than
    /// the directory being absent.
    fn zone_file_names(&self, directory: &Path) -> Result<Vec<String>, DnsError> {
        let entries = match std::fs::read_dir(directory) {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
            Err(_) => return Err(DnsError::ZoneDirectoryUnreadable),
        };

        let mut names = Vec::new();
        for entry in entries {
            let entry = entry.map_err(|_| DnsError::ZoneDirectoryUnreadable)?;
            // A name that is not UTF-8 is not a zone file the agent wrote.
            if let Ok(name) = entry.file_name().into_string() {
                names.push(name);
            }
        }
        Ok(names)
    }

    /// Asks the filesystem how much room an unprivileged writer has left.
    ///
    /// # Errors
    ///
    /// Returns the I/O error of the measurement.
    fn available_bytes(&self, directory: &Path) -> std::io::Result<u64> {
        maran_agent_core::utils::available_bytes::available_bytes(directory)
    }
}

#[cfg(test)]
#[path = "../tests/dns/process_zone_host_tests.rs"]
mod tests;
