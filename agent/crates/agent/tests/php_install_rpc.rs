//! `InstallPhpVersion` driven the way the server drives it: through
//! `PhpServiceImpl`, on a real multi-threaded runtime, collecting the stream a
//! client would read.
//!
//! The in-crate service tests pin the same stream shape from inside the crate; this
//! file is the out-of-crate view through the public surface and additionally covers
//! the family loop, the already-installed and succeeded-but-absent cases and the
//! run-to-completion drop, so the overlap is deliberate.
//!
//! What these pin is the one arrangement issue #93 changed. The agent's own
//! sandbox keeps `/usr` read-only, so it no longer runs the package manager: it
//! asks the service manager to start the installer's `maran-php-install@<package>`
//! unit and waits for that run to end. The tests observe the argv the host was
//! asked to spawn, the messages on the stream, and the run-to-completion class
//! of the rpc, with a host that records instead of installing. What no test here
//! can see is the unit itself running a package manager; that is measured on a
//! booted host (the install polygon), and the build-time polygon assertions in
//! `docker/polygon/asserts/parts/php-install-unit.sh` read the unit the
//! installer renders.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use maran_agent::proto::install_php_version_response::Result as Outcome;
use maran_agent::proto::php_service_server::PhpService;
use maran_agent::proto::{ErrorCode, InstallPhpVersionRequest, InstallPhpVersionResponse};
use maran_agent::services::php::php_service::PhpServiceImpl;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{ConfigHost, SafeWriteError};
use tokio_stream::StreamExt;
use tonic::Request;

/// How long a test waits for work that runs after its client has gone.
const COMPLETION_TIMEOUT: Duration = Duration::from_secs(10);

/// Gap between two looks at the host while waiting for that work.
const COMPLETION_POLL: Duration = Duration::from_millis(20);

/// What a fake host remembers and what it has been told to answer, shared
/// between the host the service owns and the test that inspects it afterwards.
struct State {
    /// The adapter whose pool directory "appears" when an install succeeds.
    distro: &'static dyn DistroAdapter,
    /// Every argv the host was asked to spawn, program first, in order.
    commands: Mutex<Vec<Vec<String>>>,
    /// The pool directories that exist.
    directories: Mutex<Vec<String>>,
    /// The exit status `start` answers with.
    start_status: Mutex<i32>,
    /// The stderr `start` answers with.
    start_stderr: Mutex<String>,
    /// Whether a successful `start` leaves the version's pool directory behind,
    /// as the install unit's package manager does.
    start_installs: Mutex<Option<String>>,
    /// Whether `start` outlives its deadline: the host then answers the bound
    /// the bounded spawn core produces, and no outcome at all.
    start_times_out: Mutex<bool>,
}

/// A [`PhpHost`] that spawns nothing: it records the argv and answers.
struct FakeHost {
    /// The state the test keeps the other half of.
    state: Arc<State>,
}

impl FakeHost {
    /// A host with nothing installed whose `start` succeeds and installs
    /// nothing, plus the handle the test reads it back through.
    fn new(family: DistroFamily) -> (Self, Arc<State>) {
        let state = Arc::new(State {
            distro: adapter_for(family),
            commands: Mutex::new(Vec::new()),
            directories: Mutex::new(Vec::new()),
            start_status: Mutex::new(0),
            start_stderr: Mutex::new(String::new()),
            start_installs: Mutex::new(None),
            start_times_out: Mutex::new(false),
        });
        (
            Self {
                state: Arc::clone(&state),
            },
            state,
        )
    }
}

impl State {
    /// Makes a successful `start` leave `version`'s pool directory behind.
    fn install_on_start(&self, version: &str) {
        *self.start_installs.lock().unwrap() = Some(version.to_owned());
    }

    /// Makes `start` fail with `status` and `stderr`.
    fn fail_start(&self, status: i32, stderr: &str) {
        *self.start_status.lock().unwrap() = status;
        *self.start_stderr.lock().unwrap() = stderr.to_owned();
    }

