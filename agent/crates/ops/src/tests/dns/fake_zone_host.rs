//! A host that serves zones without a daemon, a checker or a disk.

// A fake's lock can only be poisoned by a failing test, and a failing assertion
// IS the reporting mechanism there.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeMap;
use std::io::Error;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::secret_string::SecretString;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::dnssec_key_material::DnssecKeyMaterial;
use maran_agent_core::validation::dns::record_name::RecordName;
use maran_agent_core::validation::dns::soa_interval::SoaInterval;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_agent_core::validation::dns::zone_serial::ZoneSerial;
use maran_distro::AdapterBinary;

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::dns::model::record_data::RecordData;
use crate::dns::model::zone_content::ZoneContent;
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::model::zone_record::ZoneRecord;
use crate::dns::model::zone_soa::ZoneSoa;
use crate::dns::zone_host::ZoneHost;
use crate::safe_write::model::{Reload, Validator};

/// Index of the `rndc` command in an argument vector built by `rndc_arguments`:
/// after `-k key -s address -p port`.
const RNDC_COMMAND: usize = 6;

/// One program the fake was asked to run: the program, then its arguments.
pub(crate) type Spawn = Vec<String>;

/// The mutable state of the fake, behind one lock.
#[derive(Default)]
struct State {
    /// Zone files by full path.
    files: BTreeMap<PathBuf, String>,
    /// Served zones and their serials.
    served: BTreeMap<String, u32>,
    /// Every program run, in order.
    spawns: Vec<Spawn>,
    /// The serial of the file most recently swapped in.
    last_written_serial: u32,
    /// The statement the daemon holds for each zone it serves, as `addzone`,
    /// `modzone` or [`FakeZoneHost::with_zone`] left it.
    statements: BTreeMap<String, String>,
    /// The group each key file was handed to, by path.
    key_groups: BTreeMap<PathBuf, String>,
}

/// What the host was told to pretend about itself.
///
/// A per-argv fake of the zone side, kept apart from the daemon one: its `rndc`
/// answers depend on which zones are served, which the daemon fake's fixed
/// answers cannot express.
pub(crate) struct FakeZoneHost {
    /// Files, served zones and the record of what ran.
    state: Mutex<State>,
    /// Whether the daemon answers its control channel.
    daemon_up: bool,
    /// What the zone checker prints when it refuses; `None` accepts.
    checker_refusal: Option<String>,
    /// Whether `addzone` and `reload` exit non-zero.
    load_refused: bool,
    /// Entries in the zone directory that are not zone files.
    strays: Vec<String>,
    /// What the filesystem reports as available; `None` is an unmeasurable one.
    available: Option<u64>,
    /// Whether `rndc showzone` exits non-zero.
    showzone_refused: bool,
    /// What `rndc signing -list` prints, by zone; a zone not here prints the
    /// daemon's `No signing records found`.
    signing_lists: BTreeMap<String, String>,
    /// Whether writing a key file fails.
    key_write_refused: bool,
    /// Whether the checker accepts a probe configuration.
    probe_accepted: bool,
}

impl FakeZoneHost {
    /// A host with a running daemon, an empty zone directory and plenty of room.
    pub(crate) fn new() -> Self {
        Self {
            state: Mutex::new(State::default()),
            daemon_up: true,
            checker_refusal: None,
            load_refused: false,
            strays: Vec::new(),
            available: Some(10 * 1024 * 1024 * 1024),
            showzone_refused: false,
            signing_lists: BTreeMap::new(),
            key_write_refused: false,
            probe_accepted: true,
        }
    }

    /// A host whose `rndc showzone` exits non-zero.
    pub(crate) fn with_showzone_refused(mut self) -> Self {
        self.showzone_refused = true;
        self
    }

    /// A host whose `rndc signing -list zone` prints `output`.
    pub(crate) fn with_signing_list(mut self, zone: &str, output: &str) -> Self {
        self.signing_lists
            .insert(zone.to_owned(), output.to_owned());
        self
    }

    /// A host that cannot write a key file.
    pub(crate) fn with_key_write_refused(mut self) -> Self {
        self.key_write_refused = true;
        self
    }

    /// Plants a file at `path`, as an earlier operation or the daemon would have
    /// left it.
    pub(crate) fn with_file(self, path: &Path, contents: &str) -> Self {
        self.state
            .lock()
            .unwrap()
            .files
            .insert(path.to_path_buf(), contents.to_owned());
        self
    }

