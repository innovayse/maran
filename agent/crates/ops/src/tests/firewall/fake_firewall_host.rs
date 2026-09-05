//! The in-memory [`FirewallHost`] the firewall tests decide against.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host loads rules into a running kernel and replaces the file those
//! rules are rebuilt from: a unit test cannot do either, and a suite that
//! tried would either need root or would firewall the machine running it.
//! What a unit test CAN pin is the decision — which `nft` subcommand, in
//! which argument order, in which SEQUENCE relative to the rename, and what
//! the operation makes of each refusal.
//!
//! The fake answers as the real tool does rather than as the operation hopes,
//! and it models the two behaviours the whole area is shaped around, both of
//! them measured on real nftables v1.0.9:
//!
//! - **Applying the bans file erases the elements in its sets.** The fake's
//!   load of that path clears its element list, exactly as the file's
//!   create-delete-redeclare idiom does on a live kernel. So a test that
//!   watches a ban survive is watching the real hazard, not a stub.
//! - **`nft list table` exits non-zero for a table that is not loaded.** The
//!   fake decides the same way, so "the table is absent" is discovered here
//!   the way it is discovered on a host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::{Condvar, Mutex};
use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};
use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::ruleset_ports::RulesetPorts;
use crate::firewall::model::ruleset_state::RulesetState;

/// How long the first caller of the arrival gate waits for a second one.
///
/// It is only ever paid when the module lock is doing its job: the partner
/// never arrives, because it is parked on the lock. When the lock is REMOVED
/// the partner arrives at once and nothing waits at all, so the mutant is
/// detected in microseconds and only the passing run pays. A quarter of a
/// second is orders of magnitude more than a spawned thread needs to reach
/// the gate, so the mutant cannot escape by being slow.
const ARRIVAL_BUDGET: Duration = Duration::from_millis(250);

/// A rendezvous the fake makes the first caller of a check wait at.
///
/// It exists to make a race REPRODUCIBLE. Two threads that merely start at
/// the same time may or may not interleave, and a concurrency test that
/// depends on which one wins reports "serialised" for a mutant that is not —
/// the direction that manufactures confidence.
struct ArrivalGate {
    /// How many callers have arrived so far.
    arrived: Mutex<usize>,
    /// Signalled by the second arrival.
    partner: Condvar,
}

impl ArrivalGate {
    /// A gate nobody has arrived at yet.
    fn new() -> Self {
        Self {
            arrived: Mutex::new(0),
            partner: Condvar::new(),
        }
    }

    /// Blocks the first caller until a second arrives, or until the budget
    /// runs out. Later callers pass straight through.
    fn arrive(&self) {
        let mut arrived = self.arrived.lock().unwrap();
        *arrived += 1;

        if *arrived >= 2 {
            self.partner.notify_all();

            return;
        }

        let _ = self.partner.wait_timeout(arrived, ARRIVAL_BUDGET);
    }
}

/// One member of a ban set, as the fake holds it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct FakeElement {
    /// Which family set it is in.
    pub(crate) set: String,
    /// The address, spelled as `nft` was asked to add it.
    pub(crate) address: String,
    /// The remaining lifetime in seconds, or `None` for a permanent ban.
    pub(crate) seconds: Option<u64>,
}

/// A [`FirewallHost`] that keeps a host's files, tables and ban elements in
/// memory, and records every step in order.
pub(crate) struct FakeFirewallHost {
    /// The "disk": absolute path to file contents.
    files: Mutex<HashMap<String, String>>,
    /// Every step the fake was asked to take, in order — which is what makes
    /// the apply's ORDER, the one thing this area must not get wrong,
    /// something a test can see.
    steps: Mutex<Vec<String>>,
    /// Every path that was really loaded with `nft -f`, in order.
    applies: Mutex<Vec<String>>,
    /// Whether `table inet maran_bans` is loaded.
    bans_table: Mutex<bool>,
    /// The members of the two ban sets.
    elements: Mutex<Vec<FakeElement>>,
    /// The status `nft --check` exits with.
    check_status: Mutex<i32>,
    /// What `nft --check` writes to standard error when it refuses.
    check_stderr: Mutex<String>,
    /// The status `nft -f` exits with.
    load_status: Mutex<i32>,
    /// Raw JSON to answer `nft -j list set` with, when a test installed some.
    bans_json: Mutex<Option<String>>,
    /// Whether `nft` cannot be started at all.
    nft_missing: Mutex<bool>,
    /// Whether reading a file refuses.
    read_refuses: Mutex<bool>,
    /// Whether staging, flushing or renaming refuses.
    write_refuses: Mutex<bool>,
    /// The rendezvous the bans-table check waits at, when a test installed
    /// one.
    gate: Mutex<Option<ArrivalGate>>,
}

