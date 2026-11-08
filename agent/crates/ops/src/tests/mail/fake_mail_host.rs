//! A host that answers the mail operations without a daemon, a socket or a file.

// A fake's lock can only be poisoned by a failing test, and a failing assertion
// IS the reporting mechanism there.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::mail::mail_message_size_mib::MailMessageSizeMib;
use maran_agent_core::validation::mail::mail_server_hostname::MailServerHostname;
use maran_agent_core::validation::mail::spam_thresholds::SpamThresholds;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::listening_socket::ListeningSocket;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_ports::MailPorts;
use crate::mail::model::mail_settings::MailSettings;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};
use crate::sites::SiteCertificate;
use crate::ssl::{CertificateInstallation, CertificateState};

/// The hostname every test in this area enables mail for.
pub(crate) const HOSTNAME: &str = "mail.example.test";

/// The release every test runs as unless it says otherwise: Ubuntu 24.04, which
/// is Dovecot 2.3 and Redis.
pub(crate) const VERSION_ID: &str = "24.04";

/// The adapter every test runs against.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// One thing the fake was asked to run.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct Spawn {
    /// The program, followed by its arguments.
    pub(crate) argv: Vec<String>,
}

/// One configuration change the fake was asked to make.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct Write {
    /// Whose files these were.
    pub(crate) daemon: MailDaemon,
    /// Every file's target.
    pub(crate) targets: Vec<PathBuf>,
}

/// What the fake service manager knows about one unit.
#[derive(Debug, Clone, Copy, Default)]
struct UnitState {
    /// Whether the unit is masked.
    masked: bool,
    /// Whether the unit is in the boot sequence.
    enabled: bool,
    /// Whether the unit is running.
    active: bool,
}

/// The state behind the fake's lock.
#[derive(Default)]
struct World {
    units: BTreeMap<String, UnitState>,
    files: BTreeMap<PathBuf, String>,
    spawns: Vec<Spawn>,
    writes: Vec<Write>,
    now: Duration,
}

/// What the host was told to pretend about itself.
///
/// A per-argv fake: `is-active` has to answer differently from
/// `reload-or-restart`, and differently again after one has happened, which the
/// record-and-replay core cannot express (rules/rust.md "the rule of two").
pub(crate) struct FakeMailHost {
    world: Mutex<World>,
    certificate_present: bool,
    missing_programs: BTreeSet<PathBuf>,
    /// The daemon whose own check refuses its files, and what it prints.
    refusing_check: Option<(MailDaemon, String)>,
    /// A unit the service manager refuses to start.
    refusing_unit: Option<String>,
    /// A unit that reports active and binds nothing.
    silent_unit: Option<String>,
    /// A unit the service manager refuses to disable.
    refusing_disable: Option<String>,
    /// Whether POP3 ports are bound when Dovecot starts.
    pop3_bound: bool,
    /// Whether the kernel's table cannot be read.
    sockets_unreadable: bool,
    /// What a tool prints when it is run with exactly this argv (program first).
    tool_outcomes: Vec<(Vec<String>, CommandOutcome)>,
}

impl FakeMailHost {
    /// A host with certificate material, every program installed, every unit
    /// masked and stopped, and no table on disk.
    pub(crate) fn new() -> Self {
        let host = Self {
            world: Mutex::new(World::default()),
            certificate_present: true,
            missing_programs: BTreeSet::new(),
            refusing_check: None,
            refusing_unit: None,
            silent_unit: None,
            refusing_disable: None,
            pop3_bound: false,
            sockets_unreadable: false,
            tool_outcomes: Vec::new(),
        };
        for unit in host.all_units() {
            host.world.lock().unwrap().units.insert(
                unit,
                UnitState {
                    masked: true,
                    ..UnitState::default()
                },
            );
        }
        host
    }

