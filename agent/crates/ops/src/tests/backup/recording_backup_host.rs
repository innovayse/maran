//! The [`BackupHost`] the backup tests drive, and what it records.

// A fake's lock can only be poisoned by a failing test, and a failing assertion
// IS the reporting mechanism there.
#![allow(clippy::unwrap_used)]

use std::fs::{OpenOptions, Permissions, create_dir_all, set_permissions};
use std::io::Write as _;
use std::os::unix::fs::PermissionsExt as _;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::archive::dump_database::dump_arguments;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::model::extract_spec::ExtractSpec;
use crate::test_support::recording_commands::RecordingCommands;

/// The program name this fake records a dump under.
///
/// A made-up name and not `mariadb-dump`: which binary a family really ships is
/// the distro adapter's answer and is asserted in that crate's tests, and a
/// platform literal has no business in this one.
pub(crate) const DUMP_PROGRAM: &str = "dump-client";

/// The program name this fake records an archive run under.
pub(crate) const ARCHIVE_PROGRAM: &str = "archiver";

/// The program name this fake records a member listing under.
pub(crate) const LIST_PROGRAM: &str = "archive-lister";

/// The program name this fake records an extraction under.
pub(crate) const EXTRACT_PROGRAM: &str = "extractor";

/// The compressor path this fake hands the two argv builders.
///
/// A made-up ABSOLUTE path, for both halves of the reason. Absolute, because
/// that is the property the real host's `gzip_binary()` supplies and the
/// property a test asserting on a recorded argv is there to observe. Made up,
/// because `/usr/bin/gzip` is the distro adapter's answer and a platform
/// literal has no business in this crate.
pub(crate) const COMPRESSOR_PROGRAM: &str = "/fake/bin/compressor";

/// The program name this fake records a `DROP DATABASE` under.
pub(crate) const DROP_PROGRAM: &str = "db-drop";

/// The program name this fake records a dump load under.
pub(crate) const LOAD_PROGRAM: &str = "db-load";

/// The instant this fake's clock always answers.
pub(crate) const FIXED_NOW: i64 = 1_767_225_600;

/// A [`BackupHost`] that records every argv and writes plausible files.
///
/// It writes real bytes into the paths it is given, because the operation hashes
/// and measures what landed rather than believing what it was told — a fake that
/// only recorded would leave every checksum, size and ceiling assertion untested.
pub(crate) struct RecordingBackupHost {
    /// The shared record-the-argv core.
    commands: RecordingCommands,
    /// What each dump writes into its result file.
    dump_contents: Mutex<String>,
    /// When set, a dump writes its contents and THEN fails with this status —
    /// the case a fake that only failed early could never exhibit.
    dump_failure: Mutex<Option<i32>>,
    /// When set, the archiver writes nothing and fails with this status.
    archive_failure: Mutex<Option<i32>>,
    /// What the archiver writes into the artifact.
    archive_contents: Mutex<String>,
    /// When set, the archiver leaves the artifact carrying this mode — a real
    /// program handed a path can leave whatever it likes behind.
    artifact_mode: Mutex<Option<u32>>,
    /// The member names `list_members` answers with.
    members: Mutex<Vec<String>>,
    /// The manifest an extraction of `manifest.json` writes into the scratch.
    archive_manifest: Mutex<Option<BackupManifest>>,
    /// The dump files an extraction of `databases/` writes, as
    /// (database name, contents).
    archive_dumps: Mutex<Vec<(String, String)>>,
    /// Which member was extracted, and as whom — the log the R3/R4 test reads.
    extractions: Mutex<Vec<(String, ExtractIdentity)>>,
    /// The databases dropped, in order.
    dropped: Mutex<Vec<String>>,
    /// The dumps loaded, in order, as (database name, file).
    loaded: Mutex<Vec<(String, PathBuf)>>,
    /// When set, loading the ARCHIVE's dump for this database fails.
    fail_archive_load_for: Mutex<Option<String>>,
    /// When set, reloading any ROLLBACK dump fails.
    fail_rollback_loads: Mutex<bool>,
}