impl FakeFirewallHost {
    /// A host with no ruleset file, no bans table, and nothing refusing.
    pub(crate) fn new() -> Self {
        Self {
            files: Mutex::new(HashMap::new()),
            steps: Mutex::new(Vec::new()),
            applies: Mutex::new(Vec::new()),
            bans_table: Mutex::new(false),
            elements: Mutex::new(Vec::new()),
            check_status: Mutex::new(0),
            check_stderr: Mutex::new(String::new()),
            load_status: Mutex::new(0),
            bans_json: Mutex::new(None),
            nft_missing: Mutex::new(false),
            read_refuses: Mutex::new(false),
            write_refuses: Mutex::new(false),
            gate: Mutex::new(None),
        }
    }

    /// A host whose ruleset file holds exactly `rules`, rendered as this
    /// agent renders them.
    pub(crate) fn with_rules(rules: &[FirewallRule]) -> Self {
        let host = Self::new();
        host.put_file(&ruleset_path(), &rendered(rules));

        host
    }

    /// Puts `contents` at `path` on the "disk".
    pub(crate) fn put_file(&self, path: &str, contents: &str) {
        self.files
            .lock()
            .unwrap()
            .insert(path.to_owned(), contents.to_owned());
    }

    /// What is at `path` now, if anything.
    pub(crate) fn file(&self, path: &str) -> Option<String> {
        self.files.lock().unwrap().get(path).cloned()
    }

    /// Marks `table inet maran_bans` as loaded.
    pub(crate) fn with_bans_table(self) -> Self {
        *self.bans_table.lock().unwrap() = true;

        self
    }

    /// Puts a member into a ban set without going through `nft add`.
    pub(crate) fn with_element(self, set: &str, address: &str, seconds: Option<u64>) -> Self {
        self.elements.lock().unwrap().push(FakeElement {
            set: set.to_owned(),
            address: address.to_owned(),
            seconds,
        });

        self
    }

    /// Makes the first caller of the bans-table check wait for a second one.
    pub(crate) fn with_arrival_gate(self) -> Self {
        *self.gate.lock().unwrap() = Some(ArrivalGate::new());

        self
    }

    /// Makes `nft --check` refuse, with `stderr` as its complaint.
    pub(crate) fn refuse_check_with(&self, stderr: &str) {
        *self.check_status.lock().unwrap() = 1;
        *self.check_stderr.lock().unwrap() = stderr.to_owned();
    }

    /// Makes `nft -f` refuse a file it has already checked.
    pub(crate) fn refuse_load(&self) {
        *self.load_status.lock().unwrap() = 1;
    }

    /// Makes `nft` impossible to start.
    pub(crate) fn lose_nft(&self) {
        *self.nft_missing.lock().unwrap() = true;
    }

    /// Makes reading a file refuse the way an unreadable file does.
    pub(crate) fn refuse_reads(&self) {
        *self.read_refuses.lock().unwrap() = true;
    }

    /// Makes staging, flushing and renaming refuse.
    pub(crate) fn refuse_writes(&self) {
        *self.write_refuses.lock().unwrap() = true;
    }

    /// Answers `nft -j list set` with `json` instead of building it.
    pub(crate) fn answer_bans_with(&self, json: &str) {
        *self.bans_json.lock().unwrap() = Some(json.to_owned());
    }

    /// Every step the fake was asked to take, in order.
    pub(crate) fn steps(&self) -> Vec<String> {
        self.steps.lock().unwrap().clone()
    }

    /// Every path that was loaded with `nft -f`, in order.
    pub(crate) fn applies(&self) -> Vec<String> {
        self.applies.lock().unwrap().clone()
    }

