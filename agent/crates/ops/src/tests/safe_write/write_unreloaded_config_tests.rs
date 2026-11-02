//! Tests for [`write_unreloaded_config`]: the protocol without a reload, for a
//! file its reader re-opens on every use.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;
use std::sync::Mutex;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::safe_write::adapter_programs::validator;
use crate::safe_write::model::ConfigFile;
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, write_unreloaded_config};

/// A [`ConfigHost`] that counts its calls and answers every one with `status`.
struct CountingHost {
    status: i32,
    calls: Mutex<usize>,
}

impl ConfigHost for CountingHost {
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        *self.calls.lock().unwrap() += 1;
        Ok(CommandOutcome {
            status: self.status,
            stdout: String::new(),
            stderr: String::new(),
        })
    }
}

#[test]
fn the_file_lands_at_its_mode_and_only_the_validator_runs() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("acme_deploy");
    let host = CountingHost {
        status: 0,
        calls: Mutex::new(0),
    };

    write_unreloaded_config(
        &host,
        &ConfigFile {
            target: &target,
            contents: "restrict ssh-ed25519 AAAA\n",
            mode: 0o644,
        },
        &validator(),
    )
    .unwrap();

    assert_eq!(
        std::fs::read_to_string(&target).unwrap(),
        "restrict ssh-ed25519 AAAA\n"
    );
    assert_eq!(
        std::fs::metadata(&target).unwrap().permissions().mode() & 0o777,
        0o644
    );
    assert_eq!(*host.calls.lock().unwrap(), 1, "no reload is run");
}

#[test]
fn a_refused_file_puts_the_previous_one_back() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("acme_deploy");
    std::fs::write(&target, "restrict ssh-ed25519 OLD\n").unwrap();
    let host = CountingHost {
        status: 1,
        calls: Mutex::new(0),
    };

    let result = write_unreloaded_config(
        &host,
        &ConfigFile {
            target: &target,
            contents: "restrict ssh-ed25519 NEW\n",
            mode: 0o644,
        },
        &validator(),
    );

    assert!(matches!(
        result,
        Err(SafeWriteError::ValidationFailed { .. })
    ));
    assert_eq!(
        std::fs::read_to_string(&target).unwrap(),
        "restrict ssh-ed25519 OLD\n"
    );
}