impl RecordingBackupHost {
    /// A host whose dumps and archives all succeed.
    pub(crate) fn new() -> Self {
        Self {
            commands: RecordingCommands::new(),
            dump_contents: Mutex::new("-- dump\n".to_owned()),
            dump_failure: Mutex::new(None),
            archive_failure: Mutex::new(None),
            archive_contents: Mutex::new("gzip-bytes".to_owned()),
            artifact_mode: Mutex::new(None),
            members: Mutex::new(vec![
                "manifest.json".to_owned(),
                "home/".to_owned(),
                "databases/".to_owned(),
            ]),
            archive_manifest: Mutex::new(None),
            archive_dumps: Mutex::new(Vec::new()),
            extractions: Mutex::new(Vec::new()),
            dropped: Mutex::new(Vec::new()),
            loaded: Mutex::new(Vec::new()),
            fail_archive_load_for: Mutex::new(None),
            fail_rollback_loads: Mutex::new(false),
        }
    }

    /// Makes `list_members` answer exactly these names.
    pub(crate) fn holds_members(&self, names: &[&str]) {
        *self.members.lock().unwrap() = names.iter().map(|name| (*name).to_owned()).collect();
    }

    /// Makes an extraction of `manifest.json` write this manifest.
    pub(crate) fn holds_manifest(&self, manifest: BackupManifest) {
        *self.archive_manifest.lock().unwrap() = Some(manifest);
    }

    /// Makes an extraction of `databases/` write this dump for `database`.
    pub(crate) fn holds_dump(&self, database: &str, contents: &str) {
        self.archive_dumps
            .lock()
            .unwrap()
            .push((database.to_owned(), contents.to_owned()));
    }

    /// Makes the load of the archive's dump for `database` fail.
    pub(crate) fn fail_archive_load_for(&self, database: &str) {
        *self.fail_archive_load_for.lock().unwrap() = Some(database.to_owned());
    }

    /// Makes every reload of a rollback dump fail.
    pub(crate) fn fail_rollback_loads(&self) {
        *self.fail_rollback_loads.lock().unwrap() = true;
    }

    /// Which member was extracted, and as whom, in order.
    pub(crate) fn extractions(&self) -> Vec<(String, ExtractIdentity)> {
        self.extractions.lock().unwrap().clone()
    }

    /// The databases dropped, in order.
    pub(crate) fn dropped(&self) -> Vec<String> {
        self.dropped.lock().unwrap().clone()
    }

    /// The dumps loaded, in order.
    pub(crate) fn loaded(&self) -> Vec<(String, PathBuf)> {
        self.loaded.lock().unwrap().clone()
    }

    /// Makes every dump write its bytes and then exit non-zero.
    pub(crate) fn fail_dumps_after_writing(&self, status: i32) {
        *self.dump_failure.lock().unwrap() = Some(status);
    }

    /// Makes the archiver exit non-zero.
    pub(crate) fn fail_archive(&self, status: i32) {
        *self.archive_failure.lock().unwrap() = Some(status);
    }

    /// Makes the archiver leave the artifact readable by everybody.
    pub(crate) fn loosen_artifact_mode(&self, mode: u32) {
        *self.artifact_mode.lock().unwrap() = Some(mode);
    }

    /// Every recorded argv, in order.
    pub(crate) fn calls(&self) -> Vec<Vec<String>> {
        self.commands.calls()
    }

    /// The recorded argvs for one program.
    pub(crate) fn calls_to(&self, program: &str) -> Vec<Vec<String>> {
        self.commands.calls_to(program)
    }
}

impl BackupHost for RecordingBackupHost {
    fn now_unix(&self) -> i64 {
        FIXED_NOW
    }

    fn dump_database(&self, database: &DatabaseName, into: &Path) -> Result<u64, BackupError> {
        let arguments = dump_arguments(database, into);
        let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();
        self.commands.record(DUMP_PROGRAM, &borrowed);

        let contents = self.dump_contents.lock().unwrap().clone();
        write_file(into, &contents);

        match *self.dump_failure.lock().unwrap() {
            Some(status) => Err(BackupError::DumpFailed { status }),
            None => Ok(contents.len() as u64),
        }
    }