    /// The members of the ban sets now.
    pub(crate) fn elements(&self) -> Vec<FakeElement> {
        self.elements.lock().unwrap().clone()
    }

    /// The argument vector of the first `nft` call whose second argument is
    /// `verb`, the program first.
    pub(crate) fn nft_call_starting_with(&self, verb: &str) -> Option<Vec<String>> {
        self.steps()
            .into_iter()
            .filter_map(|step| step.strip_prefix(RUN_STEP).map(str::to_owned))
            .map(|argv| argv.split(' ').map(str::to_owned).collect::<Vec<_>>())
            .find(|argv| argv.get(1).is_some_and(|first| first == verb))
    }

    /// Records one step.
    fn record(&self, step: String) {
        self.steps.lock().unwrap().push(step);
    }

    /// Answers an `nft` invocation the way the real tool would.
    fn answer_nft(&self, arguments: &[&str]) -> CommandOutcome {
        match arguments {
            ["--check", "-f", _] => self.answer_check(),
            ["-f", path] => self.answer_load(path),
            ["list", "table", _, _] => self.answer_table_listing(),
            ["-j", "list", "set", _, _, set] => self.answer_set_listing(set),
            ["add", "element", ..] => self.answer_add(arguments),
            ["delete", "element", ..] => self.answer_delete(arguments),
            _ => panic!("the fake was asked to run nft with {arguments:?}"),
        }
    }

    /// `nft --check`: whatever a test installed.
    fn answer_check(&self) -> CommandOutcome {
        CommandOutcome {
            status: *self.check_status.lock().unwrap(),
            stdout: String::new(),
            stderr: self.check_stderr.lock().unwrap().clone(),
        }
    }

    /// `nft -f`: loads the file, and models what loading each of the two
    /// files really does.
    fn answer_load(&self, path: &str) -> CommandOutcome {
        let status = *self.load_status.lock().unwrap();
        if status == 0 {
            self.applies.lock().unwrap().push(path.to_owned());

            if path == bans_path() {
                // The measured behaviour, and the whole reason
                // `ensure_bans_table` refuses to run twice: the bans file
                // carries the create-delete-redeclare idiom, so loading it
                // over a table that is already there takes every ban with it.
                *self.bans_table.lock().unwrap() = true;
                self.elements.lock().unwrap().clear();
            }
        }

        CommandOutcome {
            status,
            stdout: String::new(),
            stderr: String::new(),
        }
    }

    /// `nft list table`: exit 0 when the table is loaded, 1 when it is not.
    fn answer_table_listing(&self) -> CommandOutcome {
        if let Some(gate) = self.gate.lock().unwrap().as_ref() {
            gate.arrive();
        }

        let present = *self.bans_table.lock().unwrap();

        CommandOutcome {
            status: i32::from(!present),
            stdout: String::new(),
            stderr: String::new(),
        }
    }

    /// `nft -j list set`: the JSON a test installed, or one built from the
    /// members the fake holds.
    fn answer_set_listing(&self, set: &str) -> CommandOutcome {
        if !*self.bans_table.lock().unwrap() {
            return CommandOutcome {
                status: 1,
                stdout: String::new(),
                stderr: String::from("Error: No such file or directory"),
            };
        }

        if let Some(json) = self.bans_json.lock().unwrap().clone() {
            return CommandOutcome {
                status: 0,
                stdout: json,
                stderr: String::new(),
            };
        }

        CommandOutcome {
            status: 0,
            stdout: set_listing_json(set, &self.elements()),
            stderr: String::new(),
        }
    }