    /// Makes `start` outlive its deadline, as a blocked `systemctl` does.
    fn time_out_start(&self) {
        *self.start_times_out.lock().unwrap() = true;
    }

    /// Makes `version` already installed.
    fn preinstall(&self, version: &str) {
        self.directories
            .lock()
            .unwrap()
            .push(self.distro.php_fpm_pool_directory(version));
    }

    /// Every argv the host was asked to spawn, in order.
    fn commands(&self) -> Vec<Vec<String>> {
        self.commands.lock().unwrap().clone()
    }

    /// The argvs whose program is `program`.
    fn commands_to(&self, program: &str) -> Vec<Vec<String>> {
        self.commands()
            .into_iter()
            .filter(|command| command.first().is_some_and(|first| first == program))
            .collect()
    }
}

impl ConfigHost for FakeHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        let mut command = vec![program.to_string()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.state.commands.lock().unwrap().push(command);

        if arguments.first().copied() != Some("start") {
            return Ok(CommandOutcome {
                status: 0,
                stdout: String::new(),
                stderr: String::new(),
            });
        }
        if *self.state.start_times_out.lock().unwrap() {
            return Err(SafeWriteError::Bound(BoundExceeded::TimedOut {
                program: program.to_string(),
                deadline: CallClass::PackageInstall.deadline(),
            }));
        }
        let status = *self.state.start_status.lock().unwrap();
        let stderr = self.state.start_stderr.lock().unwrap().clone();
        let installs = self.state.start_installs.lock().unwrap().clone();
        if let Some(version) = installs.filter(|_| status == 0) {
            self.state.preinstall(&version);
        }
        Ok(CommandOutcome {
            status,
            stdout: String::new(),
            stderr,
        })
    }
}

impl PhpHost for FakeHost {
    fn directory_exists(&self, path: &Path) -> bool {
        self.state
            .directories
            .lock()
            .unwrap()
            .iter()
            .any(|directory| Path::new(directory) == path)
    }

    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        Err(PhpOpError::ConfigWrite {
            reason: "an install writes no directory".to_owned(),
        })
    }

    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        Err(PhpOpError::ConfigWrite {
            reason: "an install writes nothing in a home".to_owned(),
        })
    }

    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        Err(PhpOpError::ConfigWrite {
            reason: "an install writes no file".to_owned(),
        })
    }

    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        Err(PhpOpError::ConfigWrite {
            reason: "an install removes no file".to_owned(),
        })
    }

    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        Err(PhpOpError::ConfigWrite {
            reason: "an install reloads no pool".to_owned(),
        })
    }
}

/// The service around a fake host, and the handle to read the host back.
fn service(family: DistroFamily) -> (PhpServiceImpl<FakeHost>, Arc<State>) {
    let (host, state) = FakeHost::new(family);
    (PhpServiceImpl::new(host, adapter_for(family)), state)
}

/// Asks for `version` and reads the stream to its end, the way a client does.
async fn install(
    service: &PhpServiceImpl<FakeHost>,
    version: &str,
) -> Vec<InstallPhpVersionResponse> {
    let response = service
        .install_php_version(Request::new(InstallPhpVersionRequest {
            version: version.to_owned(),
        }))
        .await
        .unwrap();
    response
        .into_inner()
        .collect::<Vec<_>>()
        .await
        .into_iter()
        .map(|item| item.unwrap())
        .collect()
}

/// The `(percent, stage)` of every progress message, in order.
fn stages(messages: &[InstallPhpVersionResponse]) -> Vec<(u32, String)> {
    messages
        .iter()
        .filter_map(|message| match &message.result {
            Some(Outcome::Progress(progress)) => Some((progress.percent, progress.stage.clone())),
            _ => None,
        })
        .collect()
}

