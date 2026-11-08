//! The [`ZoneHost`] that actually touches this machine.

use std::path::Path;

use crate::dns::dns_error::DnsError;
use crate::dns::process_dns_host::ProcessDnsHost;
use crate::dns::zone_host::ZoneHost;
use crate::dns::zone_write_error::zone_write_error;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::write_config_set;

/// The mode a zone file lands at: readable by the daemon's unprivileged user,
/// writable by root alone.
const ZONE_FILE_MODE: u32 = 0o644;

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