    /// Makes the daemon serve `zone` at `serial` under `statement`, with a plain
    /// file for it in `directory`.
    pub(crate) fn with_zone_statement(
        self,
        directory: &Path,
        zone: &str,
        serial: u32,
        statement: &str,
    ) -> Self {
        {
            let mut state = self.state.lock().unwrap();
            state.served.insert(zone.to_owned(), serial);
            state
                .statements
                .insert(zone.to_owned(), statement.to_owned());
            state
                .files
                .insert(directory.join(format!("{zone}.db")), "; old\n".to_owned());
        }
        self
    }

    /// The statement the daemon holds for `zone`, if it serves it.
    pub(crate) fn statement(&self, zone: &str) -> Option<String> {
        self.state.lock().unwrap().statements.get(zone).cloned()
    }

    /// The group the key file at `path` was handed to.
    pub(crate) fn key_group(&self, path: &Path) -> Option<String> {
        self.state.lock().unwrap().key_groups.get(path).cloned()
    }

    /// The paths of every file the fake holds, sorted.
    pub(crate) fn file_paths(&self) -> Vec<PathBuf> {
        self.state.lock().unwrap().files.keys().cloned().collect()
    }

    /// A host whose daemon answers nothing.
    pub(crate) fn with_daemon_down(mut self) -> Self {
        self.daemon_up = false;
        self
    }

    /// A host whose zone checker refuses everything with `output`.
    pub(crate) fn with_checker_refusing(mut self, output: &str) -> Self {
        self.checker_refusal = Some(output.to_owned());
        self
    }

    /// A host whose daemon refuses to add or reload a zone.
    pub(crate) fn with_load_refused(mut self) -> Self {
        self.load_refused = true;
        self
    }

    /// A host whose zone directory also holds an entry called `name`.
    pub(crate) fn with_stray_entry(mut self, name: &str) -> Self {
        self.strays.push(name.to_owned());
        self
    }

    /// A host whose filesystem has `bytes` available.
    pub(crate) fn with_available_bytes(mut self, bytes: u64) -> Self {
        self.available = Some(bytes);
        self
    }

    /// A host whose filesystem cannot be measured.
    pub(crate) fn with_unmeasurable_filesystem(mut self) -> Self {
        self.available = None;
        self
    }

    /// Makes the daemon serve `zone` at `serial` and leaves `contents` as its
    /// file in `directory`, as an earlier `put_zone` would have.
    pub(crate) fn with_zone(
        self,
        directory: &Path,
        zone: &str,
        serial: u32,
        contents: &str,
    ) -> Self {
        {
            let mut state = self.state.lock().unwrap();
            state.served.insert(zone.to_owned(), serial);
            state.statements.insert(
                zone.to_owned(),
                format!(
                    "{{ type primary; file \"{}\"; }};",
                    directory.join(format!("{zone}.db")).display()
                ),
            );
            state
                .files
                .insert(directory.join(format!("{zone}.db")), contents.to_owned());
        }
        self
    }

    /// Leaves a zone file in `directory` that the daemon does not serve.
    pub(crate) fn with_unserved_file(self, directory: &Path, zone: &str) -> Self {
        self.state
            .lock()
            .unwrap()
            .files
            .insert(directory.join(format!("{zone}.db")), "; old\n".to_owned());
        self
    }

    /// The content of the file at `path`.
    pub(crate) fn file(&self, path: &Path) -> Option<String> {
        self.state.lock().unwrap().files.get(path).cloned()
    }

    /// The serial the daemon serves `zone` at, if it serves it.
    pub(crate) fn served_serial(&self, zone: &str) -> Option<u32> {
        self.state.lock().unwrap().served.get(zone).copied()
    }

    /// Every program run, in order.
    pub(crate) fn spawns(&self) -> Vec<Spawn> {
        self.state.lock().unwrap().spawns.clone()
    }

    /// The `rndc` commands run, each from its verb on.
    pub(crate) fn rndc_commands(&self) -> Vec<Vec<String>> {
        self.spawns()
            .into_iter()
            .filter(|spawn| spawn.get(1).is_some_and(|first| first == "-k"))
            .map(|spawn| spawn[1 + RNDC_COMMAND..].to_vec())
            .collect()
    }

