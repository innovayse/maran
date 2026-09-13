//! The in-memory [`SftpHost`] the SFTP tests decide against.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host creates system logins and mounts filesystems: a unit test cannot
//! do either, and a suite that tried would pass or fail on whether it happened
//! to run as root. What a unit test CAN pin is the decision — which tool an
//! operation chooses, with which arguments, what it puts on standard input
//! rather than in the argument vector, which directories and unit files it
//! insists on first, and what it makes of each refusal.
//!
//! The fake answers as the real tools do rather than as the operation hopes:
//! `useradd` against a name it already holds exits 9, `userdel` against a name
//! it does not hold exits 6. "Create twice" and "delete twice" therefore
//! converge here for the same reason they converge on a host, not because the
//! fake was told the answer.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::safe_write::model::{Reload, Validator};
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::model::account_ownership::AccountOwnership;
use crate::sftp::model::sftp_user_request::SftpUserRequest;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// `useradd`'s exit status for a name that is already taken.
const NAME_IN_USE: i32 = 9;

/// `userdel`'s exit status for a user that is not there.
const NO_SUCH_USER: i32 = 6;

/// `getent`'s exit status for a key it does not hold.
const KEY_NOT_FOUND: i32 = 2;

/// The character a locked shadow password field begins with.
const LOCK_MARKER: &str = "!";

/// What the fake's shadow entries carry where a real one carries a hash.
///
/// Shaped like a real crypt string so that a classification which looked at the
/// shape rather than at the marker would still be exercised, and deliberately
/// not a plausible hash of anything: nothing here is a credential.
const FAKE_HASH: &str = "$6$fakesalt$fakehashfakehashfakehash";

/// The user id the fake's password database holds for the test account.
///
/// Outside the system range, as a real hosting account's is, and different from
/// [`ACCOUNT_GID`] so that a test can tell the two apart in an argument vector —
/// two equal numbers would let a uid passed as a gid pass unnoticed.
pub(crate) const ACCOUNT_UID: u32 = 1042;

/// The primary group id the fake's password database holds for the account.
pub(crate) const ACCOUNT_GID: u32 = 1043;

/// One line of the fake's password database.
#[derive(Debug, Clone)]
pub(crate) struct PasswdEntry {
    /// The login name.
    name: String,
    /// The passwd home directory: an account's own home, or a login's jail.
    home: String,
}

/// One spawn the fake was asked to perform.
#[derive(Debug, Clone)]
pub(crate) struct RecordedSpawn {
    /// The whole argument vector, the program first — which is what a `ps`
    /// listing on the host would show, and therefore what a test asserting
    /// "the password is not on the command line" has to look at.
    pub(crate) argv: Vec<String>,
    /// Everything written to the program's standard input.
    pub(crate) stdin: String,
}

/// One config file the fake was asked to write through the protocol.
#[derive(Debug, Clone)]
pub(crate) struct RecordedConfig {
    /// Absolute path the file was to be written to.
    pub(crate) target: String,
    /// The rendered content.
    pub(crate) contents: String,
    /// The validator's argument vector, the program first.
    pub(crate) validator: Vec<String>,
    /// The reload's argument vector, the program first.
    pub(crate) reload: Vec<String>,
}