    fn create_archive(&self, spec: &ArchiveSpec) -> Result<u64, BackupError> {
        let arguments = spec.arguments(COMPRESSOR_PROGRAM);
        let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();
        self.commands.record(ARCHIVE_PROGRAM, &borrowed);

        if let Some(status) = *self.archive_failure.lock().unwrap() {
            return Err(BackupError::ArchiveFailed { status });
        }

        let contents = self.archive_contents.lock().unwrap().clone();
        write_file(&spec.artifact, &contents);
        if let Some(mode) = *self.artifact_mode.lock().unwrap() {
            set_permissions(&spec.artifact, Permissions::from_mode(mode)).unwrap();
        }

        Ok(contents.len() as u64)
    }

    fn list_members(&self, artifact: &Path) -> Result<Vec<String>, BackupError> {
        self.commands
            .record(LIST_PROGRAM, &[&artifact.to_string_lossy()]);

        Ok(self.members.lock().unwrap().clone())
    }

    fn extract(&self, spec: &ExtractSpec) -> Result<(), BackupError> {
        let arguments = spec.arguments(COMPRESSOR_PROGRAM);
        let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();
        self.commands.record(EXTRACT_PROGRAM, &borrowed);
        self.extractions
            .lock()
            .unwrap()
            .push((spec.member().to_owned(), spec.identity()));

        match spec.part {
            ArchivePart::Manifest => {
                if let Some(manifest) = self.archive_manifest.lock().unwrap().as_ref() {
                    create_dir_all(&spec.into).unwrap();
                    let rendered = serde_json::to_string_pretty(manifest).unwrap();
                    write_file(&spec.into.join("manifest.json"), &rendered);
                }
            }
            ArchivePart::Databases => {
                let directory = spec.into.join("databases");
                create_dir_all(&directory).unwrap();
                for (name, contents) in self.archive_dumps.lock().unwrap().iter() {
                    write_file(&directory.join(format!("{name}.sql")), contents);
                }
            }
            ArchivePart::Home { .. } => {
                create_dir_all(spec.into.join("public")).unwrap();
                write_file(&spec.into.join("public").join("index.php"), "<?php");
            }
        }

        Ok(())
    }

    fn drop_database(&self, database: &DatabaseName) -> Result<(), BackupError> {
        self.commands.record(DROP_PROGRAM, &[database.as_str()]);
        self.dropped
            .lock()
            .unwrap()
            .push(database.as_str().to_owned());

        Ok(())
    }

    fn load_dump(&self, database: &DatabaseName, from: &Path) -> Result<(), BackupError> {
        self.commands
            .record(LOAD_PROGRAM, &[database.as_str(), &from.to_string_lossy()]);
        self.loaded
            .lock()
            .unwrap()
            .push((database.as_str().to_owned(), from.to_path_buf()));

        // A real client is handed a file, and a file that is not there is a
        // load that fails. Answering Ok for a missing dump would make a
        // rollback that never had a dump to reload look exactly like one that
        // worked.
        if !from.is_file() {
            return Err(BackupError::LoadFailed { status: -1 });
        }

        if is_rollback(from) {
            if *self.fail_rollback_loads.lock().unwrap() {
                return Err(BackupError::LoadFailed { status: 1 });
            }
        } else if self.fail_archive_load_for.lock().unwrap().as_deref() == Some(database.as_str()) {
            return Err(BackupError::LoadFailed { status: 1 });
        }

        Ok(())
    }
}

/// Whether a path is inside the scratch's rollback directory.
fn is_rollback(path: &Path) -> bool {
    path.components()
        .any(|component| component.as_os_str() == "rollback")
}

/// Writes `contents` into `path`, keeping the mode the operation created it
/// with — which is what the real programs do to a file they were handed.
fn write_file(path: &Path, contents: &str) {
    let mut file = OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(true)
        .open(path)
        .unwrap();
    file.write_all(contents.as_bytes()).unwrap();
}
