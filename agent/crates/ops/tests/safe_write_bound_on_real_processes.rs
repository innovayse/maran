//! A program stopped by the bounded spawn core, under the real config-write
//! protocol, with a real child and a real grandchild.
//!
//! The unit tests of `safe_write` drive the protocol with a host that answers
//! `Bound` from a table. What they cannot see is the whole chain the issue is
//! about: a real process group is killed, the writer's target is put back, and
//! the host-wide config-tree lock is free again for the next tenant. This file
//! runs that chain with real processes, through the crate's public API, the way
//! a caller outside the crate sees it.
//!
//! The stand-in is `xargs -a <list> -P 2 -L 1 env`, whose list names two
//! programs. `xargs` is the group leader and starts both at once: a `sleep`
//! that would run for hours, and a `head` that reads a named pipe the test
//! controls. The test waits until the `sleep` is seen alive, and only then feeds
//! the pipe more bytes than the core's ceiling allows. So at the moment the core
//! stops the call, a grandchild is provably alive, holds the output pipes open,
//! and is not writing anything: it cannot die of a broken pipe, only of the
//! group being killed.
//!
//! The ceiling is small and chosen here because the core's deadlines are fixed
//! per call class (the shortest is a minute) and a test may not wait a minute.
//!
//! UNOBSERVED HERE: the deadline path itself. A child stopped by its deadline and
//! one stopped by its ceiling take the same kill-and-reap path in the core and
//! come back as the same error type, but only the ceiling is reachable from
//! outside the core without a one-minute wait; the deadline is covered by the
//! core's own unit tests with an injected span.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::io::Write as _;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::mpsc;
use std::thread;
use std::time::{Duration, Instant};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::child_output::ChildOutput;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_agent_core::utils::run_bounded::run_bounded;
use maran_agent_core::utils::spawn_request::SpawnRequest;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};
use maran_ops::safe_write::model::{ConfigFile, Reload, Validator};
use maran_ops::safe_write::{
    ConfigHost, SafeWriteError, remove_config, write_config, write_config_set,
};

/// The group leader of the stand-in.
const XARGS: &str = "/usr/bin/xargs";

/// What the leader runs each line of its list through, so a line can name any
/// program and its arguments.
const ENV: &str = "/usr/bin/env";

/// The grandchild that lingers: it prints nothing and runs for hours.
const SLEEP: &str = "/usr/bin/sleep";

/// The grandchild that, once the test feeds the named pipe, writes past the
/// ceiling.
const HEAD: &str = "/usr/bin/head";

/// Makes the named pipe the flooding grandchild reads.
const MKFIFO: &str = "/usr/bin/mkfifo";

/// The ceiling the stand-in is held to, in bytes, on each stream.
const CEILING: u64 = 4096;

/// How many bytes the test feeds the pipe: more than [`CEILING`].
const FLOOD_BYTES: usize = 8192;

/// How long the whole stopped call may take once the flood is released. The
/// class deadlines are a minute and longer, so a call that returns inside this
/// budget was stopped by the ceiling and not by the clock.
const CALL_BUDGET: Duration = Duration::from_secs(30);

/// How long the probe waits for a process to appear in, or disappear from,
/// `/proc`.
const PROBE_BUDGET: Duration = Duration::from_secs(10);

/// How long a second writer is given to get through the lock once the first
/// writer's call has returned. A free lock lets it through in milliseconds; a
/// held one never frees, so the budget only decides how long a failing run
/// takes to say so.
const SECOND_WRITER_BUDGET: Duration = Duration::from_secs(20);

/// What a target holds before the writer touches it.
const PREVIOUS: &str = "server { server_name previous.test; }\n";

/// What the writer tries to put in place.
const PROPOSED: &str = "server { server_name proposed.test; }\n";

/// What the other tenant's write puts in place.
const OTHER_TENANT: &str = "server { server_name other-tenant.test; }\n";

/// A host that answers every call with success, except the calls of one class,
/// which run the stand-in under the real bounded core.
struct StandInHost {
    /// The class whose calls run the stand-in; `None` means none do.
    flood_class: Option<CallClass>,
    /// The list file the stand-in's leader reads its two programs from.
    list: PathBuf,
}

impl StandInHost {
    /// A host whose calls of `class` run the stand-in described by `list`.
    fn flooding(class: CallClass, list: PathBuf) -> Self {
        Self {
            flood_class: Some(class),
            list,
        }
    }

