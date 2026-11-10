//! The in-memory [`SiteLogHost`] the traffic summary and prune tests decide
//! against.
//!
//! The real host opens root's directory under `/var/log/maran/sites`, which a
//! unit test can neither create nor own. What a test can pin is which bytes the
//! summary chooses to count, where it resumes, and which names a prune removes;
//! the descriptor discipline has its own tests against a real temporary
//! directory (`open_site_log_tests.rs`, `process_site_log_host_tests.rs`).

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeMap;
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};

use maran_agent_core::validation::system::name::AccountName;

use crate::sites::{SiteLogDirectory, SiteLogFile, SiteLogHost, SitesOpError};

/// One entry of the fake directory.
#[derive(Debug, Clone)]
pub(crate) enum FakeEntry {
    /// A regular file with an inode and its bytes.
    File {
        /// The inode number the file keeps across a rename.
        inode: u64,
        /// The contents.
        bytes: Vec<u8>,
    },
    /// A subdirectory, which a prune must never remove.
    Directory,
}

/// A directory of site logs held in memory.
#[derive(Debug, Clone, Default)]
pub(crate) struct FakeSiteLogHost {
    /// The entries by name.
    entries: Arc<Mutex<BTreeMap<String, FakeEntry>>>,
    /// The next inode number to hand out.
    next_inode: Arc<AtomicU64>,
    /// How many times a directory was opened.
    opened: Arc<AtomicUsize>,
    /// Whether opening the directory is refused.
    refusing: Arc<Mutex<bool>>,
    /// Whether the directory does not exist at all.
    absent: Arc<Mutex<bool>>,
}

impl FakeSiteLogHost {
    /// An empty directory.
    pub(crate) fn new() -> Self {
        Self::default()
    }

    /// Makes the host refuse to open the directory, as a directory root does
    /// not own would be refused.
    pub(crate) fn refuse_the_directory(&self) {
        *self.refusing.lock().unwrap() = true;
    }

    /// Makes the directory not exist, as an account that never served a request has none.
    pub(crate) fn have_no_directory(&self) {
        *self.absent.lock().unwrap() = true;
    }

    /// Creates or replaces the file `name` with a fresh inode.
    pub(crate) fn put(&self, name: &str, bytes: &[u8]) {
        let inode = self.next_inode.fetch_add(1, Ordering::SeqCst) + 100;
        self.put_with_inode(name, inode, bytes);
    }

    /// Creates or replaces the file `name` with the given inode.
    pub(crate) fn put_with_inode(&self, name: &str, inode: u64, bytes: &[u8]) {
        self.entries.lock().unwrap().insert(
            name.to_owned(),
            FakeEntry::File {
                inode,
                bytes: bytes.to_vec(),
            },
        );
    }

    /// Creates a subdirectory entry.
    pub(crate) fn put_directory(&self, name: &str) {
        self.entries
            .lock()
            .unwrap()
            .insert(name.to_owned(), FakeEntry::Directory);
    }

    /// Appends to the file `name`.
    pub(crate) fn append(&self, name: &str, more: &[u8]) {
        match self.entries.lock().unwrap().get_mut(name) {
            Some(FakeEntry::File { bytes, .. }) => bytes.extend_from_slice(more),
            other => panic!("{name} is not a file: {other:?}"),
        }
    }

    /// What `logrotate` does with `kill -USR1`: the live file becomes
    /// `previous`, keeping its inode, and a new empty live file appears.
    pub(crate) fn rotate(&self, live: &str, previous: &str) {
        let mut entries = self.entries.lock().unwrap();
        let moved = entries.remove(live).expect("the live file exists");
        entries.insert(previous.to_owned(), moved);
        let inode = self.next_inode.fetch_add(1, Ordering::SeqCst) + 100;
        entries.insert(
            live.to_owned(),
            FakeEntry::File {
                inode,
                bytes: Vec::new(),
            },
        );
    }

    /// Every name in the directory, sorted.
    pub(crate) fn names(&self) -> Vec<String> {
        self.entries.lock().unwrap().keys().cloned().collect()
    }

