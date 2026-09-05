//! Tests for [`remove_config`].
//!
//! Removing a configuration is a change to the tree like writing one, so it
//! has the same failure to defend against: a validator or a reload that
//! refuses AFTER the file is gone must not leave the web server holding a
//! configuration it cannot start from.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, remove_config};

/// A [`ConfigHost`] whose validator can be told to refuse, and which records
/// whether the reload was reached at all.
struct FakeConfigHost {
    validation_status: i32,
    calls: Mutex<Vec<String>>,
}

impl FakeConfigHost {
    /// A host that accepts the tree once the file is gone.
    fn passing() -> Self {
        Self {
            validation_status: 0,
            calls: Mutex::new(Vec::new()),
        }
    }

    /// A host whose validator refuses whatever it is shown.
    fn failing_validation() -> Self {
        Self {
            validation_status: 1,
            ..Self::passing()
        }
    }

    /// The programs the host was asked to run, in order.
    fn calls(&self) -> Vec<String> {
        self.calls.lock().unwrap().clone()
    }
}

impl ConfigHost for FakeConfigHost {
    fn run(&self, program: &str, _arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        self.calls.lock().unwrap().push(program.to_owned());
        let status = if program == "validator" {
            self.validation_status
        } else {
            0
        };

        Ok(CommandOutcome {
            status,
            stdout: String::new(),
            stderr: "nginx: [emerg] still referenced".to_owned(),
        })
    }
}

/// The validator and reload every test here passes in.
fn commands() -> (Validator<'static>, Reload<'static>) {
    (
        Validator {
            program: "validator",
            arguments: &["-t"],
        },
        Reload {
            program: "reloader",
            arguments: &["reload", "web"],
        },
    )
}

#[test]
fn a_removal_that_validates_deletes_the_file_and_reloads_once() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, "server {}\n").unwrap();
    let host = FakeConfigHost::passing();
    let (validator, reload) = commands();

    remove_config(&host, &target, &validator, &reload).unwrap();

    assert!(!target.exists());
    assert_eq!(host.calls(), vec!["validator", "reloader"]);
}

#[test]
fn a_removal_the_validator_refuses_puts_the_file_back() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, "server {}\n").unwrap();
    let host = FakeConfigHost::failing_validation();
    let (validator, reload) = commands();

    let outcome = remove_config(&host, &target, &validator, &reload);

    match outcome {
        Err(SafeWriteError::ValidationFailed { stderr }) => {
            assert!(stderr.contains("still referenced"), "got {stderr}");
        }
        other => panic!("expected ValidationFailed, got {other:?}"),
    }
    assert_eq!(std::fs::read_to_string(&target).unwrap(), "server {}\n");
    // The reload was never reached: an invalid tree is not reloaded.
    assert_eq!(host.calls(), vec!["validator"]);
}

#[test]
fn removing_a_file_that_is_already_gone_runs_nothing() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("absent.conf");
    let host = FakeConfigHost::passing();
    let (validator, reload) = commands();

    remove_config(&host, &target, &validator, &reload).unwrap();

    // No reload to reach a state the machine is already in: the caller, not
    // this function, decides whether an absent file is a converged retry.
    assert!(host.calls().is_empty());
}