    /// Answers one `rndc` command the way the daemon would.
    fn rndc(&self, command: &[&str]) -> CommandOutcome {
        let mut state = self.state.lock().unwrap();
        let outcome = |status: i32, stdout: &str, stderr: &str| CommandOutcome {
            status,
            stdout: stdout.to_owned(),
            stderr: stderr.to_owned(),
        };

        if !self.daemon_up {
            return outcome(
                1,
                "",
                "rndc: connect failed: 127.0.0.1#953: connection refused",
            );
        }

        if command.first() == Some(&"signing") {
            let zone = command.last().copied().unwrap_or_default();
            return match state.served.get(zone) {
                None => outcome(1, "", "rndc: 'signing' failed: not found"),
                Some(_) => outcome(
                    0,
                    self.signing_lists
                        .get(zone)
                        .map_or("No signing records found\n", String::as_str),
                    "",
                ),
            };
        }

        let zone = command.get(1).copied().unwrap_or_default().to_owned();
        match command.first().copied() {
            Some("zonestatus") => match state.served.get(&zone) {
                Some(serial) => outcome(
                    0,
                    &format!("name: {zone}\ntype: primary\nserial: {serial}\nnodes: 3\n"),
                    "",
                ),
                None => outcome(1, "", "rndc: 'zonestatus' failed: not found"),
            },
            Some("showzone") => match state.statements.get(&zone) {
                Some(statement) if !self.showzone_refused => {
                    outcome(0, &format!("zone \"{zone}\" {statement}\n"), "")
                }
                _ => outcome(1, "", "rndc: 'showzone' failed: not found"),
            },
            Some("addzone") => {
                if self.load_refused || state.served.contains_key(&zone) {
                    return outcome(1, "", "rndc: 'addzone' failed: already exists");
                }
                let serial = state.last_written_serial;
                state.served.insert(zone.clone(), serial);
                state
                    .statements
                    .insert(zone, command.get(2).copied().unwrap_or_default().to_owned());
                outcome(0, "zone added\n", "")
            }
            Some("modzone") => {
                if self.load_refused || !state.served.contains_key(&zone) {
                    return outcome(1, "", "rndc: 'modzone' failed: not found");
                }
                let serial = state.last_written_serial;
                state.served.insert(zone.clone(), serial);
                state
                    .statements
                    .insert(zone, command.get(2).copied().unwrap_or_default().to_owned());
                outcome(0, "zone modified\n", "")
            }
            Some("reload") => {
                if self.load_refused {
                    return outcome(1, "", "rndc: 'reload' failed: failure");
                }
                let serial = state.last_written_serial;
                state.served.insert(zone, serial);
                outcome(0, "zone reload queued\n", "")
            }
            Some("delzone") => match state.served.remove(&zone) {
                Some(_) => {
                    state.statements.remove(&zone);
                    outcome(0, "zone deleted\n", "")
                }
                None => outcome(1, "", "rndc: 'delzone' failed: not found"),
            },
            _ => outcome(0, "", ""),
        }
    }
}

/// Reads the serial a rendered zone states, from its `; serial` line.
fn serial_of(contents: &str) -> u32 {
    contents
        .lines()
        .find(|line| line.trim_end().ends_with("; serial"))
        .and_then(|line| line.split_whitespace().next())
        .and_then(|number| number.parse().ok())
        .unwrap_or_default()
}

impl DnsHost for FakeZoneHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, DnsError> {
        let mut argv = vec![program.as_str().to_owned()];
        argv.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.state.lock().unwrap().spawns.push(argv);

        if arguments.first() == Some(&"-k") {
            return Ok(self.rndc(&arguments[RNDC_COMMAND..]));
        }
        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    fn read_config(&self, target: &Path) -> Result<Option<String>, DnsError> {
        Ok(self.file(target))
    }

    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), DnsError> {
        panic!("a zone is never written through the daemon configuration path");
    }

    fn probe_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
    ) -> Result<(), DnsError> {
        if self.probe_accepted {
            Ok(())
        } else {
            Err(DnsError::ConfigRejected {
                output: "unknown option 'dnssec-policy'".to_owned(),
            })
        }
    }

    fn remove_file(&self, path: &Path) -> Result<(), DnsError> {
        self.state.lock().unwrap().files.remove(path);
        Ok(())
    }

    fn tcp_port_accepts(&self, _port: u16) -> bool {
        false
    }

    fn pause(&self, _duration: Duration) {}

    fn monotonic_now(&self) -> Duration {
        Duration::ZERO
    }
}

impl ZoneHost for FakeZoneHost {
    fn write_zone_file(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), DnsError> {
        let previous = {
            let mut state = self.state.lock().unwrap();
            let previous = state
                .files
                .insert(target.to_path_buf(), contents.to_owned());
            state.last_written_serial = serial_of(contents);
            let mut checked = vec![validator.program.as_str().to_owned()];
            checked.extend(validator.arguments.iter().map(|a| (*a).to_owned()));
            state.spawns.push(checked);
            previous
        };

        let restore = |this: &Self, previous: Option<String>| {
            let mut state = this.state.lock().unwrap();
            match previous {
                Some(bytes) => state.files.insert(target.to_path_buf(), bytes),
                None => state.files.remove(target),
            };
        };

        if let Some(output) = &self.checker_refusal {
            restore(self, previous);
            return Err(DnsError::ZoneRejected {
                output: output.clone(),
            });
        }

        let answer = self.run(reload.program, reload.arguments, CallClass::Reload)?;
        if answer.status != 0 {
            restore(self, previous);
            return Err(DnsError::ControlRefused {
                output: answer.stderr,
            });
        }
        Ok(())
    }