    /// `nft add element`: refuses when the table is not loaded, otherwise
    /// records the member.
    fn answer_add(&self, arguments: &[&str]) -> CommandOutcome {
        if !*self.bans_table.lock().unwrap() {
            return CommandOutcome {
                status: 1,
                stdout: String::new(),
                stderr: String::from("Error: No such file or directory"),
            };
        }

        // Measured on real nftables v1.0.9: `add element` on an address the set
        // already holds REPLACES it, refreshing the timeout — it extends
        // (900s -> 2h), shortens (2h -> 1m) and converts in both directions
        // between timed and permanent, exiting 0 every time. The fake used to
        // push a duplicate, which taught the opposite and made a test pass for
        // a reason the kernel does not share.
        let element = element_of(arguments);
        let mut elements = self.elements.lock().unwrap();
        elements.retain(|held| held.set != element.set || held.address != element.address);
        elements.push(element);

        CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        }
    }

    /// `nft delete element`: exit 0 when the member was there, 1 when it was
    /// not — which is how the real tool reports both.
    fn answer_delete(&self, arguments: &[&str]) -> CommandOutcome {
        let wanted = element_of(arguments);
        let mut elements = self.elements.lock().unwrap();
        let held = elements
            .iter()
            .any(|element| element.set == wanted.set && element.address == wanted.address);
        elements.retain(|element| element.set != wanted.set || element.address != wanted.address);

        CommandOutcome {
            status: i32::from(!held),
            stdout: String::new(),
            stderr: String::new(),
        }
    }
}

impl FirewallHost for FakeFirewallHost {
    /// Records the call and answers as `nft` would.
    ///
    /// A program that is not the adapter's `nft` panics rather than answering
    /// blandly: a fake that shrugs at an unexpected tool is a fake that lets
    /// an operation run anything and still pass.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, FirewallError> {
        assert!(
            program.starts_with('/') && program.ends_with("nft"),
            "the firewall spawns nft by an absolute path from the adapter: {program}"
        );

        self.record(format!("{RUN_STEP}{program} {}", arguments.join(" ")));

        if *self.nft_missing.lock().unwrap() {
            return Err(FirewallError::NftFailed {
                stderr: String::from("could not run nft"),
            });
        }

        Ok(self.answer_nft(arguments))
    }

    /// Answers with what is on the "disk", or refuses when a test said reads
    /// must.
    fn read_file(&self, path: &Path) -> Result<Option<String>, FirewallError> {
        if *self.read_refuses.lock().unwrap() {
            return Err(FirewallError::RulesetUnreadable);
        }

        Ok(self.file(&path.display().to_string()))
    }

    /// Records the staging and writes the contents to a predictable
    /// neighbouring path, so a test can name it in an assertion.
    fn stage_file(&self, target: &Path, contents: &str) -> Result<PathBuf, FirewallError> {
        if *self.write_refuses.lock().unwrap() {
            return Err(FirewallError::StagingFailed);
        }

        self.record(String::from(STAGE_STEP));
        let staged = staged_path(target);
        self.put_file(&staged, contents);

        Ok(PathBuf::from(staged))
    }

    /// Records the file's flush.
    fn sync_file(&self, _staged: &Path) -> Result<(), FirewallError> {
        if *self.write_refuses.lock().unwrap() {
            return Err(FirewallError::StagingFailed);
        }

        self.record(String::from(SYNC_FILE_STEP));

        Ok(())
    }

    /// Records the rename and moves the contents across.
    fn commit_file(&self, staged: &Path, target: &Path) -> Result<(), FirewallError> {
        if *self.write_refuses.lock().unwrap() {
            return Err(FirewallError::StagingFailed);
        }

        self.record(String::from(COMMIT_STEP));
        let mut files = self.files.lock().unwrap();
        let Some(contents) = files.remove(&staged.display().to_string()) else {
            return Err(FirewallError::StagingFailed);
        };
        files.insert(target.display().to_string(), contents);

        Ok(())
    }

    /// Records the directory's flush — the step that makes the rename durable,
    /// and the reason it is recorded separately from the file's is that a test
    /// can then see which side of the rename it landed on.
    fn sync_directory(&self, _target: &Path) -> Result<(), FirewallError> {
        if *self.write_refuses.lock().unwrap() {
            return Err(FirewallError::StagingFailed);
        }

        self.record(String::from(SYNC_DIRECTORY_STEP));

        Ok(())
    }

    /// Records the discard and takes the staged file away.
    fn discard_file(&self, staged: &Path) {
        self.record(String::from(DISCARD_STEP));
        self.files
            .lock()
            .unwrap()
            .remove(&staged.display().to_string());
    }
}

/// The prefix a recorded spawn carries in the step log.
pub(crate) const RUN_STEP: &str = "run ";

/// The step log entry for writing the temporary file.
pub(crate) const STAGE_STEP: &str = "stage";

/// The step log entry for flushing the staged file.
pub(crate) const SYNC_FILE_STEP: &str = "sync-file";

