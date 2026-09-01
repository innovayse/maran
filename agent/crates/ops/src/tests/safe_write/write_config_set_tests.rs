//! Tests for [`write_config_set`].
//!
//! The set write exists because a certificate and its key must land together;
//! every test here defends one half of that promise — all of them swapped in
//! before anything validates, all of them restored when it refuses, and each of
//! them at the mode it was asked for, from the moment it exists.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;
use std::sync::Mutex;

use crate::safe_write::model::{ConfigFile, Reload, Validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, write_config_set};

/// A [`ConfigHost`] that records what it was asked to run and can be told to
/// refuse the validation.
struct FakeConfigHost {
    validation_status: i32,
    /// What the host saw on disk at the moment it was asked to validate — the
    /// only way to prove BOTH files were in place before nginx was consulted.
    seen: Mutex<Vec<(String, String)>>,
    watched: Vec<std::path::PathBuf>,
    calls: Mutex<usize>,
}

impl FakeConfigHost {
    fn passing(watched: &[&Path]) -> Self {
        Self {
            validation_status: 0,
            seen: Mutex::new(Vec::new()),
            watched: watched.iter().map(|path| path.to_path_buf()).collect(),
            calls: Mutex::new(0),
        }
    }

    fn failing_validation(watched: &[&Path]) -> Self {
        Self {
            validation_status: 1,
            ..Self::passing(watched)
        }
    }

    fn calls(&self) -> usize {
        *self.calls.lock().unwrap()
    }

    fn seen(&self) -> Vec<(String, String)> {
        self.seen.lock().unwrap().clone()
    }
}

impl ConfigHost for FakeConfigHost {
    fn run(&self, _program: &str, _arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        let mut calls = self.calls.lock().unwrap();
        *calls += 1;
        let first = *calls == 1;
        drop(calls);

        if first {
            let snapshot = self
                .watched
                .iter()
                .map(|path| {
                    (
                        path.display().to_string(),
                        std::fs::read_to_string(path).unwrap_or_default(),
                    )
                })
                .collect();
            *self.seen.lock().unwrap() = snapshot;
        }

        Ok(CommandOutcome {
            // The validator is asked first, so anything after it is the reload.
            status: if first { self.validation_status } else { 0 },
            stdout: String::new(),
            stderr: "nginx: [emerg] SSL_CTX_use_PrivateKey_file failed".to_owned(),
        })
    }
}

fn validator() -> Validator<'static> {
    Validator {
        program: "/usr/sbin/nginx",
        arguments: &["-t"],
    }
}

fn reload() -> Reload<'static> {
    Reload {
        program: "/usr/bin/systemctl",
        arguments: &["reload", "nginx"],
    }
}

/// The mode of `path`, masked to the permission bits.
fn mode_of(path: &Path) -> u32 {
    std::fs::metadata(path).unwrap().permissions().mode() & 0o777
}

#[test]
fn every_file_is_in_place_before_the_validator_is_asked_anything() {
    // The whole reason this function exists. Validating between the two halves
    // of a certificate change makes nginx compare a new key against an old
    // certificate, which fails with `key values mismatch` — so a perfectly
    // valid renewal is reported as a validation failure, every ninety days.
    let directory = tempfile::tempdir().unwrap();
    let key = directory.path().join("privkey.pem");
    let certificate = directory.path().join("fullchain.pem");
    std::fs::write(&key, "OLD KEY\n").unwrap();
    std::fs::write(&certificate, "OLD CERTIFICATE\n").unwrap();
    let host = FakeConfigHost::passing(&[&key, &certificate]);

    write_config_set(
        &host,
        &[
            ConfigFile {
                target: &key,
                contents: "NEW KEY\n",
                mode: 0o600,
            },
            ConfigFile {
                target: &certificate,
                contents: "NEW CERTIFICATE\n",
                mode: 0o644,
            },
        ],
        &validator(),
        &reload(),
    )
    .unwrap();

    let seen: Vec<String> = host.seen().into_iter().map(|(_, body)| body).collect();
    assert_eq!(seen, vec!["NEW KEY\n", "NEW CERTIFICATE\n"]);
    // One validation and one reload for the whole set, not one per file.
    assert_eq!(host.calls(), 2);
}