/// The terminal message, requiring it to be the last and the only one.
fn only_terminal(messages: &[InstallPhpVersionResponse]) -> &Outcome {
    let last = messages.last().expect("the stream carried no message");
    let terminals = messages
        .iter()
        .filter(|message| !matches!(message.result, Some(Outcome::Progress(_))))
        .count();
    assert_eq!(terminals, 1, "exactly one terminal message");
    last.result.as_ref().expect("the last message was empty")
}

#[tokio::test(flavor = "multi_thread")]
async fn an_install_starts_the_adapters_unit_and_never_runs_the_package_manager() {
    for (family, unit) in [
        (DistroFamily::Debian, "maran-php-install@php8.3-fpm.service"),
        (
            DistroFamily::Rhel,
            "maran-php-install@php83-php-fpm.service",
        ),
    ] {
        let (service, state) = service(family);
        state.install_on_start("8.3");
        let distro = adapter_for(family);

        let messages = install(&service, "8.3").await;

        assert!(
            matches!(only_terminal(&messages), Outcome::Ok(_)),
            "{family:?}: the install must succeed for its argv to be the one a real run has"
        );
        assert_eq!(
            state.commands()[0],
            vec![
                distro.service_manager().to_string(),
                "start".to_owned(),
                "--".to_owned(),
                unit.to_owned()
            ],
            "{family:?}: the first thing the agent spawns is the install unit's start, with the \
             unit after the end of options"
        );
        assert!(
            state.commands_to(distro.package_manager()).is_empty(),
            "{family:?}: the agent must never run the package manager itself, its sandbox cannot write /usr"
        );
    }
}

