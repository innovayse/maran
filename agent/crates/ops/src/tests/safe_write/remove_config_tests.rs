//! Tests for [`remove_config`].
//!
//! Removing a configuration is a change to the tree like writing one, so it
//! has the same failure to defend against: a validator or a reload that
//! refuses AFTER the file is gone must not leave the web server holding a
//! configuration it cannot start from.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::safe_write::adapter_programs::{reload, timed_out, validator};
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, remove_config};

/// A [`ConfigHost`] whose validator can be told to refuse, and which records
/// whether the reload was reached at all.
struct FakeConfigHost {
    validation_status: i32,
    /// Whether the validator is killed at its deadline instead of answering.
    validator_times_out: bool,
    /// Whether the reload is killed at its deadline instead of answering.
    reload_times_out: bool,
    /// The program of every call, in order.
    calls: Mutex<Vec<String>>,
    /// The class of every call, in order.
    classes: Mutex<Vec<CallClass>>,
}

impl FakeConfigHost {
    /// A host that accepts the tree once the file is gone.
    fn passing() -> Self {
        Self {
            validation_status: 0,
            validator_times_out: false,
            reload_times_out: false,
            calls: Mutex::new(Vec::new()),
            classes: Mutex::new(Vec::new()),
        }
    }

    /// A host whose validator outlives its deadline.
    fn validator_timing_out() -> Self {
        Self {
            validator_times_out: true,
            ..Self::passing()
        }
    }

    /// A host whose validator accepts and whose reload outlives its deadline.
    fn reload_timing_out() -> Self {
        Self {
            reload_times_out: true,
            ..Self::passing()
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
    fn run(
        &self,
        program: &AdapterBinary,
        _arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        self.calls.lock().unwrap().push(program.to_string());
        self.classes.lock().unwrap().push(class);
        // The validator is the one call that is not a reload.
        let is_validator = class != CallClass::Reload;
        if is_validator && self.validator_times_out {
            return Err(SafeWriteError::Bound(timed_out(program.as_str())));
        }
        if !is_validator && self.reload_times_out {
            return Err(SafeWriteError::Bound(timed_out(program.as_str())));
        }
        let status = if is_validator {
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
    (validator(), reload())
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
    assert_eq!(host.calls(), vec!["/usr/sbin/nginx", "/usr/bin/systemctl"]);
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
    assert_eq!(host.calls(), vec!["/usr/sbin/nginx"]);
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

#[test]
fn a_removal_whose_validator_times_out_puts_the_file_back() {
    // The file is gone when the validator runs, so a killed validator must restore it
    // exactly as a refusing one does, and report the bound, not a refusal.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, "server {}\n").unwrap();
    let host = FakeConfigHost::validator_timing_out();
    let (validator, reload) = commands();

    let outcome = remove_config(&host, &target, &validator, &reload);

    assert!(
        matches!(outcome, Err(SafeWriteError::Bound(_))),
        "a bound is not a failed validation: {outcome:?}"
    );
    assert_eq!(std::fs::read_to_string(&target).unwrap(), "server {}\n");
    assert_eq!(
        host.calls(),
        vec!["/usr/sbin/nginx"],
        "the reload is never reached"
    );
}

#[test]
fn a_removal_runs_its_validator_in_the_validators_class_and_its_reload_as_reload() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, "server {}\n").unwrap();
    let host = FakeConfigHost::passing();
    let (validator, reload) = commands();

    remove_config(&host, &target, &validator, &reload).unwrap();

    assert_eq!(
        *host.classes.lock().unwrap(),
        vec![CallClass::Validate, CallClass::Reload]
    );
}

#[test]
fn a_removal_whose_reload_times_out_puts_the_file_back_and_reports_the_bound() {
    // The validator accepted the tree without the file, but a reload that was killed
    // proves nothing about the server, so the removal must not stand.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, "server {}\n").unwrap();
    let host = FakeConfigHost::reload_timing_out();
    let (validator, reload) = commands();

    let outcome = remove_config(&host, &target, &validator, &reload);

    assert!(
        matches!(outcome, Err(SafeWriteError::Bound(_))),
        "a bound is not a failed reload: {outcome:?}"
    );
    assert_eq!(std::fs::read_to_string(&target).unwrap(), "server {}\n");
    assert_eq!(
        host.calls().len(),
        2,
        "the validator and the reload both ran"
    );
}
