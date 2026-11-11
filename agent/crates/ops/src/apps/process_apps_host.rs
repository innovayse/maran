//! The [`AppsHost`] that touches this machine: the PHP area's process host, which already spawns
//! programs and writes configuration, plus two filesystem questions.

use std::path::Path;

use crate::apps::{AppsHost, AppsOpError};
use crate::php::{PhpOpError, ProcessPhpHost};
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::write_unchecked_config;

/// The mode of the agent's records: root writes them, anyone may read what they say.
const RECORD_MODE: u32 = 0o644;

impl AppsHost for ProcessPhpHost {
    fn path_exists(&self, path: &Path) -> bool {
        path.exists()
    }

    /// Reads the directory as root. The directories asked about are the agent's own
    /// (`/etc/maran/...`), never a customer's path.
    fn directory_entries(&self, path: &Path) -> Result<Vec<String>, AppsOpError> {
        let unreadable = |error: std::io::Error| AppsOpError::HostUnreadable {
            path: path.display().to_string(),
            reason: error.to_string(),
        };
        let entries = match std::fs::read_dir(path) {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
            Err(error) => return Err(unreadable(error)),
        };
        let mut names = Vec::new();
        for entry in entries {
            // A name that is not UTF-8 is none of the agent's (it writes ASCII names only).
            if let Ok(name) = entry.map_err(unreadable)?.file_name().into_string() {
                names.push(name);
            }
        }
        Ok(names)
    }

    fn write_record(&self, path: &Path, contents: &str) -> Result<(), AppsOpError> {
        write_unchecked_config(&ConfigFile {
            target: path,
            contents,
            mode: RECORD_MODE,
        })
        .map_err(|error| AppsOpError::Php(PhpOpError::from(error)))
    }

    fn remove_record(&self, path: &Path) -> Result<(), AppsOpError> {
        match std::fs::remove_file(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(error) => Err(AppsOpError::Php(PhpOpError::ConfigWrite {
                reason: format!("could not remove {}: {error}", path.display()),
            })),
        }
    }
}