/// A [`SftpHost`] that keeps a host's logins, directories and unit files in
/// memory.
pub(crate) struct FakeSftpHost {
    /// The system logins the "host" holds, each with the passwd home it was
    /// created with.
    ///
    /// The home is modelled and not ignored, because it is what tells one
    /// account's SFTP login from a NEIGHBOURING ACCOUNT of the same spelling —
    /// the account `alice_bob` and the login `bob` of account `alice` are the
    /// same string, and only the home field distinguishes them. A fake that held
    /// names alone could not express the case, and the case is a real one that
    /// reached a real host.
    users: Mutex<Vec<PasswdEntry>>,
    /// Every spawn the fake was asked to perform, in order.
    spawns: Mutex<Vec<RecordedSpawn>>,
    /// Every directory the fake was asked to create, with its mode.
    directories: Mutex<Vec<(String, u32)>>,
    /// Every config file the fake was asked to write.
    configs: Mutex<Vec<RecordedConfig>>,
    /// The status `chpasswd` exits with, when a test installed a refusal.
    chpasswd_status: Mutex<i32>,
    /// Whether writing a config file refuses.
    config_refuses: Mutex<bool>,
    /// Whether the password database holds the hosting account.
    account_exists: Mutex<bool>,
    /// The paths the "host" holds — unit files and jail directories alike.
    ///
    /// One list for both, because the operation under test only ever asks
    /// whether a path is there and whether it will come away; a fake that
    /// modelled a file system would be a fake with more decisions in it than the
    /// code it judges.
    paths: Mutex<Vec<String>>,
    /// The paths whose removal refuses, which is what a mount point that is
    /// still mounted looks like to `rmdir`.
    unremovable: Mutex<Vec<String>>,
    /// The status the service manager exits with, when a test installed a
    /// refusal.
    systemctl_status: Mutex<i32>,
    /// The logins whose password the "host" holds as locked.
    ///
    /// Modelled as a set rather than a flag on [`PasswdEntry`] because a test
    /// needs to be able to lock a login this fake never created — which is what
    /// a hand-made login on a real host is.
    locked: Mutex<Vec<String>>,
    /// The logins that have no password at all.
    ///
    /// The one measured asymmetry of the real tool: `usermod --unlock` refuses
    /// such a login while still exiting zero, so it stays locked. A fake that
    /// unlocked it would agree with an implementation that assumed the tool did.
    passwordless: Mutex<Vec<String>>,
    /// The status `usermod` exits with, when a test installed a refusal.
    usermod_status: Mutex<i32>,
    /// Whether `usermod --lock` does nothing while still exiting zero.
    ///
    /// The shape of a tool that answered success and changed nothing, which is
    /// the one thing an exit status cannot rule out — and therefore the only way
    /// to exercise a caller that reads the field back instead of trusting it.
    lock_ignored: Mutex<bool>,
    /// The status `passwd` exits with, when a test installed a refusal.
    passwd_status: Mutex<i32>,
    /// What `passwd -S` prints instead of its ordinary line, when a test
    /// installed something unreadable.
    passwd_output: Mutex<Option<String>>,
    /// The answers `account_ownership` gives, one per call, the last repeating.
    ///
    /// A QUEUE and not one pair, because the thing worth testing about this
    /// method is that a creation asks it TWICE and compares: a fake with one
    /// answer could not exhibit a difference between the two reads, which is
    /// the whole window the audit measured.
    ownerships: Mutex<Vec<Option<AccountOwnership>>>,
    /// How many times `account_ownership` has been asked.
    ownership_questions: Mutex<usize>,
}

impl FakeSftpHost {
    /// A host that holds no logins and refuses nothing.
    pub(crate) fn new() -> Self {
        Self {
            users: Mutex::new(Vec::new()),
            spawns: Mutex::new(Vec::new()),
            directories: Mutex::new(Vec::new()),
            configs: Mutex::new(Vec::new()),
            chpasswd_status: Mutex::new(0),
            config_refuses: Mutex::new(false),
            account_exists: Mutex::new(true),
            paths: Mutex::new(Vec::new()),
            unremovable: Mutex::new(Vec::new()),
            systemctl_status: Mutex::new(0),
            locked: Mutex::new(Vec::new()),
            passwordless: Mutex::new(Vec::new()),
            usermod_status: Mutex::new(0),
            lock_ignored: Mutex::new(false),
            passwd_status: Mutex::new(0),
            passwd_output: Mutex::new(None),
            ownerships: Mutex::new(vec![Some(AccountOwnership {
                uid: ACCOUNT_UID,
                gid: ACCOUNT_GID,
            })]),
            ownership_questions: Mutex::new(0),
        }
    }

    /// Makes `account_ownership` answer these in order, the last repeating.
    pub(crate) fn answers_ownerships(&self, answers: &[Option<AccountOwnership>]) {
        *self.ownerships.lock().unwrap() = answers.to_vec();
    }

