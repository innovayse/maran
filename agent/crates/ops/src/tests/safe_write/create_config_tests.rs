//! Tests for [`create_config`].
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `create_config.rs` declares this file
//! with `#[path]`, which keeps it a child module.
//!
//! A creation differs from a replacement in exactly two refusals, and every
//! test here defends one of them or the part of the protocol they must not
//! disturb: the target must not exist (refused at the capture AND by the
//! kernel's no-replace rename), and a validator that exits 0 still refuses the
//! write when its standard error names a warning the caller listed. The inverse
//! controls matter as much as the refusals — a gate mutated to refuse
//! everything passes every test that only hands it something it must refuse.
//!
//! The private `FakeConfigHost` is a fourth copy of the one in
//! `render_validate_swap_tests.rs`, `remove_config_tests.rs` and
//! `write_config_set_tests.rs`; that duplication predates this file and folding
//! the four into one support type is a change of its own.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::Mutex;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::safe_write::adapter_programs::{reload, validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, create_config};

/// The text nginx 1.24.0 prints for two vhosts naming `alice.test`, as the
/// startup log writes it when `error_log` is a file (and `-t` echoes it).
const FILE_LOG_WARNING: &str =
    "nginx: [warn] conflicting server name \"alice.test\" on 0.0.0.0:80, ignored";

/// The same warning when the startup log IS standard error: a timestamp and a
/// process id precede the severity instead of `nginx:`.
const STDERR_LOG_WARNING: &str =
    "2026/10/26 12:00:00 [warn] 7#7: conflicting server name \"alice.test\" on [::]:80, ignored";

/// A [`ConfigHost`] whose validator and reload steps can each be told what to
/// report, without ever spawning a real process.
struct FakeConfigHost {
    validation_status: i32,
    validation_stderr: String,
    reload_status: i32,
    calls: Mutex<Vec<(String, Vec<String>)>>,
}

impl FakeConfigHost {
    /// A host whose validator prints `stderr` and exits with `status`, and
    /// whose reload succeeds.
    fn validating(status: i32, stderr: &str) -> Self {
        Self {
            validation_status: status,
            validation_stderr: stderr.to_owned(),
            reload_status: 0,
            calls: Mutex::new(Vec::new()),
        }
    }

    /// A host whose validator and reload both succeed silently.
    fn passing() -> Self {
        Self::validating(0, "")
    }

    /// A host whose validator passes and whose reload fails.
    fn failing_reload() -> Self {
        Self {
            reload_status: 1,
            ..Self::passing()
        }
    }

    /// The recorded calls, in order, as `(program, arguments)`.
    fn calls(&self) -> Vec<(String, Vec<String>)> {
        self.calls
            .lock()
            .expect("the fixture lock is never poisoned")
            .clone()
    }
}

impl ConfigHost for FakeConfigHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        let mut calls = self
            .calls
            .lock()
            .expect("the fixture lock is never poisoned");
        calls.push((
            program.to_string(),
            arguments
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
        ));

        // The validator is always asked first, so the second call is the reload.
        if calls.len() > 1 {
            Ok(CommandOutcome {
                status: self.reload_status,
                stdout: String::new(),
                stderr: "Job for nginx.service failed".to_owned(),
            })
        } else {
            Ok(CommandOutcome {
                status: self.validation_status,
                stdout: String::new(),
                stderr: self.validation_stderr.clone(),
            })
        }
    }
}

/// The one warning every test below lets the creation refuse.
fn refused() -> Vec<String> {
    vec!["conflicting server name \"alice.test\"".to_owned()]
}

/// Everything in `directory`, by path, so a test can say "nothing else is
/// there" — a leftover temporary file included.
fn entries(directory: &Path) -> Vec<std::path::PathBuf> {
    let mut found: Vec<_> = std::fs::read_dir(directory)
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .collect();
    found.sort();
    found
}