    /// The four unit names this release uses.
    pub(crate) fn all_units(&self) -> [String; 4] {
        [
            "redis-server".to_owned(),
            distro().rspamd_service().to_owned(),
            distro().dovecot_service().to_owned(),
            distro().postfix_service().to_owned(),
        ]
    }

    /// No certificate material for the hostname.
    pub(crate) fn without_certificate(mut self) -> Self {
        self.certificate_present = false;
        self
    }

    /// The spam filter's program is not on the host.
    pub(crate) fn without_rspamd(mut self) -> Self {
        self.missing_programs
            .insert(distro().rspamadm_binary().as_path().to_path_buf());
        self
    }

    /// `daemon`'s own check refuses what it is given and prints `output`.
    pub(crate) fn with_refusing_check(mut self, daemon: MailDaemon, output: &str) -> Self {
        self.refusing_check = Some((daemon, output.to_owned()));
        self
    }

    /// The service manager refuses to start `unit`.
    pub(crate) fn with_refusing_unit(mut self, unit: &str) -> Self {
        self.refusing_unit = Some(unit.to_owned());
        self
    }

    /// `unit` reports active and never binds a port.
    pub(crate) fn with_silent_unit(mut self, unit: &str) -> Self {
        self.silent_unit = Some(unit.to_owned());
        self
    }

    /// The service manager refuses to disable `unit`.
    pub(crate) fn with_refusing_disable(mut self, unit: &str) -> Self {
        self.refusing_disable = Some(unit.to_owned());
        self
    }

    /// Dovecot binds the POP3 ports as well.
    pub(crate) fn with_pop3_bound(mut self) -> Self {
        self.pop3_bound = true;
        self
    }

    /// The kernel's socket table cannot be read.
    pub(crate) fn with_unreadable_sockets(mut self) -> Self {
        self.sockets_unreadable = true;
        self
    }

    /// `program` run with exactly `arguments` exits `status` printing `stdout`.
    pub(crate) fn with_tool_outcome(
        mut self,
        program: &AdapterBinary,
        arguments: &[&str],
        status: i32,
        stdout: &str,
    ) -> Self {
        let mut argv = vec![program.as_str().to_owned()];
        argv.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.tool_outcomes.push((
            argv,
            CommandOutcome {
                status,
                stdout: stdout.to_owned(),
                stderr: String::new(),
            },
        ));
        self
    }

    /// Puts `contents` at `path` as if a previous run had written it.
    pub(crate) fn with_file(self, path: &str, contents: &str) -> Self {
        self.world
            .lock()
            .unwrap()
            .files
            .insert(PathBuf::from(path), contents.to_owned());
        self
    }

    /// Starts `unit` as if a previous enable had: unmasked, enabled and active.
    pub(crate) fn with_running_unit(self, unit: &str) -> Self {
        self.world.lock().unwrap().units.insert(
            unit.to_owned(),
            UnitState {
                masked: false,
                enabled: true,
                active: true,
            },
        );
        self
    }

    /// Whether `unit` is masked.
    pub(crate) fn is_masked(&self, unit: &str) -> bool {
        self.world.lock().unwrap().units[unit].masked
    }

    /// Whether `unit` is in the boot sequence.
    pub(crate) fn is_enabled(&self, unit: &str) -> bool {
        self.world.lock().unwrap().units[unit].enabled
    }

    /// Whether `unit` is running.
    pub(crate) fn is_active(&self, unit: &str) -> bool {
        self.world.lock().unwrap().units[unit].active
    }

    /// The live bytes at `path`.
    pub(crate) fn file(&self, path: &str) -> Option<String> {
        self.world
            .lock()
            .unwrap()
            .files
            .get(Path::new(path))
            .cloned()
    }

    /// Every program the host was asked to run, in order.
    pub(crate) fn spawns(&self) -> Vec<Spawn> {
        self.world.lock().unwrap().spawns.clone()
    }

    /// Every configuration change the host was asked to make, in order.
    pub(crate) fn writes(&self) -> Vec<Write> {
        self.world.lock().unwrap().writes.clone()
    }

