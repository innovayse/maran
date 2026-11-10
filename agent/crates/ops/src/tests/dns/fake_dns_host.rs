//! A host that answers the DNS operations without a daemon, a socket or a file.

// A fake's lock can only be poisoned by a failing test, and a failing assertion
// IS the reporting mechanism there.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::safe_write::model::{Reload, Validator};

/// One program the fake was asked to run: the program, then its arguments.
pub(crate) type Spawn = Vec<String>;

/// What the host was told to pretend about itself.
///
/// A per-argv fake: `is-active` answers differently from `restart`, and
/// differently again after a restart, which the record-and-replay core cannot
/// express (rules/rust.md "the rule of two").
pub(crate) struct FakeDnsHost {
    /// The bytes at the live configuration path, if any.
    live: Mutex<Option<String>>,
    /// Every configuration ever written, in order.
    written: Mutex<Vec<String>>,
    /// Every program the host was asked to run, in order.
    spawns: Mutex<Vec<Spawn>>,
    /// Whether the unit is active.
    running: Mutex<bool>,
    /// Whether the unit is in the boot sequence.
    enabled: Mutex<bool>,
    /// What the checker prints when it refuses; `None` accepts.
    checker_refusal: Option<String>,
    /// Whether a restart brings the unit up or exits non-zero.
    restart_succeeds: bool,
    /// Whether the daemon ever answers its control channel once running.
    control_answers: bool,
    /// Whether `systemctl enable` succeeds.
    enable_succeeds: bool,
    /// Whether `systemctl disable --now` succeeds.
    disable_succeeds: bool,
    /// Whether TCP port 53 accepts once the unit runs.
    port_53_accepts: bool,
    /// Whether the checker accepts a probe configuration stating a
    /// `dnssec-policy`.
    probe_accepted: bool,
    /// The text of every probe configuration the host was asked to check.
    probes: Mutex<Vec<String>>,
    /// A configuration a neighbour writes right after this one is swapped in.
    neighbour_writes_after_the_swap: Mutex<Option<String>>,
    /// The fake's virtual monotonic clock, advanced only by [`DnsHost::pause`].
    clock: Mutex<Duration>,
    /// Every pause the code asked for.
    pauses: Mutex<Vec<Duration>>,
}

impl FakeDnsHost {
    /// A host with no configuration, a stopped unit and a daemon that answers
    /// once it runs.
    pub(crate) fn new() -> Self {
        Self {
            live: Mutex::new(None),
            written: Mutex::new(Vec::new()),
            spawns: Mutex::new(Vec::new()),
            running: Mutex::new(false),
            enabled: Mutex::new(false),
            checker_refusal: None,
            restart_succeeds: true,
            control_answers: true,
            enable_succeeds: true,
            disable_succeeds: true,
            port_53_accepts: true,
            probe_accepted: true,
            probes: Mutex::new(Vec::new()),
            neighbour_writes_after_the_swap: Mutex::new(None),
            clock: Mutex::new(Duration::ZERO),
            pauses: Mutex::new(Vec::new()),
        }
    }

    /// A host whose unit is already running and enabled.
    pub(crate) fn with_running_daemon(self) -> Self {
        *self.running.lock().unwrap() = true;
        *self.enabled.lock().unwrap() = true;
        self
    }

    /// A host whose unit is running but was never put in the boot sequence.
    pub(crate) fn with_running_unit_not_enabled(self) -> Self {
        *self.running.lock().unwrap() = true;
        self
    }

    /// A host that already holds `config` at the live path.
    pub(crate) fn with_live_config(self, config: &str) -> Self {
        *self.live.lock().unwrap() = Some(config.to_owned());
        self
    }

    /// A host whose checker refuses every configuration with `output`.
    pub(crate) fn with_checker_refusing(mut self, output: &str) -> Self {
        self.checker_refusal = Some(output.to_owned());
        self
    }

    /// A host whose restart exits non-zero.
    pub(crate) fn with_failing_restart(mut self) -> Self {
        self.restart_succeeds = false;
        self
    }

    /// A host whose daemon never answers its control channel.
    pub(crate) fn with_silent_daemon(mut self) -> Self {
        self.control_answers = false;
        self
    }

    /// A host that refuses `systemctl enable`.
    pub(crate) fn with_failing_enable(mut self) -> Self {
        self.enable_succeeds = false;
        self
    }

    /// A host that refuses `systemctl disable --now`.
    pub(crate) fn with_failing_disable(mut self) -> Self {
        self.disable_succeeds = false;
        self
    }

    /// A host whose checker refuses a configuration stating a `dnssec-policy`, as a
    /// build without the statement does.
    pub(crate) fn with_dnssec_unsupported(mut self) -> Self {
        self.probe_accepted = false;
        self
    }

    /// A host on which another operation writes `config` right after this one's
    /// swap.
    pub(crate) fn with_neighbour_write_after_the_swap(self, config: &str) -> Self {
        *self.neighbour_writes_after_the_swap.lock().unwrap() = Some(config.to_owned());
        self
    }

    /// The bytes at the live configuration path.
    pub(crate) fn live_config(&self) -> Option<String> {
        self.live.lock().unwrap().clone()
    }