#[test]
fn creating_a_config_whose_target_already_exists_is_refused_and_leaves_the_file_untouched() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, b"server { listen 80; }\n").unwrap();
    let host = FakeConfigHost::passing();

    let refusal = create_config(
        &host,
        &target,
        "server { listen 80; index x; }\n",
        &validator(),
        &reload(),
        &refused(),
    );

    match refusal {
        Err(SafeWriteError::TargetExists { target: named }) => {
            assert_eq!(named, target.display().to_string());
        }
        other => panic!("expected TargetExists, got {other:?}"),
    }
    assert_eq!(std::fs::read(&target).unwrap(), b"server { listen 80; }\n");
    assert!(
        host.calls().is_empty(),
        "nothing may be validated or reloaded for a write that never happened: {:?}",
        host.calls()
    );
    assert_eq!(
        entries(directory.path()),
        vec![target],
        "the refusal must not leave a temporary file beside the target"
    );
}

#[test]
fn a_dangling_symlink_at_the_target_is_refused_by_the_no_replace_rename_and_stays_a_symlink() {
    // The capture reads THROUGH the link, finds nothing and calls the target
    // absent — so only the kernel's no-replace rename can refuse this. A plain
    // `persist` would replace the link, which is the overwrite a creation must
    // never perform.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::os::unix::fs::symlink(directory.path().join("nowhere"), &target).unwrap();
    let host = FakeConfigHost::passing();

    let refusal = create_config(
        &host,
        &target,
        "server { listen 80; }\n",
        &validator(),
        &reload(),
        &refused(),
    );

    assert!(
        matches!(refusal, Err(SafeWriteError::TargetExists { .. })),
        "expected TargetExists, got {refusal:?}"
    );
    assert!(
        std::fs::symlink_metadata(&target)
            .unwrap()
            .file_type()
            .is_symlink(),
        "the file at the target is somebody else's and must be left as it was"
    );
    assert!(host.calls().is_empty(), "{:?}", host.calls());
    assert_eq!(entries(directory.path()), vec![target]);
}

#[test]
fn a_validator_that_passes_but_warns_about_a_named_fragment_refuses_and_removes_the_new_file() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::validating(0, FILE_LOG_WARNING);

    let refusal = create_config(
        &host,
        &target,
        "server { listen 80; server_name alice.test; }\n",
        &validator(),
        &reload(),
        &refused(),
    );

    match refusal {
        Err(SafeWriteError::RefusedWarning { warning, stderr }) => {
            assert_eq!(warning, "conflicting server name \"alice.test\"");
            assert_eq!(stderr, FILE_LOG_WARNING);
        }
        other => panic!("expected RefusedWarning, got {other:?}"),
    }
    assert!(!target.exists(), "the refused file must be removed again");
    assert!(entries(directory.path()).is_empty());
}

#[test]
fn a_refused_warning_never_reaches_the_reload() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::validating(0, FILE_LOG_WARNING);

    let _ = create_config(&host, &target, "x\n", &validator(), &reload(), &refused());

    let calls = host.calls();
    assert_eq!(calls.len(), 1, "only the validator may run: {calls:?}");
    assert_eq!(
        calls[0],
        ("/usr/sbin/nginx".to_owned(), vec!["-t".to_owned()])
    );
}

#[test]
fn a_warning_the_caller_did_not_name_does_not_refuse_the_creation() {
    // The inverse control: a gate that refused on any stderr, or on any
    // `[warn]`, would pass every test above and break every unrelated site.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::validating(
        0,
        "nginx: [warn] conflicting server name \"bob.test\" on 0.0.0.0:80, ignored",
    );

    let created = create_config(
        &host,
        &target,
        "server { listen 80; }\n",
        &validator(),
        &reload(),
        &refused(),
    );

    assert!(created.is_ok(), "{created:?}");
    assert_eq!(std::fs::read(&target).unwrap(), b"server { listen 80; }\n");
    assert_eq!(host.calls().len(), 2, "validator then reload");
}

#[test]
fn a_fragment_is_found_whatever_prefix_the_startup_log_puts_before_it() {
    // Read from nginx's source, not yet measured on every family: where the
    // startup log points decides what precedes the fragment, and the fragment
    // is the only part that does not change.
    for stderr in [FILE_LOG_WARNING, STDERR_LOG_WARNING] {
        let directory = tempfile::tempdir().unwrap();
        let target = directory.path().join("site.conf");
        let host = FakeConfigHost::validating(0, stderr);

        let refusal = create_config(&host, &target, "x\n", &validator(), &reload(), &refused());

        assert!(
            matches!(refusal, Err(SafeWriteError::RefusedWarning { .. })),
            "{stderr:?} must refuse, got {refusal:?}"
        );
        assert!(!target.exists());
    }
}