    /// How many times a directory was opened.
    pub(crate) fn opened_directories(&self) -> usize {
        self.opened.load(Ordering::SeqCst)
    }

    /// Whether a file named `name` exists, as an owner of the same pair would find it.
    pub(crate) fn open_if_present(&self, name: &str) -> Option<Box<dyn SiteLogFile>> {
        FakeDirectory { host: self.clone() }
            .open_file(name)
            .unwrap()
    }

    /// Opens the file `name` directly, for tests of the reader.
    pub(crate) fn open(&self, name: &str) -> Box<dyn SiteLogFile> {
        FakeDirectory { host: self.clone() }
            .open_file(name)
            .unwrap()
            .expect("the file exists")
    }
}

impl SiteLogHost for FakeSiteLogHost {
    fn open_directory(
        &self,
        _account: &AccountName,
    ) -> Result<Box<dyn SiteLogDirectory>, SitesOpError> {
        if *self.refusing.lock().unwrap() {
            return Err(SitesOpError::LogUnreadable {
                path: "the site log directory".to_owned(),
            });
        }

        self.opened.fetch_add(1, Ordering::SeqCst);
        Ok(Box::new(FakeDirectory { host: self.clone() }))
    }

    fn open_directory_if_present(
        &self,
        account: &AccountName,
    ) -> Result<Option<Box<dyn SiteLogDirectory>>, SitesOpError> {
        if *self.absent.lock().unwrap() {
            return Ok(None);
        }

        self.open_directory(account).map(Some)
    }
}

/// The directory handle the fake hands out.
struct FakeDirectory {
    /// The host whose entries it reads and removes.
    host: FakeSiteLogHost,
}

impl SiteLogDirectory for FakeDirectory {
    fn open_file(&self, name: &str) -> Result<Option<Box<dyn SiteLogFile>>, SitesOpError> {
        match self.host.entries.lock().unwrap().get(name) {
            None => Ok(None),
            Some(FakeEntry::File { inode, bytes }) => Ok(Some(Box::new(FakeFile {
                inode: *inode,
                bytes: bytes.clone(),
            }))),
            Some(FakeEntry::Directory) => Err(SitesOpError::LogUnreadable {
                path: name.to_owned(),
            }),
        }
    }

    fn list_names(&self) -> Result<Vec<String>, SitesOpError> {
        Ok(self.host.names())
    }

    fn remove_file(&self, name: &str) -> Result<bool, SitesOpError> {
        let mut entries = self.host.entries.lock().unwrap();
        match entries.get(name) {
            Some(FakeEntry::File { .. }) => {
                entries.remove(name);
                Ok(true)
            }
            Some(FakeEntry::Directory) | None => Ok(false),
        }
    }
}

/// A snapshot of a file as it was when opened.
struct FakeFile {
    /// Its inode.
    inode: u64,
    /// Its bytes at open time.
    bytes: Vec<u8>,
}

impl SiteLogFile for FakeFile {
    fn device(&self) -> u64 {
        7
    }

    fn inode(&self) -> u64 {
        self.inode
    }

    fn length(&self) -> Result<u64, SitesOpError> {
        Ok(self.bytes.len() as u64)
    }

    fn read_at(&self, offset: u64, buffer: &mut [u8]) -> Result<usize, SitesOpError> {
        let start = usize::try_from(offset).unwrap_or(usize::MAX);
        if start >= self.bytes.len() {
            return Ok(0);
        }
        let count = buffer.len().min(self.bytes.len() - start);
        buffer[..count].copy_from_slice(&self.bytes[start..start + count]);
        Ok(count)
    }
}

/// One `combined`-format line for 203.0.113.9, newline included.
pub(crate) fn access_line(time_local: &str, status: u16, bytes: u64, target: &str) -> String {
    format!(
        "203.0.113.9 - - [{time_local}] \"GET {target} HTTP/1.1\" {status} {bytes} \"-\" \"probe/1.0\"\n"
    )
}