    /// The service-manager verbs run against `unit`, in order.
    pub(crate) fn verbs_for(&self, unit: &str) -> Vec<String> {
        let manager = distro().service_manager().as_str().to_owned();
        self.spawns()
            .into_iter()
            .filter(|spawn| {
                spawn.argv.first() == Some(&manager)
                    && spawn.argv.last().map(String::as_str) == Some(unit)
            })
            .map(|spawn| spawn.argv[1].clone())
            .collect()
    }

    /// The ports a unit binds once it is running.
    fn ports_of(&self, unit: &str) -> Vec<u16> {
        if self.silent_unit.as_deref() == Some(unit) {
            return Vec::new();
        }
        if unit == distro().postfix_service() {
            return MailPorts::POSTFIX.to_vec();
        }
        if unit == distro().dovecot_service() {
            let mut ports = MailPorts::DOVECOT.to_vec();
            if self.pop3_bound {
                ports.extend(MailPorts::POP3);
            }
            return ports;
        }
        Vec::new()
    }

    /// Answers one service-manager command.
    fn service_manager(&self, argv: &[String]) -> CommandOutcome {
        let verb = argv[1].as_str();
        let unit = argv.last().cloned().unwrap_or_default();
        let mut world = self.world.lock().unwrap();
        let state = world.units.entry(unit.clone()).or_default();
        let status = match verb {
            "unmask" => {
                state.masked = false;
                0
            }
            "mask" => {
                state.masked = true;
                0
            }
            "enable" if state.masked => 1,
            "enable" => {
                state.enabled = true;
                0
            }
            "is-active" => i32::from(!state.active) * 3,
            "reload-or-restart"
                if state.masked || self.refusing_unit.as_deref() == Some(unit.as_str()) =>
            {
                1
            }
            "reload-or-restart" => {
                state.active = true;
                0
            }
            "disable" if self.refusing_disable.as_deref() == Some(unit.as_str()) => 1,
            "disable" => {
                state.enabled = false;
                state.active = false;
                0
            }
            other => panic!("the fake service manager was asked for {other}"),
        };
        CommandOutcome {
            status,
            stdout: String::new(),
            stderr: String::new(),
        }
    }
}

impl MailHost for FakeMailHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, MailError> {
        let mut argv = vec![program.as_str().to_owned()];
        argv.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.world
            .lock()
            .unwrap()
            .spawns
            .push(Spawn { argv: argv.clone() });

        if let Some((_, outcome)) = self.tool_outcomes.iter().find(|(known, _)| *known == argv) {
            return Ok(outcome.clone());
        }
        if program.as_str() == distro().service_manager().as_str() {
            return Ok(self.service_manager(&argv));
        }
        if program.as_str() == distro().postconf_binary().as_str() {
            let stdout = match arguments {
                ["-h", "myhostname"] => format!("{HOSTNAME}\n"),
                ["-h", "smtpd_tls_chain_files"] => "/k, /c\n".to_owned(),
                _ => String::new(),
            };
            return Ok(CommandOutcome {
                status: 0,
                stdout,
                stderr: String::new(),
            });
        }
        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    fn read_config(&self, target: &Path) -> Result<Option<String>, MailError> {
        Ok(self.world.lock().unwrap().files.get(target).cloned())
    }

