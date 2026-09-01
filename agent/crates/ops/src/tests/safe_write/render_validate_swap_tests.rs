//! Tests for [`write_config`].
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `render_validate_swap.rs` declares this
//! file with `#[path]`, which keeps it a child module and therefore able to reach
//! private items — a crate-level `tests/` directory sees only the public API.
//!
//! The happy path is the least interesting test here (rules/rust.md "Config
//! writes"): every other test defends one of the ways the protocol can be
//! interrupted without corrupting the file nginx (or php-fpm, or crontab)
//! reads next.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, write_config};

/// A [`ConfigHost`] whose validator and reload steps can each be told what to
/// report, without ever spawning a real process.
struct FakeConfigHost {
    validation_status: i32,
    validation_stderr: String,
    reload_status: i32,
    reload_stderr: String,
    /// Every `(program, arguments)` pair the host was asked to run, in order —
    /// the thing the "validates and reloads once" test pins.
    calls: Mutex<Vec<(String, Vec<String>)>>,
}

impl FakeConfigHost {
    /// A host whose validator and reload both report success.
    fn passing() -> Self {
        Self {
            validation_status: 0,
            validation_stderr: String::new(),
            reload_status: 0,
            reload_stderr: String::new(),
            calls: Mutex::new(Vec::new()),
        }
    }

    /// A host whose validator rejects everything it is given, with `stderr`
    /// as the reason an operator would read in the log.
    fn failing_validation(stderr: &str) -> Self {
        Self {
            validation_status: 1,
            validation_stderr: stderr.to_owned(),
            ..Self::passing()
        }
    }

    /// A host whose validator passes but whose reload command fails.
    fn failing_reload(stderr: &str) -> Self {
        Self {
            reload_status: 1,
            reload_stderr: stderr.to_owned(),
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
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        self.calls
            .lock()
            .expect("the fixture lock is never poisoned")
            .push((
                program.to_owned(),
                arguments
                    .iter()
                    .map(|argument| (*argument).to_owned())
                    .collect(),
            ));

        // The validator is always asked first, so the second call this fixture
        // ever sees is the reload.
        let already_validated = self.calls().len() > 1;
        if already_validated {
            Ok(CommandOutcome {
                status: self.reload_status,
                stdout: String::new(),
                stderr: self.reload_stderr.clone(),
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

/// A validator argv stable across the tests below — its content does not
/// matter to `FakeConfigHost`, only that it is passed through unchanged.
fn validator() -> Validator<'static> {
    Validator {
        program: "/usr/sbin/nginx",
        arguments: &["-t"],
    }
}

/// A reload argv stable across the tests below.
fn reload() -> Reload<'static> {
    Reload {
        program: "/usr/bin/systemctl",
        arguments: &["reload", "nginx"],
    }
}

#[test]
fn a_config_that_fails_validation_leaves_the_previous_one_in_place() {
    // The whole reason this module exists: nginx must never be left holding a
    // file it cannot parse, because the next reload — by us or by logrotate —
    // takes the site down. Validation now runs AFTER the rename, so this
    // exercises a real restore rather than a no-op: the bad content really
    // did land on `target` for a moment before the guard put the old bytes
    // back.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, b"server { listen 80; }\n").unwrap();
    let host = FakeConfigHost::failing_validation("nginx: [emerg] unknown directive");

    let refused = write_config(&host, &target, "not a config", &validator(), &reload());

    assert!(matches!(
        refused,
        Err(SafeWriteError::ValidationFailed { .. })
    ));
    assert_eq!(std::fs::read(&target).unwrap(), b"server { listen 80; }\n");
}

#[test]
fn a_validation_failure_does_not_reload_the_service() {
    // The point of validating at all: a bad config must never reach the
    // running server. Since the rename happens before validation now, this
    // is the assertion that actually protects that promise — if it silently
    // reloaded anyway, the rejected content would still take effect.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, b"server { listen 80; }\n").unwrap();
    let host = FakeConfigHost::failing_validation("nginx: [emerg] unknown directive");

    let refused = write_config(&host, &target, "not a config", &validator(), &reload());

    assert!(matches!(
        refused,
        Err(SafeWriteError::ValidationFailed { .. })
    ));
    let calls = host.calls();
    assert_eq!(calls.len(), 1, "the reload command must not run: {calls:?}");
    assert_eq!(
        calls[0],
        ("/usr/sbin/nginx".to_owned(), vec!["-t".to_owned()])
    );
}

#[test]
fn a_failed_reload_also_restores_the_previous_config() {
    // Validation passing and the reload failing is the harder case: the file
    // is syntactically fine and still wrong, so the guard must run on a path
    // that looks like success until the last step.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, b"server { listen 80; }\n").unwrap();
    let host = FakeConfigHost::failing_reload("Job for nginx.service failed");

    let refused = write_config(
        &host,
        &target,
        "server { listen 80; index x; }",
        &validator(),
        &reload(),
    );

    assert!(matches!(refused, Err(SafeWriteError::ReloadFailed { .. })));
    assert_eq!(std::fs::read(&target).unwrap(), b"server { listen 80; }\n");
}

#[test]
fn writing_a_config_where_none_existed_removes_the_file_when_validation_fails() {
    // There is nothing to restore, and leaving a rejected file behind would
    // break the NEXT unrelated reload.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("new-site.conf");
    let host = FakeConfigHost::failing_validation("nginx: [emerg] unexpected end of file");

    let refused = write_config(&host, &target, "server {", &validator(), &reload());

    assert!(matches!(
        refused,
        Err(SafeWriteError::ValidationFailed { .. })
    ));
    assert!(!target.exists());
}

#[test]
fn the_temporary_file_is_created_in_the_targets_own_directory() {
    // A temporary file in /tmp cannot be renamed atomically onto /etc: the
    // rename becomes a copy, and a copy can be read half-written.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    let host = FakeConfigHost::passing();

    let written = write_config(
        &host,
        &target,
        "server { listen 80; }\n",
        &validator(),
        &reload(),
    );

    assert!(written.is_ok());
    // The only entry in the directory once the write completes is the target
    // itself: a temp file left behind, or written to a different directory,
    // would show up as an extra entry or leave this one missing.
    let entries: Vec<_> = std::fs::read_dir(directory.path())
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .collect();
    assert_eq!(entries, vec![target]);
}

#[test]
fn a_config_that_validates_replaces_the_previous_one_and_reloads_once() {
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, b"server { listen 80; }\n").unwrap();
    let host = FakeConfigHost::passing();

    let written = write_config(
        &host,
        &target,
        "server { listen 80; index x; }\n",
        &validator(),
        &reload(),
    );

    assert!(written.is_ok());
    assert_eq!(
        std::fs::read(&target).unwrap(),
        b"server { listen 80; index x; }\n"
    );

    let calls = host.calls();
    assert_eq!(calls.len(), 2);
    assert_eq!(
        calls[0],
        ("/usr/sbin/nginx".to_owned(), vec!["-t".to_owned()])
    );
    assert_eq!(
        calls[1],
        (
            "/usr/bin/systemctl".to_owned(),
            vec!["reload".to_owned(), "nginx".to_owned()]
        )
    );
}
