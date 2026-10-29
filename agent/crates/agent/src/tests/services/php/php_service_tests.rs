//! The `InstallPhpVersion` rpc, driven the way the server drives it.
//!
//! These go through `PhpServiceImpl` itself, on a real multi-threaded runtime,
//! because the rpc wraps a blocking operation in `spawn_blocking` and turns its
//! progress callback into stream messages — the part nothing else exercises.
//! The operation underneath has its own suite in `maran_ops`; what is pinned
//! here is the stream's shape: the stages, exactly one terminal message, and
//! the wire code a failure travels as.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeSet;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{ConfigHost, SafeWriteError};
use tokio_stream::StreamExt;
use tonic::Request;

use crate::proto::php_service_server::PhpService;
use crate::proto::{ErrorCode, InstallPhpVersionRequest, install_php_version_response};
use crate::services::php::php_service::PhpServiceImpl;

/// The adapter every test here runs against.
fn adapter() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// A host with no PHP installed whose `start` either installs the version or
/// fails, and which records every argv it is handed.
///
/// Every method the install rpc never reaches says so rather than pretending to
/// work.
struct InstallHost {
    /// Whether `systemctl start` succeeds and leaves the pool directory behind.
    start_succeeds: bool,
    /// The pool directories that exist.
    directories: Mutex<BTreeSet<PathBuf>>,
    /// Every argv handed to `run`, shared with the test that owns the host's
    /// other half because the service takes the host by value.
    calls: Arc<Mutex<Vec<Vec<String>>>>,
}

impl InstallHost {
    /// A host on which starting the install unit works.
    fn installing(calls: &Arc<Mutex<Vec<Vec<String>>>>) -> Self {
        Self::with(true, calls)
    }

    /// A host on which starting the install unit fails.
    fn failing(calls: &Arc<Mutex<Vec<Vec<String>>>>) -> Self {
        Self::with(false, calls)
    }

    /// A host with nothing installed.
    fn with(start_succeeds: bool, calls: &Arc<Mutex<Vec<Vec<String>>>>) -> Self {
        Self {
            start_succeeds,
            directories: Mutex::new(BTreeSet::new()),
            calls: Arc::clone(calls),
        }
    }
}

impl ConfigHost for InstallHost {
    /// Records the argv, and answers as the service manager would.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        let mut command = vec![program.to_string()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls.lock().unwrap().push(command);

        if arguments.first() == Some(&"start") {
            if !self.start_succeeds {
                return Ok(CommandOutcome {
                    status: 1,
                    stdout: String::new(),
                    stderr: "Job for maran-php-install@php8.3-fpm.service failed".to_owned(),
                });
            }
            self.directories
                .lock()
                .unwrap()
                .insert(PathBuf::from(adapter().php_fpm_pool_directory("8.3")));
        }

        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }
}

impl PhpHost for InstallHost {
    /// Answers from the directories a successful start created.
    fn directory_exists(&self, path: &Path) -> bool {
        self.directories.lock().unwrap().contains(path)
    }

    /// Unreachable: installing creates no directory itself.
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        panic!("an install must not create a directory")
    }

    /// Unreachable: installing creates nothing as an account.
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        panic!("an install must not create a directory as an account")
    }

    /// Unreachable: installing writes no configuration.
    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an install must not write a file")
    }

    /// Unreachable: installing removes no configuration.
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an install must not remove a file")
    }

    /// Unreachable: installing reloads no pool.
    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an install must not reload a pool")
    }
}

/// Drives the rpc for `version` and collects every message of its stream.
async fn collect(host: InstallHost, version: &str) -> Vec<install_php_version_response::Result> {
    let service = PhpServiceImpl::new(host, adapter());
    let mut stream = service
        .install_php_version(Request::new(InstallPhpVersionRequest {
            version: version.to_owned(),
        }))
        .await
        .unwrap()
        .into_inner();

    let mut messages = Vec::new();
    while let Some(message) = stream.next().await {
        messages.push(message.unwrap().result.expect("a result is always set"));
    }

    messages
}

#[tokio::test(flavor = "multi_thread")]
async fn an_install_streams_four_stages_and_ends_with_exactly_one_ok() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let messages = collect(InstallHost::installing(&calls), "8.3").await;

    assert_eq!(messages.len(), 5, "four stages and one terminal message");
    let stages: Vec<(u32, String)> = messages[..4]
        .iter()
        .map(|message| match message {
            install_php_version_response::Result::Progress(progress) => {
                (progress.percent, progress.stage.clone())
            }
            other => panic!("expected a progress message, got {other:?}"),
        })
        .collect();
    assert_eq!(
        stages,
        vec![
            (10, "preparing".to_owned()),
            (30, "download".to_owned()),
            (80, "install".to_owned()),
            (100, "enable".to_owned()),
        ]
    );
    match &messages[4] {
        install_php_version_response::Result::Ok(ok) => assert_eq!(ok.version, "8.3"),
        other => panic!("expected the terminal Ok, got {other:?}"),
    }
}

#[tokio::test(flavor = "multi_thread")]
async fn a_failed_install_unit_ends_the_stream_with_one_system_failure_carrying_its_report() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let messages = collect(InstallHost::failing(&calls), "8.3").await;

    let errors: Vec<_> = messages
        .iter()
        .filter_map(|message| match message {
            install_php_version_response::Result::Error(error) => Some(error),
            _ => None,
        })
        .collect();
    assert_eq!(errors.len(), 1, "exactly one terminal message");
    assert!(matches!(
        messages.last().unwrap(),
        install_php_version_response::Result::Error(_)
    ));
    assert_eq!(errors[0].code, ErrorCode::SystemFailure as i32);
    assert!(
        errors[0]
            .tool_output
            .contains("maran-php-install@php8.3-fpm.service")
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_malformed_version_ends_the_stream_with_invalid_input_and_starts_nothing() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let messages = collect(InstallHost::installing(&calls), "8").await;

    assert_eq!(messages.len(), 1);
    match &messages[0] {
        install_php_version_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an Error, got {other:?}"),
    }
    assert!(calls.lock().unwrap().is_empty());
}