    /// A host that never runs the stand-in: the other tenant's.
    fn quiet() -> Self {
        Self {
            flood_class: None,
            list: PathBuf::new(),
        }
    }
}

impl ConfigHost for StandInHost {
    /// Runs the stand-in for the chosen class and reports success otherwise.
    fn run(
        &self,
        program: &AdapterBinary,
        _arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        if self.flood_class != Some(class) {
            return Ok(CommandOutcome {
                status: 0,
                stdout: String::new(),
                stderr: String::new(),
            });
        }

        let list = self.list.display().to_string();
        let arguments = ["-a", list.as_str(), "-P", "2", "-L", "1", ENV];
        run_bounded(SpawnRequest {
            program: Path::new(XARGS),
            arguments: &arguments,
            class,
            input: ChildInput::Null,
            output: ChildOutput::Capture {
                stdout_cap: CEILING,
                stderr_cap: CEILING,
            },
        })
        .map_err(|error| SafeWriteError::from_spawn(program, error))
    }
}

/// Everything one scenario needs on disk: the directory it works in, the named
/// pipe, the list the leader reads, and the sleep length that makes the lingering
/// grandchild's argument vector unique to this scenario.
struct Scenario {
    /// Owns every file of the scenario; removed with it.
    directory: tempfile::TempDir,
    /// The named pipe the flooding grandchild reads.
    fifo: PathBuf,
    /// The list file the leader reads.
    list: PathBuf,
    /// The lingering grandchild's sleep length, unique to this scenario.
    length: String,
}

impl Scenario {
    /// Builds the scenario for `case`, a number unique within this file.
    fn new(case: u32) -> Self {
        let directory = tempfile::tempdir().unwrap();
        let fifo = directory.path().join("flood.fifo");
        let made = Command::new(MKFIFO).arg(&fifo).status().unwrap();
        assert!(made.success(), "mkfifo failed");

        let length = format!("{}.{}", 6000 + case, std::process::id());
        let list = directory.path().join("programs.list");
        fs::write(
            &list,
            format!(
                "{SLEEP} {length}\n{HEAD} -c {FLOOD_BYTES} {}\n",
                fifo.display()
            ),
        )
        .unwrap();

        Self {
            directory,
            fifo,
            list,
            length,
        }
    }

    /// A host that runs this scenario's stand-in for calls of `class`.
    fn host(&self, class: CallClass) -> StandInHost {
        StandInHost::flooding(class, self.list.clone())
    }

    /// The target every scenario writes, holding [`PREVIOUS`].
    fn target(&self) -> PathBuf {
        let target = self.directory.path().join("site.conf");
        fs::write(&target, PREVIOUS).unwrap();

        target
    }

    /// The pids of the lingering grandchild.
    fn sleeper_pids(&self) -> Vec<u32> {
        pids_with_argv(&[SLEEP, self.length.as_str()])
    }

    /// Runs `call` on a thread, waits until the lingering grandchild is seen
    /// alive, releases the flood, and returns what the call returned.
    ///
    /// The wait is the positive control of the whole file: it proves the
    /// grandchild existed before the core stopped anything, so "gone" afterwards
    /// is a fact about the kill and not about a process that never started.
    fn drive<F>(&self, call: F) -> Result<(), SafeWriteError>
    where
        F: FnOnce() -> Result<(), SafeWriteError> + Send + 'static,
    {
        let (sender, receiver) = mpsc::channel();
        thread::spawn(move || {
            let _ = sender.send(call());
        });

        let started = Instant::now();
        while self.sleeper_pids().is_empty() {
            assert!(
                started.elapsed() < PROBE_BUDGET,
                "the lingering grandchild never appeared, so nothing here was stopped"
            );
            thread::sleep(Duration::from_millis(20));
        }

        let fifo = self.fifo.clone();
        thread::spawn(move || {
            // The write end blocks until the flooding grandchild has opened the
            // read end; a failure here shows up as the call never returning.
            if let Ok(mut pipe) = fs::OpenOptions::new().write(true).open(fifo) {
                let _ = pipe.write_all(&[b'x'; FLOOD_BYTES]);
            }
        });

        let released = Instant::now();
        let result = receiver
            .recv_timeout(CALL_BUDGET)
            .expect("the call was not stopped by the ceiling");
        assert!(released.elapsed() < CALL_BUDGET);

        result
    }

