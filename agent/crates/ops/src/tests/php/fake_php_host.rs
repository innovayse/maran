//! The in-memory [`PhpHost`] the PHP tests decide against, and the inputs they
//! decide about.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host runs `apt-get`, writes into a root-owned pool directory and
//! restarts a live php-fpm: what a unit test can pin is which content an
//! operation chooses to write, which argv array it chooses to spawn, and when
//! it chooses to do neither. The write protocol has its own tests in
//! `safe_write`.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::php::model::php_override::PhpOverride;
use crate::php::model::pool_input::PoolInput;
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, SafeWriteError};

/// A [`PhpHost`] that keeps the pool directory, the installed versions and the
/// spawned commands in memory.
pub(crate) struct FakePhpHost {
    /// Directories that "exist" — the installed versions' pool directories,
    /// plus every directory the host has been asked to create.
    directories: Mutex<BTreeSet<PathBuf>>,
    /// The pool files, path to content.
    files: Mutex<BTreeMap<PathBuf, String>>,
    /// Every command the host was asked to spawn, as `program` plus its argv.
    commands: Mutex<Vec<Vec<String>>>,
    /// `php-fpm -t`'s answer, and what it says when it refuses.
    validation: Mutex<(i32, String)>,
    /// The status and standard error every spawned command reports.
    command: Mutex<(i32, String)>,
    /// How many times a write actually reached the protocol — the number an
    /// idempotence test pins.
    writes: Mutex<usize>,
    /// How many times a removal actually reached the protocol, i.e. how many
    /// pools were really there to remove.
    removals: Mutex<usize>,
    /// Directories the host was asked to create as an account.
    created_as_account: Mutex<Vec<PathBuf>>,
    /// The mode the host was asked to give each directory it created.
    modes: Mutex<BTreeMap<PathBuf, u32>>,
}

impl FakePhpHost {
    /// A host with nothing installed and a validator that accepts everything.
    pub(crate) fn empty() -> Self {
        Self {
            directories: Mutex::new(BTreeSet::new()),
            files: Mutex::new(BTreeMap::new()),
            commands: Mutex::new(Vec::new()),
            validation: Mutex::new((0, String::new())),
            command: Mutex::new((0, String::new())),
            writes: Mutex::new(0),
            removals: Mutex::new(0),
            created_as_account: Mutex::new(Vec::new()),
            modes: Mutex::new(BTreeMap::new()),
        }
    }

    /// A host with `versions` already installed, i.e. with their pool
    /// directories present — which is exactly what the operations read as
    /// "installed".
    pub(crate) fn with_installed(versions: &[&str]) -> Self {
        let host = Self::empty();
        {
            let mut directories = host.directories.lock().unwrap();
            for version in versions {
                directories.insert(PathBuf::from(distro().php_fpm_pool_directory(version)));
            }
        }
        host
    }

    /// Makes every spawned command fail, with `stderr` as the reason an
    /// operator would read in the log.
    pub(crate) fn reject_commands(&self, stderr: &str) {
        *self.command.lock().unwrap() = (1, stderr.to_owned());
    }

    /// Makes `php-fpm -t` refuse every pool, with `stderr` as the reason.
    pub(crate) fn reject_validation(&self, stderr: &str) {
        *self.validation.lock().unwrap() = (1, stderr.to_owned());
    }

    /// The content of a pool file, if the host holds one.
    pub(crate) fn config(&self, path: &Path) -> Option<String> {
        self.files.lock().unwrap().get(path).cloned()
    }

    /// Every command the host was asked to spawn.
    pub(crate) fn commands(&self) -> Vec<Vec<String>> {
        self.commands.lock().unwrap().clone()
    }

    /// The directories the host was asked to create as the account.
    pub(crate) fn created_as_account(&self) -> Vec<PathBuf> {
        self.created_as_account.lock().unwrap().clone()
    }

    /// The mode the host was asked to give `path`, if it created it.
    pub(crate) fn mode(&self, path: &Path) -> Option<u32> {
        self.modes.lock().unwrap().get(path).copied()
    }

    /// How many writes reached the protocol.
    /// How many removals reached the protocol.
    pub(crate) fn removals(&self) -> usize {
        *self.removals.lock().unwrap()
    }

    pub(crate) fn writes(&self) -> usize {
        *self.writes.lock().unwrap()
    }
}

impl ConfigHost for FakePhpHost {
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        let mut command = vec![program.to_owned()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.commands.lock().unwrap().push(command);

        let (status, stderr) = self.command.lock().unwrap().clone();
        Ok(CommandOutcome {
            status,
            stdout: String::new(),
            stderr,
        })
    }
}

impl PhpHost for FakePhpHost {
    fn directory_exists(&self, path: &Path) -> bool {
        self.directories.lock().unwrap().contains(path)
    }

    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), PhpOpError> {
        self.directories.lock().unwrap().insert(path.to_path_buf());
        self.modes.lock().unwrap().insert(path.to_path_buf(), mode);
        Ok(())
    }

    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        directories: &[&Path],
        mode: u32,
    ) -> Result<(), PhpOpError> {
        let mut created = self.created_as_account.lock().unwrap();
        let mut known = self.directories.lock().unwrap();
        let mut modes = self.modes.lock().unwrap();
        for directory in directories {
            created.push(directory.to_path_buf());
            known.insert(directory.to_path_buf());
            modes.insert(directory.to_path_buf(), mode);
        }
        Ok(())
    }

    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        *self.writes.lock().unwrap() += 1;

        let (status, stderr) = self.validation.lock().unwrap().clone();
        if status != 0 {
            // The real protocol restores the previous content before
            // returning, so the fake leaves the map untouched.
            return Err(PhpOpError::PoolValidation { stderr });
        }

        self.files
            .lock()
            .unwrap()
            .insert(target.to_path_buf(), contents.to_owned());
        Ok(())
    }

    fn remove_config(
        &self,
        target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        // Absent is a success that runs NOTHING, exactly as the real protocol
        // decides — so a test can tell "removed a pool" from "was asked about a
        // pool that never existed", which is the difference between a reload
        // that happened and one that did not.
        if !self.files.lock().unwrap().contains_key(target) {
            return Ok(());
        }

        *self.removals.lock().unwrap() += 1;

        let (status, stderr) = self.validation.lock().unwrap().clone();
        if status != 0 {
            // The real protocol puts the bytes back before returning, so the
            // fake leaves the map untouched.
            return Err(PhpOpError::PoolValidation { stderr });
        }

        self.files.lock().unwrap().remove(target);
        Ok(())
    }
}

/// The adapter every test in this folder runs against. Which family is
/// immaterial to the decisions being tested; that the facts come from the
/// adapter rather than a literal is the point.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// A pool for `acme` at 8.3 with `overrides` and a ten-worker budget.
pub(crate) fn pool_input(overrides: Vec<PhpOverride>) -> PoolInput {
    PoolInput {
        account: AccountName::parse("acme").unwrap(),
        version: PhpVersion::parse("8.3").unwrap(),
        max_children: 10,
        overrides,
    }
}