#[test]
fn each_file_lands_at_the_mode_it_was_asked_for() {
    let directory = tempfile::tempdir().unwrap();
    let key = directory.path().join("privkey.pem");
    let certificate = directory.path().join("fullchain.pem");
    let host = FakeConfigHost::passing(&[]);

    write_config_set(
        &host,
        &[
            ConfigFile {
                target: &key,
                contents: "KEY\n",
                mode: 0o600,
            },
            ConfigFile {
                target: &certificate,
                contents: "CERTIFICATE\n",
                mode: 0o644,
            },
        ],
        &validator(),
        &reload(),
    )
    .unwrap();

    // The real `st_mode`, not a promise in a doc comment: this is the entire
    // protection of a private key on this host.
    assert_eq!(mode_of(&key), 0o600);
    assert_eq!(mode_of(&certificate), 0o644);
}

#[test]
fn a_file_replaced_at_a_wider_mode_is_narrowed_by_the_write() {
    // A key left at 0644 by an older agent, a restore from a backup, or a
    // careless operator must be corrected by the next write rather than
    // inherited: the protocol sets the mode on the temporary file, so the
    // target's mode is the one that was asked for and never the one it had.
    let directory = tempfile::tempdir().unwrap();
    let key = directory.path().join("privkey.pem");
    std::fs::write(&key, "OLD KEY\n").unwrap();
    std::fs::set_permissions(&key, std::fs::Permissions::from_mode(0o644)).unwrap();
    let host = FakeConfigHost::passing(&[]);

    write_config_set(
        &host,
        &[ConfigFile {
            target: &key,
            contents: "NEW KEY\n",
            mode: 0o600,
        }],
        &validator(),
        &reload(),
    )
    .unwrap();

    assert_eq!(mode_of(&key), 0o600);
}

#[test]
fn a_refused_set_restores_every_file_and_not_merely_the_last() {
    let directory = tempfile::tempdir().unwrap();
    let key = directory.path().join("privkey.pem");
    let certificate = directory.path().join("fullchain.pem");
    std::fs::write(&key, "OLD KEY\n").unwrap();
    std::fs::write(&certificate, "OLD CERTIFICATE\n").unwrap();
    let host = FakeConfigHost::failing_validation(&[]);

    let refused = write_config_set(
        &host,
        &[
            ConfigFile {
                target: &key,
                contents: "NEW KEY\n",
                mode: 0o600,
            },
            ConfigFile {
                target: &certificate,
                contents: "NEW CERTIFICATE\n",
                mode: 0o644,
            },
        ],
        &validator(),
        &reload(),
    );

    assert!(matches!(
        refused,
        Err(SafeWriteError::ValidationFailed { .. })
    ));
    // A half-restored set is a mismatched pair, which is the state the whole
    // function exists to make impossible.
    assert_eq!(std::fs::read_to_string(&key).unwrap(), "OLD KEY\n");
    assert_eq!(
        std::fs::read_to_string(&certificate).unwrap(),
        "OLD CERTIFICATE\n"
    );
}

#[test]
fn a_refused_set_that_had_no_previous_files_leaves_none_behind() {
    let directory = tempfile::tempdir().unwrap();
    let key = directory.path().join("privkey.pem");
    let certificate = directory.path().join("fullchain.pem");
    let host = FakeConfigHost::failing_validation(&[]);

    let refused = write_config_set(
        &host,
        &[
            ConfigFile {
                target: &key,
                contents: "NEW KEY\n",
                mode: 0o600,
            },
            ConfigFile {
                target: &certificate,
                contents: "NEW CERTIFICATE\n",
                mode: 0o644,
            },
        ],
        &validator(),
        &reload(),
    );

    assert!(refused.is_err());
    // A rejected private key left on disk is a secret nothing accounts for.
    assert!(!key.exists());
    assert!(!certificate.exists());
}

#[test]
fn an_empty_set_changes_nothing_and_reloads_nothing() {
    let host = FakeConfigHost::passing(&[]);

    write_config_set(&host, &[], &validator(), &reload()).unwrap();

    assert_eq!(host.calls(), 0);
}