#[test]
fn only_a_fragment_that_is_present_in_the_output_is_the_one_reported() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::validating(0, FILE_LOG_WARNING);
    let fragments = vec![
        "conflicting server name \"zzz.test\"".to_owned(),
        "conflicting server name \"alice.test\"".to_owned(),
    ];

    let refusal = create_config(&host, &target, "x\n", &validator(), &reload(), &fragments);

    match refusal {
        Err(SafeWriteError::RefusedWarning { warning, .. }) => {
            assert_eq!(warning, "conflicting server name \"alice.test\"");
        }
        other => panic!("expected RefusedWarning, got {other:?}"),
    }
}

#[test]
fn a_creation_into_an_empty_directory_validates_and_reloads_once() {
    // The accepting control for the target check: an absent target must go
    // through, or the first vhost on any host would be refused.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::passing();

    let created = create_config(
        &host,
        &target,
        "server { listen 80; }\n",
        &validator(),
        &reload(),
        &refused(),
    );

    assert!(created.is_ok(), "{created:?}");
    assert_eq!(std::fs::read(&target).unwrap(), b"server { listen 80; }\n");
    assert_eq!(
        host.calls(),
        vec![
            ("/usr/sbin/nginx".to_owned(), vec!["-t".to_owned()]),
            (
                "/usr/bin/systemctl".to_owned(),
                vec!["reload".to_owned(), "nginx".to_owned()]
            ),
        ]
    );
    assert_eq!(entries(directory.path()), vec![target]);
}

#[test]
fn a_creation_the_validator_rejects_is_a_validation_failure_even_when_it_also_warns() {
    // A non-zero exit is the validator's verdict and keeps its own variant: the
    // warning check runs only after a zero exit, so the operator reads the
    // real reason rather than a refusal about a name.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::validating(1, FILE_LOG_WARNING);

    let refusal = create_config(&host, &target, "x\n", &validator(), &reload(), &refused());

    assert!(
        matches!(refusal, Err(SafeWriteError::ValidationFailed { .. })),
        "expected ValidationFailed, got {refusal:?}"
    );
    assert!(!target.exists());
    assert_eq!(host.calls().len(), 1);
}

#[test]
fn a_creation_whose_reload_fails_removes_the_new_file() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::failing_reload();

    let refusal = create_config(&host, &target, "x\n", &validator(), &reload(), &refused());

    assert!(
        matches!(refusal, Err(SafeWriteError::ReloadFailed { .. })),
        "expected ReloadFailed, got {refusal:?}"
    );
    assert!(!target.exists());
}

#[test]
fn an_existing_target_is_refused_at_the_capture_before_any_temporary_file_is_attempted() {
    // The witness for the capture-time refusal. The kernel's no-replace rename
    // refuses the same input later, so a test that only looks at the error would
    // pass with the capture check deleted. A directory the process cannot write
    // into separates the two: with the check, the refusal comes before a
    // temporary file is attempted and is TargetExists; without it, creating the
    // temporary file fails first and the error is TemporaryWrite. (A process
    // that ignores directory permissions reaches the no-replace rename instead
    // and still reports TargetExists, so the assertion holds there too.)
    use std::os::unix::fs::PermissionsExt;

    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, b"server { listen 80; }\n").unwrap();
    std::fs::set_permissions(directory.path(), std::fs::Permissions::from_mode(0o555)).unwrap();
    let host = FakeConfigHost::passing();

    let refusal = create_config(
        &host,
        &target,
        "server { listen 80; index x; }\n",
        &validator(),
        &reload(),
        &refused(),
    );

    // Restored before any assertion can panic, so the temporary directory can
    // always be removed.
    std::fs::set_permissions(directory.path(), std::fs::Permissions::from_mode(0o755)).unwrap();

    assert!(
        matches!(refusal, Err(SafeWriteError::TargetExists { .. })),
        "expected TargetExists, got {refusal:?}"
    );
    assert_eq!(std::fs::read(&target).unwrap(), b"server { listen 80; }\n");
    assert!(host.calls().is_empty(), "{:?}", host.calls());
}
