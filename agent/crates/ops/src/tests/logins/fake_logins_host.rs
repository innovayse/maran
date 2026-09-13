//! The in-memory [`LoginsHost`] the login tests decide against.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host reads the machine's password database and locks real system
//! logins: a unit test can do neither, and a suite that tried would pass or
//! fail on whichever accounts the machine running it happens to have.
//!
//! **The fake holds passwd ROWS and nothing else.** It has no opinion about
//! which row is a login of which account — that is the decision under test, and
//! the enumeration this module replaces was untestable precisely because its
//! fake held a second copy of that opinion and could never disagree with the
//! code. A test here plants a row in a jail and reads back what the code made
//! of it.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::system_account::SystemAccount;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::logins::logins_error::LoginsError;
use crate::logins::logins_host::LoginsHost;

/// The uid the fake's password database holds for the test account.
///
/// Outside the system range, as a real hosting account's is.
pub(crate) const ACCOUNT_UID: u32 = 1001;

/// A uid belonging to some OTHER account on the same host.
pub(crate) const OTHER_UID: u32 = 1002;

/// One spawn the fake was asked to perform.
#[derive(Debug, Clone)]
pub(crate) struct RecordedSpawn {
    /// The whole argument vector, the program first — which is what a `ps`
    /// listing on the host would show.
    pub(crate) argv: Vec<String>,
}

/// A [`LoginsHost`] that keeps a password database and a set of locked logins
/// in memory.
pub(crate) struct FakeLoginsHost {
    /// The rows the "password database" holds.
    rows: Mutex<Vec<SystemAccount>>,
    /// Every spawn the fake was asked to perform, in order.
    spawns: Mutex<Vec<RecordedSpawn>>,
    /// The logins whose password the "host" holds as locked.
    ///
    /// A set rather than a flag on a row, because a test needs to be able to
    /// lock a login this fake never created — which is what a hand-made login
    /// on a real host is.
    locked: Mutex<Vec<String>>,
    /// The logins that have no password at all.
    ///
    /// The one measured asymmetry of the real tool: `usermod --unlock` refuses
    /// such a login while still exiting zero, so it stays locked.
    passwordless: Mutex<Vec<String>>,
    /// Whether the password database can be read at all.
    readable: Mutex<bool>,
    /// The status `usermod` exits with, when a test installed a refusal.
    usermod_status: Mutex<i32>,
    /// The status `passwd` exits with, when a test installed a refusal.
    passwd_status: Mutex<i32>,
    /// What `passwd -S` prints instead of its ordinary line, when a test
    /// installed something unreadable.
    passwd_output: Mutex<Option<String>>,
    /// The real uid of every process the "host" is running.
    ///
    /// A uid per process and not a count per uid, because what the cull must be
    /// judged on is WHICH processes it reached: a fake that held "three
    /// processes" could not tell a cull that killed the account's from one that
    /// killed a neighbour's.
    processes: Mutex<Vec<u32>>,
    /// The status `pkill` exits with, when a test installed a refusal.
    ///
    /// `None` means answer as the real tool does — 0 having signalled what it
    /// matched, 1 having matched nothing.
    pkill_status: Mutex<Option<i32>>,
}

impl FakeLoginsHost {
    /// A host whose password database holds exactly `rows`, as
    /// `(name, uid, home)`.
    ///
    /// The gid is set from the uid: nothing in this area reads it, and a second
    /// number a test had to supply for every row would be a number nobody
    /// checks.
    pub(crate) fn with_passwd(rows: &[(&str, u32, &str)]) -> Self {
        Self {
            rows: Mutex::new(
                rows.iter()
                    .map(|(name, uid, home)| SystemAccount {
                        name: (*name).to_owned(),
                        uid: *uid,
                        gid: *uid,
                        home: (*home).to_owned(),
                    })
                    .collect(),
            ),
            spawns: Mutex::new(Vec::new()),
            locked: Mutex::new(Vec::new()),
            passwordless: Mutex::new(Vec::new()),
            readable: Mutex::new(true),
            usermod_status: Mutex::new(0),
            passwd_status: Mutex::new(0),
            passwd_output: Mutex::new(None),
            processes: Mutex::new(Vec::new()),
            pkill_status: Mutex::new(None),
        }
    }