/// The step log entry for flushing the directory the rename published the
/// file in. A separate entry from [`SYNC_FILE_STEP`] on purpose: the two
/// flushes do different jobs and belong on opposite sides of the rename, and a
/// single "sync" entry would make the difference invisible to a test.
pub(crate) const SYNC_DIRECTORY_STEP: &str = "sync-directory";

/// The step log entry for renaming it over the target.
pub(crate) const COMMIT_STEP: &str = "commit";

/// The step log entry for removing a temporary file that will not be
/// committed.
pub(crate) const DISCARD_STEP: &str = "discard";

/// Where the fake puts the temporary file it stages beside `target`.
pub(crate) fn staged_path(target: &Path) -> String {
    format!("{}.staged", target.display())
}

/// The adapter every test in this folder asks its platform facts of.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The ruleset file's path, as a string a test can assert on.
pub(crate) fn ruleset_path() -> String {
    AgentPaths::nftables_ruleset_path().display().to_string()
}

/// The bans file's path, as a string a test can assert on.
pub(crate) fn bans_path() -> String {
    AgentPaths::nftables_bans_path().display().to_string()
}

/// The host ports every test in this folder renders with: sshd on 22, the
/// panel on 8443.
///
/// One ssh port, because that is the ordinary host and these tests are about
/// everything else. The multi-port shape has its own tests, and the polygon
/// asserts it against a real kernel.
pub(crate) fn ports() -> RulesetPorts {
    RulesetPorts {
        ssh_ports: vec![port(22)],
        panel_port: port(8443),
    }
}

/// A validated port number.
pub(crate) fn port(number: u32) -> Port {
    Port::parse(number).expect("a valid port")
}

/// A rule open to every source.
pub(crate) fn open_rule(number: u32, protocol: NftablesProtocol) -> FirewallRule {
    FirewallRule {
        port: port(number),
        protocol,
        source: SourceCidr::any_v4(),
    }
}

/// A rule restricted to one source network.
pub(crate) fn restricted_rule(number: u32, protocol: NftablesProtocol, cidr: &str) -> FirewallRule {
    FirewallRule {
        port: port(number),
        protocol,
        source: SourceCidr::parse(cidr).expect("a valid network"),
    }
}

/// The ruleset file this agent renders for `rules`, with the standard ports.
pub(crate) fn rendered(rules: &[FirewallRule]) -> String {
    let mut state = RulesetState::empty();
    for rule in rules {
        state = state.with(rule);
    }

    state.render(&ports()).expect("the ruleset renders")
}

/// The JSON `nft -j list set` answers with for `elements` of `set`.
///
/// Both member shapes the real tool writes are produced: a bare string for a
/// member with no timeout, and an object carrying `val` and `expires` for one
/// with a timeout.
fn set_listing_json(set: &str, elements: &[FakeElement]) -> String {
    let members: Vec<String> = elements
        .iter()
        .filter(|element| element.set == set)
        .map(|element| match element.seconds {
            Some(seconds) => format!(
                "{{\"elem\":{{\"val\":\"{}\",\"timeout\":{seconds},\"expires\":{seconds}}}}}",
                element.address
            ),
            None => format!("\"{}\"", element.address),
        })
        .collect();

    let body = if members.is_empty() {
        String::new()
    } else {
        format!(",\"elem\":[{}]", members.join(","))
    };

    format!(
        "{{\"nftables\":[{{\"metainfo\":{{\"version\":\"1.0.9\"}}}},\
         {{\"set\":{{\"family\":\"inet\",\"name\":\"{set}\",\"table\":\"maran_bans\"{body}}}}}]}}"
    )
}

/// Reads the set and the address out of an `nft add/delete element` argument
/// vector.
fn element_of(arguments: &[&str]) -> FakeElement {
    let set = (*arguments.get(4).expect("a set name")).to_owned();
    let address = (*arguments.get(6).expect("an address")).to_owned();
    let seconds = arguments
        .iter()
        .position(|argument| *argument == "timeout")
        .and_then(|at| arguments.get(at + 1))
        .and_then(|value| value.strip_suffix('s'))
        .and_then(|value| value.parse::<u64>().ok());

    FakeElement {
        set,
        address,
        seconds,
    }
}