    /// How many times the "password database" was asked about the account.
    pub(crate) fn ownership_questions(&self) -> usize {
        *self.ownership_questions.lock().unwrap()
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

    /// Whether `name`'s password is locked on the "host" right now.
    pub(crate) fn is_locked(&self, name: &str) -> bool {
        self.locked.lock().unwrap().iter().any(|held| held == name)
    }

    /// Makes `usermod --lock` change nothing while still exiting zero.
    pub(crate) fn ignore_locking(&self) {
        *self.lock_ignored.lock().unwrap() = true;
    }

    /// Puts `path` on the "host", so `path_exists` finds it and a removal has
    /// something to take away.
    pub(crate) fn with_path(self, path: &str) -> Self {
        self.paths.lock().unwrap().push(path.to_owned());
        self
    }

    /// Makes the removal of `path` refuse, the way `rmdir` refuses a mount
    /// point that is still mounted.
    pub(crate) fn refuse_removal_of(self, path: &str) -> Self {
        self.unremovable.lock().unwrap().push(path.to_owned());
        self
    }

    /// Makes the service manager exit with `status`.
    pub(crate) fn refuse_systemctl_with(&self, status: i32) {
        *self.systemctl_status.lock().unwrap() = status;
    }

    /// The paths the "host" holds now.
    pub(crate) fn paths(&self) -> Vec<String> {
        self.paths.lock().unwrap().clone()
    }

    /// Removes `path` when it is there, refusing when a test said it must.
    fn take_path(&self, path: &Path) -> Result<(), SftpError> {
        let wanted = path.display().to_string();
        if self.unremovable.lock().unwrap().contains(&wanted) {
            return Err(SftpError::JailFailed);
        }

        self.paths.lock().unwrap().retain(|held| *held != wanted);

        Ok(())
    }

    /// A host that already holds the login `name`, jailed as the agent jails one.
    pub(crate) fn with_existing(name: &str) -> Self {
        Self::new().with_login(name)
    }

    /// Adds the SFTP login `name` to the "host", homed in its account's jail.
    pub(crate) fn with_login(self, name: &str) -> Self {
        let home = jail_directory_of(name);
        self.users.lock().unwrap().push(PasswdEntry {
            name: name.to_owned(),
            home,
        });

        self
    }

    /// Adds a HOSTING ACCOUNT called `name` to the "host", homed under `/home`.
    ///
    /// Not a login: it is what a neighbouring account looks like in the same
    /// namespace, which is the collision this area has to get right.
    pub(crate) fn with_hosting_account(self, name: &str) -> Self {
        self.users.lock().unwrap().push(PasswdEntry {
            name: name.to_owned(),
            home: format!("/home/{name}"),
        });

        self
    }

    /// Adds the login `name` to the "host", homed at `home`.
    ///
    /// The escape hatch the two helpers above do not cover: a passwd entry
    /// whose home is neither this account's jail nor `/home/<name>` — the FTPS
    /// login of the same account, or a login inside a NEIGHBOURING account's
    /// jail. The home is the only thing that tells those apart from this
    /// account's own logins, so a fake that could not express one could not
    /// test the check that reads it.
    pub(crate) fn with_foreign_login(self, name: &str, home: &str) -> Self {
        self.users.lock().unwrap().push(PasswdEntry {
            name: name.to_owned(),
            home: home.to_owned(),
        });

        self
    }

    /// Makes `chpasswd` exit with `status`.
    pub(crate) fn refuse_password_with(&self, status: i32) {
        *self.chpasswd_status.lock().unwrap() = status;
    }

    /// Makes every config write refuse.
    pub(crate) fn refuse_config_writes(&self) {
        *self.config_refuses.lock().unwrap() = true;
    }

    /// Empties the "password database", so the hosting account is not there.
    pub(crate) fn forget_the_account(&self) {
        *self.account_exists.lock().unwrap() = false;
    }

    /// Every spawn the fake was asked to perform, in order.
    pub(crate) fn spawns(&self) -> Vec<RecordedSpawn> {
        self.spawns.lock().unwrap().clone()
    }

    /// The first spawn whose program's file name is `program`.
    pub(crate) fn spawn_of(&self, program: &str) -> Option<RecordedSpawn> {
        self.spawns()
            .into_iter()
            .find(|spawn| spawn.argv.first().is_some_and(|p| p.ends_with(program)))
    }

    /// The system logins the "host" holds now, by name.
    pub(crate) fn users(&self) -> Vec<String> {
        self.users
            .lock()
            .unwrap()
            .iter()
            .map(|entry| entry.name.clone())
            .collect()
    }

    /// Every directory the fake was asked to create, with its mode.
    pub(crate) fn directories(&self) -> Vec<(String, u32)> {
        self.directories.lock().unwrap().clone()
    }

    /// Every config file the fake was asked to write.
    pub(crate) fn configs(&self) -> Vec<RecordedConfig> {
        self.configs.lock().unwrap().clone()
    }

    /// The argument vector of a validator or reload, the program first.
    fn command_of(program: &str, arguments: &[&str]) -> Vec<String> {
        let mut argv = vec![program.to_owned()];
        argv.extend(arguments.iter().map(|argument| (*argument).to_owned()));

        argv
    }
}

impl FakeSftpHost {
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
                if !*self.lock_ignored.lock().unwrap() && !locked.contains(&name) {
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

    /// Answers as `getent shadow <login>` does: the entry, or nothing.
    ///
    /// The password field is composed from the two facts the fake already holds
    /// rather than stored a third time, so a test that locks a login and a test
    /// that reads its field cannot disagree:
    ///
    /// ```text
    /// no password at all      !            (markers only — nothing to unlock)
    /// locked over a password  !<hash>
    /// a usable password       <hash>
    /// ```
    ///
    /// A login the "host" does not hold exits [`KEY_NOT_FOUND`], which is what
    /// the real tool does and what tells "this login is not here" from "the
    /// name service would not answer".
    fn shadow_entry(&self, arguments: &[&str]) -> CommandOutcome {
        let name = arguments.last().copied().unwrap_or_default();
        if !self
            .users
            .lock()
            .unwrap()
            .iter()
            .any(|entry| entry.name == name)
        {
            return CommandOutcome {
                status: KEY_NOT_FOUND,
                stdout: String::new(),
                stderr: String::new(),
            };
        }

        let field = if self
            .passwordless
            .lock()
            .unwrap()
            .iter()
            .any(|held| held == name)
        {
            LOCK_MARKER.to_owned()
        } else if self.is_locked(name) {
            format!("{LOCK_MARKER}{FAKE_HASH}")
        } else {
            FAKE_HASH.to_owned()
        };

        CommandOutcome {
            status: 0,
            stdout: format!("{name}:{field}:20000:0:99999:7:::\n"),
            stderr: String::new(),
        }
    }

    /// Answers as `passwd -S <login>` does: the name, then one letter for the
    /// state.
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
            stdout: format!("{name} {state} 2026-09-08 0 99999 7 -1\n"),
            stderr: String::new(),
        }
    }
}