    /// Waits for the lingering grandchild to be gone, polling and never
    /// sleeping for a fixed time.
    fn assert_grandchild_gone(&self) {
        let started = Instant::now();
        while !self.sleeper_pids().is_empty() {
            assert!(
                started.elapsed() < PROBE_BUDGET,
                "the grandchild of a stopped call is still alive after {PROBE_BUDGET:?}: {:?}",
                self.sleeper_pids()
            );
            thread::sleep(Duration::from_millis(20));
        }
    }
}

/// The pids of every process whose argument vector is exactly `argv`.
fn pids_with_argv(argv: &[&str]) -> Vec<u32> {
    let mut wanted: Vec<u8> = Vec::new();
    for part in argv {
        wanted.extend_from_slice(part.as_bytes());
        wanted.push(0);
    }

    let mut found = Vec::new();
    for entry in fs::read_dir("/proc").unwrap().flatten() {
        let name = entry.file_name();
        let Some(pid) = name.to_str().and_then(|text| text.parse::<u32>().ok()) else {
            continue;
        };
        // A process that exits between the listing and the read is simply
        // absent; a zombie has an empty command line and never matches.
        if let Ok(cmdline) = fs::read(format!("/proc/{pid}/cmdline"))
            && cmdline == wanted
        {
            found.push(pid);
        }
    }

    found
}

/// The nginx and service-manager binaries of the Debian-family adapter, the
/// values every protocol call below passes in.
fn binaries() -> (AdapterBinary, AdapterBinary) {
    let adapter = adapter_for(DistroFamily::Debian);

    (adapter.nginx_binary(), adapter.service_manager())
}

/// The pure validator every call passes in.
fn validator(program: &AdapterBinary) -> Validator<'_> {
    Validator {
        program,
        arguments: &["-t"],
        class: CallClass::Validate,
    }
}

/// The reload every call passes in.
fn reload(program: &AdapterBinary) -> Reload<'_> {
    Reload {
        program,
        arguments: &["reload", "nginx"],
    }
}

/// Asserts `error` is the core's output-ceiling bound for the stand-in's leader,
/// carried through the protocol as a bound and as nothing else.
fn assert_ceiling_bound(error: &SafeWriteError) {
    match error {
        SafeWriteError::Bound(BoundExceeded::OutputTooLarge {
            program,
            stream,
            cap,
        }) => {
            assert_eq!(
                program, XARGS,
                "the bound must name the program that was killed"
            );
            assert_eq!(*stream, OutputStream::Stdout);
            assert_eq!(*cap, CEILING);
        }
        other => panic!("a stopped program must surface as a bound, got {other:?}"),
    }
}

/// Another tenant writes its own target through the same protocol, on a thread,
/// and must get through the host-wide lock well inside [`SECOND_WRITER_BUDGET`].
fn assert_another_tenant_can_write(scenario: &Scenario) {
    let target = scenario.directory.path().join("other-tenant.conf");
    fs::write(&target, PREVIOUS).unwrap();

    let (sender, receiver) = mpsc::channel();
    let thread_target = target.clone();
    let worker = thread::spawn(move || {
        let (nginx, manager) = binaries();
        let result = write_config(
            &StandInHost::quiet(),
            &thread_target,
            OTHER_TENANT,
            &validator(&nginx),
            &reload(&manager),
        );
        let _ = sender.send(result);
    });

    let result = receiver
        .recv_timeout(SECOND_WRITER_BUDGET)
        .expect("another tenant's write never got past the config-tree lock");
    worker.join().unwrap();

    result.expect("another tenant's write must succeed once the lock is free");
    assert_eq!(fs::read_to_string(&target).unwrap(), OTHER_TENANT);
}

#[test]
fn the_probe_finds_a_live_process_so_a_gone_verdict_means_something() {
    let length = format!("5999.{}", std::process::id());
    let mut stand_in = Command::new(SLEEP)
        .arg(&length)
        .stdout(Stdio::null())
        .spawn()
        .unwrap();

    let started = Instant::now();
    while !pids_with_argv(&[SLEEP, length.as_str()]).contains(&stand_in.id()) {
        assert!(
            started.elapsed() < PROBE_BUDGET,
            "the probe never saw a process it started"
        );
        thread::sleep(Duration::from_millis(20));
    }

    stand_in.kill().unwrap();
    stand_in.wait().unwrap();
    assert!(
        !pids_with_argv(&[SLEEP, length.as_str()]).contains(&stand_in.id()),
        "a reaped process must not be found"
    );
}