    fn write_key_file(&self, target: &Path, contents: &str, group: &str) -> Result<(), DnsError> {
        if self.key_write_refused {
            return Err(DnsError::ConfigWrite {
                reason: "signing key file: refused".to_owned(),
            });
        }
        let mut state = self.state.lock().unwrap();
        state
            .files
            .insert(target.to_path_buf(), contents.to_owned());
        state
            .key_groups
            .insert(target.to_path_buf(), group.to_owned());
        Ok(())
    }

    fn zone_file_names(&self, directory: &Path) -> Result<Vec<String>, DnsError> {
        let mut names: Vec<String> = self
            .state
            .lock()
            .unwrap()
            .files
            .keys()
            .filter(|path| path.parent() == Some(directory))
            .filter_map(|path| path.file_name()?.to_str().map(str::to_owned))
            .collect();
        names.extend(self.strays.iter().cloned());
        Ok(names)
    }

    fn available_bytes(&self, _directory: &Path) -> std::io::Result<u64> {
        self.available
            .ok_or_else(|| Error::other("the filesystem could not be measured"))
    }
}

/// The directory every zone test writes into.
pub(crate) fn zone_directory() -> PathBuf {
    PathBuf::from("/zones")
}

/// The directory signed zones' files are written into in every zone test.
pub(crate) fn signed_directory() -> PathBuf {
    PathBuf::from("/signed-zones")
}

/// The directory signing keys are written into in every zone test.
pub(crate) fn key_directory() -> PathBuf {
    PathBuf::from("/keys")
}

/// The three directories every zone test works in.
pub(crate) fn directories() -> ZoneDirectories {
    ZoneDirectories::new(zone_directory(), signed_directory(), key_directory())
}

/// Base64 of the 64 bytes `0, 1, 2, ... 63`: a public key whose RFC 4034 tag with
/// flags 257 is [`SAMPLE_KEY_TAG`].
const SAMPLE_PUBLIC_KEY: &str =
    "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==";

/// The key tag of [`SAMPLE_PUBLIC_KEY`], derived independently in the key type's
/// own tests.
pub(crate) const SAMPLE_KEY_TAG: u16 = 59_409;

/// Base64 of the 32 recognisable bytes `MARAN-PLANTED-SCALAR-0123456789!`, the
/// planted private half the tests search for.
pub(crate) const PLANTED_PRIVATE_KEY: &str = "TUFSQU4tUExBTlRFRC1TQ0FMQVItMDEyMzQ1Njc4OSE=";

/// A valid signing key whose private half is [`PLANTED_PRIVATE_KEY`].
pub(crate) fn sample_key() -> DnssecKeyMaterial {
    DnssecKeyMaterial::parse(
        SAMPLE_PUBLIC_KEY,
        &SecretString::new(PLANTED_PRIVATE_KEY.to_owned()),
        257,
        u32::from(SAMPLE_KEY_TAG),
    )
    .unwrap()
}

/// A zone named `zone` with two name servers, one A record and one MX record,
/// at `serial`.
pub(crate) fn sample_content(zone: &str, serial: u32) -> ZoneContent {
    let zone_name = ZoneName::parse(zone).unwrap();
    let name = |text: &str| DnsName::parse(text).unwrap();
    let soa = ZoneSoa {
        primary_nameserver: name(&format!("ns1.{zone}")),
        hostmaster: name(&format!("hostmaster.{zone}")),
        serial: ZoneSerial::parse(serial).unwrap(),
        refresh: SoaInterval::parse(3600).unwrap(),
        retry: SoaInterval::parse(600).unwrap(),
        expire: SoaInterval::parse(1_209_600).unwrap(),
        negative_ttl: DnsTtl::parse(300).unwrap(),
    };
    let records = vec![
        ZoneRecord {
            name: RecordName::parse("www").unwrap(),
            ttl: DnsTtl::parse(300).unwrap(),
            data: RecordData::A("192.0.2.10".parse().unwrap()),
        },
        ZoneRecord {
            name: RecordName::Apex,
            ttl: DnsTtl::parse(3600).unwrap(),
            data: RecordData::Mx {
                priority:
                    maran_agent_core::validation::dns::record_priority::RecordPriority::parse(10)
                        .unwrap(),
                exchange: name(&format!("mail.{zone}")),
            },
        },
    ];
    ZoneContent::new(
        zone_name,
        soa,
        vec![name(&format!("ns1.{zone}")), name(&format!("ns2.{zone}"))],
        records,
    )
    .unwrap()
}