impl SftpHost for FakeSftpHost {
    /// Records the spawn, then answers as the tool would.
    ///
    /// A program the fake does not know panics rather than answering blandly: a
    /// fake that shrugs at an unexpected tool is a fake that lets an operation
    /// run anything and still pass.
    fn run(
        &self,
        program: &str,
        arguments: &[&str],
        stdin: Option<&str>,
    ) -> Result<CommandOutcome, SftpError> {
        self.spawns.lock().unwrap().push(RecordedSpawn {
            argv: Self::command_of(program, arguments),
            stdin: stdin.unwrap_or_default().to_owned(),
        });

        let status = if program.ends_with("useradd") {
            // The login is the last argument, as it is for the real tool, and
            // its home is the value of `--home-dir` — which the fake records
            // rather than discards, because the home is what the account
            // cascade tells a login from a neighbouring account by.
            let name = arguments.last().copied().unwrap_or_default().to_owned();
            let home = arguments
                .iter()
                .position(|argument| *argument == "--home-dir")
                .and_then(|at| arguments.get(at + 1))
                .map_or_else(|| format!("/home/{name}"), |home| (*home).to_owned());
            let mut users = self.users.lock().unwrap();
            if users.iter().any(|entry| entry.name == name) {
                NAME_IN_USE
            } else {
                users.push(PasswdEntry { name, home });
                0
            }
        } else if program.ends_with("userdel") {
            let name = arguments.last().copied().unwrap_or_default().to_owned();
            let mut users = self.users.lock().unwrap();
            if users.iter().any(|entry| entry.name == name) {
                users.retain(|held| held.name != name);
                0
            } else {
                NO_SUCH_USER
            }
        } else if program.ends_with("chpasswd") {
            let status = *self.chpasswd_status.lock().unwrap();
            if status == 0 {
                // The real tool REPLACES the shadow password field rather than
                // editing it, so the `!` a suspension wrote is gone and a login
                // that had no password now has one. Modelled here because it is
                // the defect: a fake that left the lock in place would agree
                // with an implementation that assumed `chpasswd` respected it.
                let name = stdin
                    .unwrap_or_default()
                    .split(':')
                    .next()
                    .unwrap_or_default()
                    .to_owned();
                self.locked.lock().unwrap().retain(|held| *held != name);
                self.passwordless
                    .lock()
                    .unwrap()
                    .retain(|held| *held != name);
            }

            status
        } else if program.ends_with("getent") {
            return Ok(self.shadow_entry(arguments));
        } else if program.ends_with("systemctl") {
            *self.systemctl_status.lock().unwrap()
        } else if program.ends_with("usermod") {
            return Ok(self.usermod(arguments));
        } else if program.ends_with("passwd") {
            return Ok(self.password_status(arguments));
        } else {
            panic!("the fake was asked to run an unexpected program: {program}");
        };

        Ok(CommandOutcome {
            status,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    /// Answers with the account's ids, or refuses when a test emptied the
    /// "password database".
    fn account_ownership(&self, _account: &AccountName) -> Result<AccountOwnership, SftpError> {
        if !*self.account_exists.lock().unwrap() {
            return Err(SftpError::AccountMissing);
        }

        let mut asked = self.ownership_questions.lock().unwrap();
        let answers = self.ownerships.lock().unwrap();
        let index = (*asked).min(answers.len().saturating_sub(1));
        *asked += 1;

        answers[index].ok_or(SftpError::AccountMissing)
    }

    /// Records the directory and its mode.
    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), SftpError> {
        self.directories
            .lock()
            .unwrap()
            .push((path.display().to_string(), mode));

        Ok(())
    }

    /// Decodes the "host"'s logins the way the real password database is
    /// decoded, so a test pins the predicate and not the fake's opinion of it.
    fn account_logins(
        &self,
        passwd_database: &str,
        account: &AccountName,
        jail_directory: &str,
    ) -> Result<Vec<SftpUserName>, SftpError> {
        assert!(
            passwd_database.starts_with('/'),
            "the password database must be an absolute path from the adapter: {passwd_database}"
        );

        if !*self.account_exists.lock().unwrap() {
            return Err(SftpError::AccountMissing);
        }

        let mut logins: Vec<SftpUserName> = self
            .users
            .lock()
            .unwrap()
            .iter()
            .filter(|entry| entry.home == jail_directory)
            .filter_map(|entry| {
                let (owner, requested) = entry.name.rsplit_once('_')?;
                if owner != account.as_str() {
                    return None;
                }

                SftpUserName::for_account(account, requested).ok()
            })
            .collect();
        logins.sort_by(|left, right| left.as_str().cmp(right.as_str()));

        Ok(logins)
    }

    /// Whether a test put `path` on the "host".
    fn path_exists(&self, path: &Path) -> bool {
        self.paths
            .lock()
            .unwrap()
            .contains(&path.display().to_string())
    }

    /// Takes the file away, or refuses when a test said it must.
    fn remove_file(&self, path: &Path) -> Result<(), SftpError> {
        self.take_path(path)
    }

    /// Takes the directory away, or refuses when a test said it must — which is
    /// the shape a mount point that is still mounted has.
    fn remove_directory(&self, path: &Path) -> Result<(), SftpError> {
        self.take_path(path)
    }

    /// Records the config write, or refuses when a test asked it to.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SftpError> {
        if *self.config_refuses.lock().unwrap() {
            return Err(SftpError::JailFailed);
        }

        self.configs.lock().unwrap().push(RecordedConfig {
            target: target.display().to_string(),
            contents: contents.to_owned(),
            validator: Self::command_of(validator.program, validator.arguments),
            reload: Self::command_of(reload.program, reload.arguments),
        });

        Ok(())
    }
}

/// The jail directory the login `name` would have been created with.
///
/// Derived from the login's own account half, exactly as `create_sftp_user`
/// derives it, so the fake's password database says what a real one would.
fn jail_directory_of(name: &str) -> String {
    let owner = name.rsplit_once('_').map_or(name, |(owner, _)| owner);
    let account = AccountName::parse(owner).expect("the fixture's own names are valid");

    AccountJail::for_account(&account, distro().systemd_unit_directory())
        .directory()
        .to_owned()
}

/// The adapter every test in this folder asks its platform facts of.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The account every test in this folder is about.
pub(crate) fn account() -> AccountName {
    AccountName::parse("alice").expect("valid")
}

/// `alice`'s `web` SFTP login.
pub(crate) fn web_user() -> SftpUserName {
    SftpUserName::for_account(&account(), "web").expect("valid")
}

/// The password every test in this folder uses, so a leak of it is greppable.
pub(crate) const TEST_PASSWORD: &str = "Gen3rated-pw";

/// A request for `alice`'s `web` login.
pub(crate) fn web_request() -> SftpUserRequest {
    SftpUserRequest {
        account: account(),
        user: web_user(),
        password: Password::parse(TEST_PASSWORD).expect("valid"),
    }
}