    fn write_config_set(
        &self,
        daemon: MailDaemon,
        unit: &str,
        files: &[ConfigFile<'_>],
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), MailError> {
        let previous: Vec<(PathBuf, Option<String>)> = {
            let mut world = self.world.lock().unwrap();
            world.writes.push(Write {
                daemon,
                targets: files.iter().map(|file| file.target.to_path_buf()).collect(),
            });
            files
                .iter()
                .map(|file| {
                    let before = world
                        .files
                        .insert(file.target.to_path_buf(), file.contents.to_owned());
                    (file.target.to_path_buf(), before)
                })
                .collect()
        };
        let put_back = |host: &Self| {
            let mut world = host.world.lock().unwrap();
            for (path, before) in &previous {
                match before {
                    Some(bytes) => world.files.insert(path.clone(), bytes.clone()),
                    None => world.files.remove(path),
                };
            }
        };

        if let Some((refusing, output)) = &self.refusing_check
            && *refusing == daemon
        {
            put_back(self);
            return Err(MailError::ConfigRejected {
                daemon,
                output: output.clone(),
            });
        }
        let checked = self.run(validator.program, validator.arguments, validator.class)?;
        if checked.status != 0 {
            put_back(self);
            return Err(MailError::ConfigRejected {
                daemon,
                output: checked.stderr,
            });
        }
        let reloaded = self.run(reload.program, reload.arguments, CallClass::Reload)?;
        if reloaded.status != 0 {
            put_back(self);
            return Err(MailError::ServiceRefused {
                unit: unit.to_owned(),
            });
        }
        Ok(())
    }

    fn write_file_unreloaded(
        &self,
        daemon: MailDaemon,
        _unit: &str,
        file: &ConfigFile<'_>,
        validator: &Validator<'_>,
    ) -> Result<(), MailError> {
        {
            let mut world = self.world.lock().unwrap();
            world.writes.push(Write {
                daemon,
                targets: vec![file.target.to_path_buf()],
            });
            world
                .files
                .insert(file.target.to_path_buf(), file.contents.to_owned());
        }
        let checked = self.run(validator.program, validator.arguments, validator.class)?;
        if checked.status != 0 {
            self.world.lock().unwrap().files.remove(file.target);
            return Err(MailError::ConfigRejected {
                daemon,
                output: checked.stderr,
            });
        }
        Ok(())
    }

    fn path_exists(&self, path: &Path) -> bool {
        !self.missing_programs.contains(path)
    }

    fn listening_sockets(&self) -> Result<Vec<ListeningSocket>, MailError> {
        if self.sockets_unreadable {
            return Err(MailError::SocketsUnreadable);
        }
        let world = self.world.lock().unwrap();
        let mut sockets = Vec::new();
        for (unit, state) in &world.units {
            if state.active {
                sockets.extend(self.ports_of(unit).into_iter().map(|port| ListeningSocket {
                    port,
                    loopback_only: false,
                }));
            }
        }
        Ok(sockets)
    }

    fn certificate_state(&self, domain: &Domain) -> Result<CertificateState, MailError> {
        let paths = SiteCertificate::for_domain(domain);
        Ok(CertificateState {
            certificate_path: paths.certificate_path().to_string_lossy().into_owned(),
            private_key_path: paths.key_path().to_string_lossy().into_owned(),
            installation: if self.certificate_present {
                CertificateInstallation::Installed
            } else {
                CertificateInstallation::Absent
            },
        })
    }

    fn pause(&self, duration: Duration) {
        self.world.lock().unwrap().now += duration;
    }

    fn monotonic_now(&self) -> Duration {
        self.world.lock().unwrap().now
    }
}

/// The certificate name of the hostname every test enables mail for.
pub(crate) fn hostname() -> Domain {
    Domain::parse(HOSTNAME).unwrap()
}

/// The settings every enable test applies: POP3 off, 50 MiB, the product's
/// default spam scores.
pub(crate) fn settings() -> MailSettings {
    MailSettings {
        hostname: MailServerHostname::parse(HOSTNAME).unwrap(),
        pop3_enabled: false,
        message_size: MailMessageSizeMib::parse(50).unwrap(),
        spam: SpamThresholds::parse(4, 6, 15).unwrap(),
    }
}

/// The same settings with POP3 switched on.
pub(crate) fn settings_with_pop3() -> MailSettings {
    MailSettings {
        pop3_enabled: true,
        ..settings()
    }
}