#[tokio::test(flavor = "multi_thread")]
async fn a_version_already_installed_completes_at_one_hundred_without_starting_anything() {
    let (service, state) = service(DistroFamily::Debian);
    state.preinstall("8.3");

    let messages = install(&service, "8.3").await;

    assert_eq!(stages(&messages), vec![(100, "install".to_owned())]);
    assert!(matches!(only_terminal(&messages), Outcome::Ok(_)));
    assert!(
        state.commands().is_empty(),
        "a retry of an installed version must cost nothing: no unit start, no enable"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn an_install_unit_stopped_at_its_deadline_is_one_system_failure_with_no_tool_output() {
    let (service, state) = service(DistroFamily::Debian);
    state.time_out_start();

    let messages = install(&service, "8.3").await;

    match only_terminal(&messages) {
        Outcome::Error(error) => {
            assert_eq!(
                error.code,
                ErrorCode::SystemFailure as i32,
                "a program stopped at its deadline is a fault of the host: {}",
                error.message
            );
            assert!(
                error.tool_output.is_empty(),
                "what a stopped program printed is neither complete nor safe to forward, got {:?}",
                error.tool_output
            );
            assert!(
                error
                    .message
                    .contains(adapter_for(DistroFamily::Debian).service_manager().as_str()),
                "the operator's message must name the program that was stopped, got {:?}",
                error.message
            );
        }
        other => panic!("expected the error terminal message, got {other:?}"),
    }
    assert!(
        !stages(&messages).iter().any(|(percent, _)| *percent >= 80),
        "no stage past the unit may be reported for a unit that was stopped"
    );
    assert!(
        state
            .commands()
            .iter()
            .all(|command| command.get(1).map(String::as_str) != Some("enable")),
        "a stopped install must not go on to enable a service"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_failed_install_unit_ends_the_stream_with_one_system_failure_naming_the_unit() {
    let (service, state) = service(DistroFamily::Debian);
    state.fail_start(
        1,
        "Job for maran-php-install@php8.3-fpm.service failed because the control process exited with error code.",
    );

    let messages = install(&service, "8.3").await;

    match only_terminal(&messages) {
        Outcome::Error(error) => {
            assert_eq!(error.code, ErrorCode::SystemFailure as i32);
            assert!(
                error
                    .tool_output
                    .contains("maran-php-install@php8.3-fpm.service"),
                "the operator's text must name the unit, got {:?}",
                error.tool_output
            );
        }
        other => panic!("expected the error terminal message, got {other:?}"),
    }
    assert!(
        !stages(&messages).iter().any(|(percent, _)| *percent >= 80),
        "no stage past the unit may be reported for a unit that failed"
    );
    assert!(
        state
            .commands_to(adapter_for(DistroFamily::Debian).package_manager())
            .is_empty(),
        "a failed unit must not fall back to the package manager in the agent"
    );
    assert!(
        state
            .commands()
            .iter()
            .all(|command| command.get(1).map(String::as_str) != Some("enable")),
        "a failed install must not go on to enable a service"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_unit_that_succeeds_without_installing_the_version_is_a_system_failure() {
    let (service, state) = service(DistroFamily::Debian);

    let messages = install(&service, "8.3").await;

    match only_terminal(&messages) {
        Outcome::Error(error) => assert_eq!(error.code, ErrorCode::SystemFailure as i32),
        other => panic!("expected the error terminal message, got {other:?}"),
    }
    assert!(
        state
            .commands()
            .iter()
            .all(|command| command.get(1).map(String::as_str) != Some("enable")),
        "a version that never appeared must not be enabled"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_refused_version_ends_the_stream_with_invalid_input_and_spawns_nothing() {
    // A malformed value, one carrying what a shell or a unit name would act
    // on, an empty one, and a well-formed version outside the supported set.
    for version in ["8", "8.3; reboot", "../../etc", "-8.3", "", "5.6"] {
        let (service, state) = service(DistroFamily::Debian);

        let messages = install(&service, version).await;

        assert_eq!(messages.len(), 1, "{version:?}: one terminal message only");
        match only_terminal(&messages) {
            Outcome::Error(error) => assert_eq!(
                error.code,
                ErrorCode::InvalidInput as i32,
                "{version:?}: refused as invalid input"
            ),
            other => panic!("{version:?}: expected the error terminal message, got {other:?}"),
        }
        assert!(
            state.commands().is_empty(),
            "{version:?}: a refused version must never become an argv"
        );
    }
}

/// A client that has gone away does not stop an install that has begun: the
/// rpc is in the run-to-completion class, because a package manager stopped
/// part-way is a broken host and the panel's remedy is to ask again.
///
/// The runtime is built with ONE blocking thread, held by a task the test owns,
/// so the install's blocking closure is still queued when the stream is
/// dropped. It therefore starts after its client has gone, deterministically,
/// which a runtime with spare blocking threads would leave to a race. What the
/// test then waits for, by polling with a timeout, is the `enable` that only a
/// run that reached its end spawns.
#[test]
fn a_client_that_drops_the_stream_does_not_stop_the_install() {
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .max_blocking_threads(1)
        .enable_all()
        .build()
        .unwrap();

    runtime.block_on(async {
        let (service, state) = service(DistroFamily::Debian);
        state.install_on_start("8.3");
        let distro = adapter_for(DistroFamily::Debian);

        let (release, held) = std::sync::mpsc::channel::<()>();
        let occupied = tokio::task::spawn_blocking(move || {
            let _ = held.recv();
        });

        let response = service
            .install_php_version(Request::new(InstallPhpVersionRequest {
                version: "8.3".to_owned(),
            }))
            .await
            .unwrap();
        drop(response);
        release.send(()).unwrap();
        occupied.await.unwrap();

        let deadline = Instant::now() + COMPLETION_TIMEOUT;
        loop {
            let enabled = state.commands().iter().any(|command| {
                command.get(1).map(String::as_str) == Some("enable")
                    && command.last().map(String::as_str)
                        == Some(distro.php_fpm_service("8.3").as_str())
            });
            if enabled {
                break;
            }
            assert!(
                Instant::now() < deadline,
                "the install did not run to its end after its client dropped the stream; commands: {:?}",
                state.commands()
            );
            tokio::time::sleep(COMPLETION_POLL).await;
        }
    });
}