    /// Puts one process running as `uid` on the "host".
    pub(crate) fn with_process(self, uid: u32) -> Self {
        self.processes.lock().unwrap().push(uid);

        self
    }

    /// Makes `pkill` answer `status` whatever it matched.
    pub(crate) fn refuse_pkill_with(&self, status: i32) {
        *self.pkill_status.lock().unwrap() = Some(status);
    }

    /// The real uid of every process still running on the "host", in order.
    pub(crate) fn running_processes(&self) -> Vec<u32> {
        self.processes.lock().unwrap().clone()
    }

    /// Marks `name`'s password as already locked on the "host".
    pub(crate) fn with_locked(self, name: &str) -> Self {
        self.locked.lock().unwrap().push(name.to_owned());

        self
    }

    /// Marks `name` as a login that has no password at all.
    pub(crate) fn with_passwordless(self, name: &str) -> Self {
        self.passwordless.lock().unwrap().push(name.to_owned());

        self
    }

    /// Makes the password database unreadable, as a permission or a missing
    /// file makes the real one.
    pub(crate) fn refuse_to_be_read(&self) {
        *self.readable.lock().unwrap() = false;
    }

    /// Makes `usermod` refuse with `status`.
    pub(crate) fn refuse_usermod_with(&self, status: i32) {
        *self.usermod_status.lock().unwrap() = status;
    }

    /// Makes `passwd` refuse with `status`.
    pub(crate) fn refuse_passwd_with(&self, status: i32) {
        *self.passwd_status.lock().unwrap() = status;
    }

    /// Makes `passwd -S` print `text` instead of its ordinary line.
    pub(crate) fn passwd_prints(&self, text: &str) {
        *self.passwd_output.lock().unwrap() = Some(text.to_owned());
    }

    /// Whether `name`'s password is locked on the "host" right now.
    pub(crate) fn is_locked(&self, name: &str) -> bool {
        self.locked.lock().unwrap().iter().any(|held| held == name)
    }

    /// Every login the "host" holds as locked right now, in name order.
    pub(crate) fn locked_users(&self) -> Vec<String> {
        let mut locked = self.locked.lock().unwrap().clone();
        locked.sort();

        locked
    }

    /// Every spawn the fake was asked to perform, in order.
    pub(crate) fn spawns(&self) -> Vec<RecordedSpawn> {
        self.spawns.lock().unwrap().clone()
    }