#[test]
fn a_validator_stopped_by_the_core_restores_the_target_kills_its_group_and_frees_the_lock() {
    let scenario = Scenario::new(1);
    let target = scenario.target();
    let host = scenario.host(CallClass::Validate);
    let call_target = target.clone();

    let error = scenario
        .drive(move || {
            let (nginx, manager) = binaries();
            write_config(
                &host,
                &call_target,
                PROPOSED,
                &validator(&nginx),
                &reload(&manager),
            )
        })
        .expect_err("a validator that was stopped cannot have validated anything");

    assert_ceiling_bound(&error);
    assert_eq!(
        fs::read_to_string(&target).unwrap(),
        PREVIOUS,
        "the previous content must be back on disk"
    );
    scenario.assert_grandchild_gone();
    assert_another_tenant_can_write(&scenario);
}

#[test]
fn a_reload_stopped_by_the_core_restores_the_target_kills_its_group_and_frees_the_lock() {
    let scenario = Scenario::new(2);
    let target = scenario.target();
    let host = scenario.host(CallClass::Reload);
    let call_target = target.clone();

    let error = scenario
        .drive(move || {
            let (nginx, manager) = binaries();
            write_config(
                &host,
                &call_target,
                PROPOSED,
                &validator(&nginx),
                &reload(&manager),
            )
        })
        .expect_err("a reload that was stopped cannot have reloaded anything");

    assert_ceiling_bound(&error);
    assert_eq!(fs::read_to_string(&target).unwrap(), PREVIOUS);
    scenario.assert_grandchild_gone();
    assert_another_tenant_can_write(&scenario);
}

#[test]
fn a_stopped_validator_puts_back_every_file_of_a_set_and_frees_the_lock() {
    let scenario = Scenario::new(3);
    let certificate = scenario.directory.path().join("site.pem");
    let key = scenario.directory.path().join("site.key");
    fs::write(&certificate, "old certificate\n").unwrap();
    fs::write(&key, "old key\n").unwrap();
    let host = scenario.host(CallClass::Validate);
    let (call_certificate, call_key) = (certificate.clone(), key.clone());

    let error = scenario
        .drive(move || {
            let (nginx, manager) = binaries();
            write_config_set(
                &host,
                &[
                    ConfigFile {
                        target: &call_certificate,
                        contents: "new certificate\n",
                        mode: 0o644,
                    },
                    ConfigFile {
                        target: &call_key,
                        contents: "new key\n",
                        mode: 0o600,
                    },
                ],
                &validator(&nginx),
                &reload(&manager),
            )
        })
        .expect_err("a validator that was stopped cannot have validated anything");

    assert_ceiling_bound(&error);
    assert_eq!(
        fs::read_to_string(&certificate).unwrap(),
        "old certificate\n"
    );
    assert_eq!(fs::read_to_string(&key).unwrap(), "old key\n");
    scenario.assert_grandchild_gone();
    assert_another_tenant_can_write(&scenario);
}

#[test]
fn a_stopped_reload_brings_back_a_removed_file_and_frees_the_lock() {
    let scenario = Scenario::new(4);
    let target = scenario.target();
    let host = scenario.host(CallClass::Reload);
    let call_target = target.clone();

    let error = scenario
        .drive(move || {
            let (nginx, manager) = binaries();
            remove_config(&host, &call_target, &validator(&nginx), &reload(&manager))
        })
        .expect_err("a reload that was stopped cannot have reloaded anything");

    assert_ceiling_bound(&error);
    assert_eq!(
        fs::read_to_string(&target).unwrap(),
        PREVIOUS,
        "a removal that was not completed must leave the file where it was"
    );
    scenario.assert_grandchild_gone();
    assert_another_tenant_can_write(&scenario);
}

#[test]
fn a_stopped_validator_is_not_a_verdict_on_the_configuration() {
    let scenario = Scenario::new(5);
    let target = scenario.target();
    let host = scenario.host(CallClass::Validate);

    let error = scenario
        .drive(move || {
            let (nginx, manager) = binaries();
            write_config(
                &host,
                &target,
                PROPOSED,
                &validator(&nginx),
                &reload(&manager),
            )
        })
        .expect_err("stopped");

    assert!(
        !matches!(
            error,
            SafeWriteError::ValidationFailed { .. }
                | SafeWriteError::SpawnFailed { .. }
                | SafeWriteError::RollbackFailed { .. }
        ),
        "a bound must not be reported as a rejected configuration, a failed start or a failed \
         rollback: {error:?}"
    );
    scenario.assert_grandchild_gone();
}
