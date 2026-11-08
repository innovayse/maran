//! The [`SiteLogHost`] that actually touches this machine.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::fd::AsRawFd;
use std::os::unix::fs::{FileExt, MetadataExt};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::system::name::AccountName;

use crate::sites::SitesOpError;
use crate::sites::open_site_log::open_site_log;
use crate::sites::open_site_log_directory::{LOG_OWNER_UID, open_site_log_directory};
use crate::sites::site_log_directory::SiteLogDirectory;
use crate::sites::site_log_file::SiteLogFile;
use crate::sites::site_log_host::SiteLogHost;

/// Opens the real `/var/log/maran/sites/<account>`.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: every decision worth reviewing — what to count,
/// where to resume, which names to remove — lives in the operations, where it
/// is tested against a fake. It forks nothing: the files are root's, in a tree
/// no unprivileged uid can reach, so root reads and unlinks them directly.
#[derive(Debug, Default, Clone, Copy)]
pub struct ProcessSiteLogHost;

impl ProcessSiteLogHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl SiteLogHost for ProcessSiteLogHost {
    /// Opens the account's log directory by path, once, with `O_NOFOLLOW`, and
    /// proves root owns it. Every later name goes through the descriptor.
    fn open_directory(
        &self,
        account: &AccountName,
    ) -> Result<Box<dyn SiteLogDirectory>, SitesOpError> {
        let path = AgentPaths::account_site_log_dir(account);
        let file = open_site_log_directory(&path, LOG_OWNER_UID)?;

        Ok(Box::new(ProcessLogDirectory {
            file,
            uid: LOG_OWNER_UID,
        }))
    }
}

/// An open site-log directory.
struct ProcessLogDirectory {
    /// The descriptor every name is reached through.
    file: File,
    /// The uid every entry must be owned by: root in production, a test's own
    /// uid in the tests, which cannot be root.
    uid: u32,
}

impl SiteLogDirectory for ProcessLogDirectory {
    /// Opens `name` through the held descriptor with the discipline of
    /// [`open_site_log`].
    fn open_file(&self, name: &str) -> Result<Option<Box<dyn SiteLogFile>>, SitesOpError> {
        let opened = open_site_log(&self.file, OsStr::new(name), self.uid)?;

        Ok(opened.map(|(file, metadata)| {
            Box::new(ProcessLogFile {
                file,
                device: metadata.dev(),
                inode: metadata.ino(),
                name: name.to_owned(),
            }) as Box<dyn SiteLogFile>
        }))
    }

    /// Lists the held directory through `/proc/self/fd/<n>`, the kernel's name
    /// for this very inode, so the listing and the removals that follow describe
    /// the same directory even if its path has been swapped since. Entries whose
    /// name is not UTF-8 are skipped: none of the names this area matches can be.
    fn list_names(&self) -> Result<Vec<String>, SitesOpError> {
        let held = format!("/proc/self/fd/{}", self.file.as_raw_fd());
        let unreadable = || SitesOpError::LogUnreadable {
            path: "the site log directory".to_owned(),
        };

        let mut names = Vec::new();
        for entry in std::fs::read_dir(held).map_err(|_| unreadable())? {
            let entry = entry.map_err(|_| unreadable())?;
            if let Some(name) = entry.file_name().to_str() {
                names.push(name.to_owned());
            }
        }

        Ok(names)
    }

    /// Unlinks `name` by name through the held descriptor, after the no-follow
    /// `fstatat` showed it is a regular file. A directory, a symlink or any
    /// other kind is left alone and reported as nothing removed.
    fn remove_file(&self, name: &str) -> Result<bool, SitesOpError> {
        let name = OsStr::new(name);

        let stat = match stat_in_directory(&self.file, name) {
            Ok(stat) => stat,
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(false),
            Err(_) => return Err(SitesOpError::unreadable_log(name)),
        };
        if stat.kind != EntryKind::File {
            return Ok(false);
        }

        match remove_file_in_directory(&self.file, name) {
            Ok(()) => Ok(true),
            Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(false),
            Err(_) => Err(SitesOpError::unreadable_log(name)),
        }
    }
}

/// An open site log.
struct ProcessLogFile {
    /// The opened, proven regular file.
    file: File,
    /// The device it lives on, read once at open.
    device: u64,
    /// Its inode number, read once at open.
    inode: u64,
    /// Its name in the directory, for an error's sake.
    name: String,
}

impl SiteLogFile for ProcessLogFile {
    fn device(&self) -> u64 {
        self.device
    }

    fn inode(&self) -> u64 {
        self.inode
    }

    /// The length of the open inode right now.
    fn length(&self) -> Result<u64, SitesOpError> {
        self.file
            .metadata()
            .map(|metadata| metadata.len())
            .map_err(|_| SitesOpError::unreadable_log(OsStr::new(&self.name)))
    }

    /// A positioned read; the file's own offset is never moved.
    fn read_at(&self, offset: u64, buffer: &mut [u8]) -> Result<usize, SitesOpError> {
        FileExt::read_at(&self.file, buffer, offset)
            .map_err(|_| SitesOpError::unreadable_log(OsStr::new(&self.name)))
    }
}

#[cfg(test)]
#[path = "../tests/sites/process_site_log_host_tests.rs"]
mod tests;