    /// Answers as `usermod --lock` / `--unlock` was measured to answer.
    ///
    /// Both are idempotent and both exit zero — including the one case that
    /// surprises: unlocking a login that has NO password leaves it locked and
    /// still succeeds. Modelling that here is what stops a test agreeing with
    /// an implementation that assumed otherwise.
    fn usermod(&self, arguments: &[&str]) -> CommandOutcome {
        let status = *self.usermod_status.lock().unwrap();
        if status != 0 {
            return CommandOutcome {
                status,
                stdout: String::new(),
                stderr: String::new(),
            };
        }

        let name = arguments.last().copied().unwrap_or_default().to_owned();
        let mut locked = self.locked.lock().unwrap();
        match arguments.first().copied() {
            Some("--lock") => {
                if !locked.contains(&name) {
                    locked.push(name);
                }
            }
            Some("--unlock") => {
                let passwordless = self.passwordless.lock().unwrap();
                if !passwordless.contains(&name) {
                    locked.retain(|held| *held != name);
                }
            }
            other => panic!("the fake was asked for an unexpected usermod flag: {other:?}"),
        }

        CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        }
    }

    /// Answers as `pkill --signal KILL --count --uid <uid>` was measured to
    /// answer on both polygon images.
    ///
    /// Measured rather than assumed, because the statuses are the whole
    /// interface: it prints the number of processes it matched and exits **0**
    /// when that number is at least one, and prints `0` and exits **1** when
    /// nothing matched. A fake that returned 0 for "nothing matched" would let
    /// an implementation treat an idle account's suspension as a failure and
    /// still pass.
    ///
    /// It kills only the processes whose uid was asked for, which is the
    /// property under test: a cull aimed at the wrong uid leaves the right
    /// processes running and this fake will say so.
    fn pkill(&self, arguments: &[&str]) -> CommandOutcome {
        let uid: u32 = arguments
            .last()
            .copied()
            .unwrap_or_default()
            .parse()
            .expect("the cull must name a uid as a number");

        if let Some(status) = *self.pkill_status.lock().unwrap() {
            return CommandOutcome {
                status,
                stdout: String::new(),
                stderr: String::new(),
            };
        }

        let mut processes = self.processes.lock().unwrap();
        let matched = processes.iter().filter(|held| **held == uid).count();
        processes.retain(|held| *held != uid);

        CommandOutcome {
            status: i32::from(matched == 0),
            stdout: format!("{matched}\n"),
            stderr: String::new(),
        }
    }

    /// Answers as `passwd -S <login>` does: the name, then the state.
    fn password_status(&self, arguments: &[&str]) -> CommandOutcome {
        let status = *self.passwd_status.lock().unwrap();
        if status != 0 {
            return CommandOutcome {
                status,
                stdout: String::new(),
                stderr: String::new(),
            };
        }

        if let Some(text) = self.passwd_output.lock().unwrap().clone() {
            return CommandOutcome {
                status: 0,
                stdout: text,
                stderr: String::new(),
            };
        }

        let name = arguments.last().copied().unwrap_or_default();
        let state = if self.is_locked(name) { "L" } else { "P" };

        CommandOutcome {
            status: 0,
            stdout: format!("{name} {state} 2026-09-09 0 99999 7 -1\n"),
            stderr: String::new(),
        }
    }
}

impl LoginsHost for FakeLoginsHost {
    /// Hands back the planted rows, or refuses when a test made the database
    /// unreadable.
    fn read_passwd(&self, passwd_database: &str) -> Result<Vec<SystemAccount>, LoginsError> {
        assert!(
            passwd_database.starts_with('/'),
            "the password database must be an absolute path from the adapter: {passwd_database}"
        );

        if !*self.readable.lock().unwrap() {
            return Err(LoginsError::AccountMissing);
        }

        Ok(self.rows.lock().unwrap().clone())
    }

    /// Records the spawn, then answers as the tool would.
    ///
    /// A program the fake does not know panics rather than answering blandly: a
    /// fake that shrugs at an unexpected tool is a fake that lets an operation
    /// run anything and still pass.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, LoginsError> {
        let mut argv = vec![program.to_owned()];
        argv.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.spawns.lock().unwrap().push(RecordedSpawn { argv });

        if program.ends_with("usermod") {
            return Ok(self.usermod(arguments));
        }
        if program.ends_with("passwd") {
            return Ok(self.password_status(arguments));
        }
        if program.ends_with("pkill") {
            return Ok(self.pkill(arguments));
        }

        panic!("the fake was asked to run an unexpected program: {program}");
    }
}

/// The adapter every test in this folder asks its platform facts of.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The account every test in this folder is about.
pub(crate) fn account() -> AccountName {
    AccountName::parse("alice").expect("valid")
}

/// The account's own passwd row, which every realistic database holds.
///
/// A row and not a fact the fake invents: the uid the unmanaged count is
/// measured against is read from this row, so a test that left it out would be
/// testing a host the panel could not have created.
pub(crate) fn account_row() -> (&'static str, u32, &'static str) {
    ("alice", ACCOUNT_UID, "/home/alice")
}