    /// Every configuration ever written through the protocol, in order.
    pub(crate) fn written_configs(&self) -> Vec<String> {
        self.written.lock().unwrap().clone()
    }

    /// The text of every probe configuration the host was asked to check.
    pub(crate) fn probes(&self) -> Vec<String> {
        self.probes.lock().unwrap().clone()
    }

    /// Every program run, in order.
    pub(crate) fn spawns(&self) -> Vec<Spawn> {
        self.spawns.lock().unwrap().clone()
    }

    /// Whether the unit is active now.
    pub(crate) fn is_running(&self) -> bool {
        *self.running.lock().unwrap()
    }

    /// Whether the unit is in the boot sequence now.
    pub(crate) fn is_enabled(&self) -> bool {
        *self.enabled.lock().unwrap()
    }

    /// How many `restart` commands were run.
    pub(crate) fn restart_count(&self) -> usize {
        self.spawns()
            .iter()
            .filter(|spawn| spawn.get(1).is_some_and(|verb| verb == "restart"))
            .count()
    }

    /// Every pause the code asked for.
    pub(crate) fn pauses(&self) -> Vec<Duration> {
        self.pauses.lock().unwrap().clone()
    }

    /// Answers one command the way the service manager, the checker or `rndc`
    /// would, dispatching on its first argument.
    fn answer(&self, arguments: &[&str]) -> CommandOutcome {
        let code = match arguments.first().copied() {
            Some("restart") => {
                if self.restart_succeeds {
                    *self.running.lock().unwrap() = true;
                    0
                } else {
                    1
                }
            }
            Some("is-active") => {
                if *self.running.lock().unwrap() {
                    0
                } else {
                    3
                }
            }
            Some("is-enabled") => {
                if *self.enabled.lock().unwrap() {
                    0
                } else {
                    1
                }
            }
            Some("enable") => {
                if self.enable_succeeds {
                    *self.enabled.lock().unwrap() = true;
                    0
                } else {
                    1
                }
            }
            Some("disable") => {
                if self.disable_succeeds {
                    *self.running.lock().unwrap() = false;
                    *self.enabled.lock().unwrap() = false;
                    0
                } else {
                    1
                }
            }
            Some("stop") => {
                *self.running.lock().unwrap() = false;
                0
            }
            Some("-k") => {
                // `rndc`: answers only while the unit runs and the daemon is
                // meant to answer.
                if *self.running.lock().unwrap() && self.control_answers {
                    0
                } else {
                    1
                }
            }
            _ => 0,
        };
        CommandOutcome {
            status: code,
            stdout: String::new(),
            stderr: String::new(),
        }
    }
}

impl DnsHost for FakeDnsHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, DnsError> {
        let mut argv = vec![program.as_str().to_owned()];
        argv.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.spawns.lock().unwrap().push(argv);
        Ok(self.answer(arguments))
    }

    fn read_config(&self, _target: &Path) -> Result<Option<String>, DnsError> {
        Ok(self.live.lock().unwrap().clone())
    }

    fn write_config(
        &self,
        _target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), DnsError> {
        let previous = self.live.lock().unwrap().clone();
        *self.live.lock().unwrap() = Some(contents.to_owned());
        self.written.lock().unwrap().push(contents.to_owned());
        if let Some(theirs) = self.neighbour_writes_after_the_swap.lock().unwrap().take() {
            *self.live.lock().unwrap() = Some(theirs);
        }

        let mut checked = vec![validator.program.as_str().to_owned()];
        checked.extend(validator.arguments.iter().map(|a| (*a).to_owned()));
        self.spawns.lock().unwrap().push(checked);
        if let Some(output) = &self.checker_refusal {
            *self.live.lock().unwrap() = previous;
            return Err(DnsError::ConfigRejected {
                output: output.clone(),
            });
        }

        let answer = self.run(reload.program, reload.arguments, CallClass::Reload)?;
        if answer.status != 0 {
            *self.live.lock().unwrap() = previous;
            return Err(DnsError::ServiceRefused {
                unit: AgentPaths::DNS_UNIT.to_owned(),
            });
        }
        Ok(())
    }

    fn probe_config(
        &self,
        _target: &Path,
        contents: &str,
        validator: &Validator<'_>,
    ) -> Result<(), DnsError> {
        self.probes.lock().unwrap().push(contents.to_owned());
        let mut checked = vec![validator.program.as_str().to_owned()];
        checked.extend(validator.arguments.iter().map(|a| (*a).to_owned()));
        self.spawns.lock().unwrap().push(checked);
        if self.probe_accepted {
            Ok(())
        } else {
            Err(DnsError::ConfigRejected {
                output: "unknown option 'dnssec-policy'".to_owned(),
            })
        }
    }

    fn remove_file(&self, _path: &Path) -> Result<(), DnsError> {
        *self.live.lock().unwrap() = None;
        Ok(())
    }

    fn tcp_port_accepts(&self, _port: u16) -> bool {
        self.port_53_accepts && *self.running.lock().unwrap()
    }

    fn pause(&self, duration: Duration) {
        self.pauses.lock().unwrap().push(duration);
        *self.clock.lock().unwrap() += duration;
    }

    fn monotonic_now(&self) -> Duration {
        *self.clock.lock().unwrap()
    }
}

/// The Debian family's adapter, the one every DNS test runs against.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}
