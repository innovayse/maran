//! A host that answers the FTPS operations without a daemon, a socket or a file.

// A fake's lock can only be poisoned by a failing test, and a failing assertion
// IS the reporting mechanism there.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::ErrorKind;
use std::path::Path;
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::passive_address::PassiveAddress;
use maran_agent_core::validation::web::port::Port;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::candidate_outcome::CandidateOutcome;
use crate::ftps::model::ftps_configuration::FtpsConfiguration;
use crate::ftps::model::ftps_jail::FtpsJail;
use crate::ftps::model::ftps_user_request::FtpsUserRequest;
use crate::safe_write::model::{Reload, Validator};
use crate::sftp::AccountOwnership;
use crate::sites::SiteCertificate;
use crate::ssl::CertificateState;

/// The hostname every test in this area serves FTPS for.
pub(crate) const HOSTNAME: &str = "ftp.example.test";

/// The greeting a healthy control port answers with.
pub(crate) const GREETING: &str = "220 Maran FTPS\r\n";

/// The exit status the service manager gives for a unit that is not active.
const NOT_ACTIVE: i32 = 3;

/// `getent`'s exit status for a key it does not hold.
const GETENT_KEY_NOT_FOUND: i32 = 2;

/// The shadow field a successful `chpasswd` leaves behind.
///
/// Shaped like a real crypt string so that
/// [`StoredPassword`](crate::accounts::StoredPassword) classifies it the way it
/// would classify the host's own — and carrying no lock marker, which is exactly
/// what makes the suspension re-assert necessary.
const CHPASSWD_HASH: &str = "$6$fake$hash";

/// One thing the fake was asked to run.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct Spawn {
    /// The program, followed by its arguments.
    pub(crate) argv: Vec<String>,
    /// Everything written to the program's standard input, if anything was.
    ///
    /// `None` and `Some(String::new())` are deliberately different: the first is
    /// a spawn with no pipe at all, the second a pipe that carried nothing. A
    /// test asserting that a password never reached an argument vector has to be
    /// able to see which spawn was given the pipe.
    pub(crate) stdin: Option<String>,
}

/// One system login the fake holds, as `useradd` left it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct CreatedLogin {
    /// The login name.
    pub(crate) name: String,
    /// Its numeric user id.
    pub(crate) uid: u32,
    /// Its numeric primary group id.
    pub(crate) gid: u32,
    /// Its login shell.
    pub(crate) shell: String,
    /// The supplementary groups it was put in.
    pub(crate) groups: Vec<String>,
    /// Its passwd home directory, which for a transfer login is its jail.
    pub(crate) home: String,
}

/// What the host was told to pretend about itself.
///
/// A per-argv fake and not a [`crate::test_support::recording_commands`] one:
/// `is-active` has to answer differently from `restart`, and differently again
/// after a restart has happened, which the record-and-replay core cannot express
/// (rules/rust.md "the rule of two" — a fake that answers per-argv is a different
/// kind of fake and keeps its own body).
pub(crate) struct FakeFtpsHost {
    /// Whether certificate material exists for the hostname.
    certificate_present: bool,
    /// The bytes at the live configuration path, if any.
    live: Mutex<Option<String>>,
    /// Every configuration ever written, in order.
    written: Mutex<Vec<String>>,
    /// Every program the host was asked to run, in order.
    spawns: Mutex<Vec<Spawn>>,
    /// Whether the daemon is up.
    running: Mutex<bool>,
    /// Whether the unit is in the boot sequence.
    enabled: Mutex<bool>,
    /// What the candidate daemon does: `None` accepts, `Some(output)` refuses.
    candidate_refusal: Option<String>,
    /// The error an IPv6 listening bind is refused with, if any.
    ipv6_bind_error: Option<ErrorKind>,
    /// Whether the control port answers once the daemon is up.
    control_port_answers: bool,
    /// What the control port says when it answers.
    greeting: String,
    /// Whether a restart brings the daemon up, or exits non-zero.
    restart_succeeds: bool,
    /// Whether reading the live configuration fails.
    config_readable: bool,
    /// Whether the config-write protocol fails mechanically.
    config_writable: bool,
    /// Whether the service manager can be started at all.
    service_manager_available: bool,
    /// Whether an ephemeral port can be obtained.
    ephemeral_port_available: bool,
    /// Whether reading the certificate material fails.
    certificate_readable: bool,
    /// The hosting account these login operations are for, when there is one.
    account: Option<String>,
    /// What `account_ownership` answers, question by question.
    ///
    /// A queue rather than one value, because the operation asks TWICE and the
    /// whole point of the second question is that the answer may have changed.
    /// The last entry repeats once the queue is exhausted, so a test that does
    /// not care about the re-read configures one answer and gets it twice.
    ownerships: Mutex<Vec<Option<AccountOwnership>>>,
    /// How many times `account_ownership` has been asked.
    ownership_questions: Mutex<usize>,
    /// Every login in the fake's password database.
    logins: Mutex<Vec<CreatedLogin>>,
    /// Every directory that exists, with the mode it was created at.
    directories: Mutex<Vec<(String, u32)>>,
    /// Every unit file that exists, by absolute path.
    unit_files: Mutex<Vec<String>>,
    /// Every unit file ever written, by absolute path, in order.
    written_units: Mutex<Vec<String>>,
    /// The bytes of every unit file that exists, by absolute path.
    unit_contents: Mutex<Vec<(String, String)>>,
    /// Whether the account's bind mount is up.
    mounted: Mutex<bool>,
    /// Whether the password database can be read at all.
    /// What a NEIGHBOURING operation leaves in the live configuration right
    /// after this "host" commits a daemon config write.
    ///
    /// Models the one interleaving an operation-level lock would exclude and
    /// there is none for: a second EnableFtps (or a DisableFtps) committing
    /// between this operation's own write and its post-swap check.
    neighbour_writes_after_the_swap: Mutex<Option<String>>,
    passwd_readable: bool,
    /// The status `chpasswd` exits with.
    chpasswd_status: i32,
    /// The status the service manager gives for a mount unit it is told to stop.
    unit_stop_status: i32,
    /// Whether the bind mount survives a service manager that reports success.
    stuck_mount: bool,
    /// Every login's shadow password field, by login name.
    ///
    /// Modelled rather than ignored because the password change is a
    /// read-modify-write of exactly this string: `chpasswd` REPLACES it, and a
    /// suspension is a `!` in front of it. A fake that only counted spawns could
    /// not tell a restored lock from an unlocked login.
    shadow: Mutex<Vec<(String, String)>>,
    /// The status `getent` exits with, when it is not the key-not-found one.
    getent_status: i32,
    /// What `getent shadow` prints instead of a well-formed entry, when set.
    garbled_shadow: Option<String>,
    /// The status `usermod` exits with.
    usermod_status: i32,
    /// Whether `usermod --lock` actually writes the marker.
    ///
    /// Split from the status on purpose: the measured behaviour this area
    /// defends against is a `usermod --lock` that exits ZERO having done
    /// nothing, so the two have to be settable apart.
    usermod_locks: bool,
    /// How many times the account's logins have been enumerated.
    ///
    /// The one observable that says whether this host was ASKED anything at all,
    /// which a spawn count cannot: an account with no jail and no unit reaches
    /// the end of its teardown without running a single program.
    login_enumerations: Mutex<usize>,
}

impl FakeFtpsHost {
    /// A host with no certificate material and no FTPS daemon.
    pub(crate) fn new() -> Self {
        Self {
            certificate_present: false,
            live: Mutex::new(None),
            written: Mutex::new(Vec::new()),
            spawns: Mutex::new(Vec::new()),
            running: Mutex::new(false),
            enabled: Mutex::new(false),
            candidate_refusal: None,
            ipv6_bind_error: None,
            control_port_answers: true,
            greeting: GREETING.to_owned(),
            restart_succeeds: true,
            config_readable: true,
            config_writable: true,
            service_manager_available: true,
            ephemeral_port_available: true,
            certificate_readable: true,
            account: None,
            ownerships: Mutex::new(Vec::new()),
            ownership_questions: Mutex::new(0),
            logins: Mutex::new(Vec::new()),
            directories: Mutex::new(Vec::new()),
            unit_files: Mutex::new(Vec::new()),
            written_units: Mutex::new(Vec::new()),
            unit_contents: Mutex::new(Vec::new()),
            mounted: Mutex::new(false),
            neighbour_writes_after_the_swap: Mutex::new(None),
            passwd_readable: true,
            chpasswd_status: 0,
            unit_stop_status: 0,
            stuck_mount: false,
            shadow: Mutex::new(Vec::new()),
            getent_status: 0,
            garbled_shadow: None,
            usermod_status: 0,
            usermod_locks: true,
            login_enumerations: Mutex::new(0),
        }
    }

    /// A host holding the account `account` with the ids `uid` and `gid`, and no
    /// jail, no login and no mount yet.
    pub(crate) fn for_account(account: &str, uid: u32, gid: u32) -> Self {
        let mut host = Self::new();
        host.account = Some(account.to_owned());
        *host.ownerships.lock().unwrap() = vec![Some(AccountOwnership { uid, gid })];
        host
    }

    /// The account's identity answers `answers` in order, so the operation's
    /// second question can be given a different answer from its first.
    pub(crate) fn answering_ownerships(self, answers: &[Option<AccountOwnership>]) -> Self {
        *self.ownerships.lock().unwrap() = answers.to_vec();
        self
    }

    /// How many times the account's identity was asked for.
    pub(crate) fn ownership_questions(&self) -> usize {
        *self.ownership_questions.lock().unwrap()
    }

    /// The host already holds the login `name`, in the account's jail.
    pub(crate) fn with_existing_login(self, name: &str) -> Self {
        let home = self.jail().directory().to_owned();
        self.logins.lock().unwrap().push(CreatedLogin {
            name: name.to_owned(),
            uid: 1,
            gid: 1,
            shell: distro().nologin_shell().to_owned(),
            groups: vec![distro().ftps_group().to_owned()],
            home,
        });
        // What `useradd` leaves behind on both families: lock markers and no
        // hash. A test that wants a login with a real password says so.
        self.shadow
            .lock()
            .unwrap()
            .push((name.to_owned(), "!".to_owned()));
        self
    }

    /// The host already holds a login of `name` whose home is NOT the account's
    /// FTPS jail — an SFTP login, or one an administrator made by hand.
    pub(crate) fn with_foreign_login(self, name: &str, home: &str) -> Self {
        self.logins.lock().unwrap().push(CreatedLogin {
            name: name.to_owned(),
            uid: 1,
            gid: 1,
            shell: distro().nologin_shell().to_owned(),
            groups: Vec::new(),
            home: home.to_owned(),
        });
        self
    }

    /// The account's jail is built and its bind mount is still up, so the mount
    /// point refuses to come away.
    pub(crate) fn with_jail_still_mounted(self) -> Self {
        let host = self.with_jail();
        *host.mounted.lock().unwrap() = true;
        host
    }

    /// The account's jail, its mount point and its unit file are all in place,
    /// with nothing mounted.
    pub(crate) fn with_jail(self) -> Self {
        let jail = self.jail();
        self.directories
            .lock()
            .unwrap()
            .push((jail.directory().to_owned(), 0o755));
        self.directories
            .lock()
            .unwrap()
            .push((jail.mount_point().to_owned(), 0o755));
        self.unit_files
            .lock()
            .unwrap()
            .push(jail.unit_path().to_owned());
        self
    }

    /// The password database cannot be read at all.
    pub(crate) fn unreadable_passwd(mut self) -> Self {
        self.passwd_readable = false;
        self
    }

    /// `chpasswd` refuses the line it is given.
    pub(crate) fn refusing_passwords(mut self) -> Self {
        self.chpasswd_status = 1;
        self
    }

    /// The service manager refuses to stop the account's mount unit.
    pub(crate) fn refusing_unit_stop(mut self) -> Self {
        self.unit_stop_status = 1;
        self
    }

    /// The service manager reports the mount stopped and it is still there.
    ///
    /// The state the teardown's `remove_dir` exists for, and the one piece of
    /// this fake's behaviour its author INVENTED rather than measured — which is
    /// why the same claim is settled against a real kernel in
    /// `agent/tests/account_deletion_on_a_real_host.rs`, where the mount, the
    /// unit and the EBUSY are all the machine's own.
    pub(crate) fn with_a_mount_that_will_not_come_down(mut self) -> Self {
        self.stuck_mount = true;
        self
    }

    /// The login `name`'s shadow password field is `field`.
    ///
    /// The four states [`StoredPassword`](crate::accounts::StoredPassword)
    /// classifies are all reachable through this: `""`, `"!"`, `"!$6$hash"` and
    /// `"$6$hash"`.
    pub(crate) fn with_shadow_field(self, name: &str, field: &str) -> Self {
        let mut shadow = self.shadow.lock().unwrap();
        shadow.retain(|(held, _)| held != name);
        shadow.push((name.to_owned(), field.to_owned()));
        drop(shadow);
        self
    }

    /// What the login `name`'s shadow password field holds now.
    pub(crate) fn shadow_field(&self, name: &str) -> Option<String> {
        self.shadow
            .lock()
            .unwrap()
            .iter()
            .find(|(held, _)| held == name)
            .map(|(_, field)| field.clone())
    }

    /// `getent` refuses with `status` rather than answering.
    pub(crate) fn getent_refusing(mut self, status: i32) -> Self {
        self.getent_status = status;
        self
    }

    /// `getent shadow` prints `line` instead of a well-formed entry.
    pub(crate) fn getent_printing(mut self, line: &str) -> Self {
        self.garbled_shadow = Some(line.to_owned());
        self
    }

    /// `usermod` refuses with a non-zero status.
    pub(crate) fn refusing_usermod(mut self) -> Self {
        self.usermod_status = 1;
        self
    }

    /// `usermod --lock` exits ZERO and writes nothing.
    ///
    /// The measured behaviour on a login there is nothing to lock, and the whole
    /// reason a restored lock is verified by re-reading the field.
    pub(crate) fn usermod_lock_that_does_nothing(mut self) -> Self {
        self.usermod_locks = false;
        self
    }

    /// The account's jail, derived exactly as the operations derive it.
    pub(crate) fn jail(&self) -> FtpsJail {
        FtpsJail::for_account(&self.account_name(), distro().systemd_unit_directory())
    }

    /// The hosting account's validated name.
    pub(crate) fn account_name(&self) -> AccountName {
        AccountName::parse(self.account.as_deref().unwrap_or("alice")).unwrap()
    }

    /// The single login `useradd` was asked to create, if it created one.
    pub(crate) fn created_user(&self) -> Option<CreatedLogin> {
        self.logins
            .lock()
            .unwrap()
            .iter()
            .find(|login| {
                login
                    .groups
                    .iter()
                    .any(|group| group == distro().ftps_group())
            })
            .cloned()
    }

    /// Every login the fake's password database holds, by name.
    pub(crate) fn login_names(&self) -> Vec<String> {
        self.logins
            .lock()
            .unwrap()
            .iter()
            .map(|login| login.name.clone())
            .collect()
    }

    /// The last thing the fake was asked to run.
    pub(crate) fn last_spawn(&self) -> Option<Spawn> {
        self.spawns().last().cloned()
    }

    /// The directory recorded at `path`, with the mode it was created at.
    pub(crate) fn created_directory(&self, path: &str) -> Option<(String, u32)> {
        self.directories
            .lock()
            .unwrap()
            .iter()
            .find(|(recorded, _)| recorded == path)
            .cloned()
    }

    /// Every directory the "host" was asked to create, in order, with its mode.
    ///
    /// The ORDER is the part `created_directory` cannot answer, and the base's
    /// mode is only correct if the base is created before anything that would
    /// bring it into existence as a by-product.
    pub(crate) fn created_directories(&self) -> Vec<(String, u32)> {
        self.directories.lock().unwrap().clone()
    }

    /// Whether a directory still exists at `path`.
    pub(crate) fn directory_still_exists(&self, path: &str) -> bool {
        self.created_directory(path).is_some()
    }

    /// Whether a unit file still exists at `path`.
    pub(crate) fn unit_file_exists(&self, path: &str) -> bool {
        self.unit_files
            .lock()
            .unwrap()
            .iter()
            .any(|recorded| recorded == path)
    }

    /// Every unit file ever written, in order.
    pub(crate) fn written_unit_paths(&self) -> Vec<String> {
        self.written_units.lock().unwrap().clone()
    }

    /// How many times this host was asked to enumerate the account's logins.
    pub(crate) fn login_enumerations(&self) -> usize {
        *self.login_enumerations.lock().unwrap()
    }

    /// Whether the account's bind mount is up.
    pub(crate) fn is_mounted(&self) -> bool {
        *self.mounted.lock().unwrap()
    }

    /// Records `argv` and `stdin`.
    fn record(&self, program: &str, arguments: &[&str], stdin: Option<&str>) {
        let mut argv = vec![program.to_owned()];
        argv.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.spawns.lock().unwrap().push(Spawn {
            argv,
            stdin: stdin.map(ToOwned::to_owned),
        });
    }

    /// Answers a `useradd` the way the shadow suite would.
    fn useradd_answer(&self, arguments: &[&str]) -> CommandOutcome {
        let Some(name) = arguments.last().copied() else {
            return refusal(1);
        };
        if self
            .logins
            .lock()
            .unwrap()
            .iter()
            .any(|login| login.name == name)
        {
            // The shadow suite's E_NAME_IN_USE.
            return refusal(9);
        }

        let value_after = |flag: &str| -> String {
            arguments
                .iter()
                .position(|argument| *argument == flag)
                .and_then(|index| arguments.get(index + 1))
                .map(|value| (*value).to_owned())
                .unwrap_or_default()
        };
        let groups = value_after("--groups");

        self.logins.lock().unwrap().push(CreatedLogin {
            name: name.to_owned(),
            uid: value_after("--uid").parse().unwrap_or_default(),
            gid: value_after("--gid").parse().unwrap_or_default(),
            shell: value_after("--shell"),
            groups: if groups.is_empty() {
                Vec::new()
            } else {
                groups.split(',').map(ToOwned::to_owned).collect()
            },
            home: value_after("--home-dir"),
        });

        refusal(0)
    }

    /// Answers a `userdel` the way the shadow suite would.
    fn userdel_answer(&self, arguments: &[&str]) -> CommandOutcome {
        let Some(name) = arguments.last().copied() else {
            return refusal(1);
        };
        let mut logins = self.logins.lock().unwrap();
        let before = logins.len();
        logins.retain(|login| login.name != name);
        if logins.len() == before {
            // The shadow suite's E_NOTFOUND.
            return refusal(6);
        }

        refusal(0)
    }

    /// A host with certificate material installed for [`HOSTNAME`].
    /// Answers `getent shadow <login>` the way the tool does.
    fn getent_answer(&self, arguments: &[&str]) -> CommandOutcome {
        if self.getent_status != 0 {
            return refusal(self.getent_status);
        }
        if let Some(line) = &self.garbled_shadow {
            return CommandOutcome {
                status: 0,
                stdout: line.clone(),
                stderr: String::new(),
            };
        }

        let Some(name) = arguments.get(1) else {
            return refusal(GETENT_KEY_NOT_FOUND);
        };
        match self.shadow_field(name) {
            // The remaining seven fields of a shadow entry, present because the
            // reader indexes into the line and must be given a real one.
            Some(field) => CommandOutcome {
                status: 0,
                stdout: format!("{name}:{field}:19000:0:99999:7:::\n"),
                stderr: String::new(),
            },
            None => refusal(GETENT_KEY_NOT_FOUND),
        }
    }

    /// Answers `usermod --lock <login>` the way the tool does.
    fn usermod_answer(&self, arguments: &[&str]) -> CommandOutcome {
        if self.usermod_status != 0 {
            return refusal(self.usermod_status);
        }

        let Some(name) = arguments.get(1) else {
            return refusal(0);
        };
        if self.usermod_locks
            && let Some(field) = self.shadow_field(name)
            && !field.starts_with('!')
        {
            return {
                let mut shadow = self.shadow.lock().unwrap();
                shadow.retain(|(held, _)| held != name);
                shadow.push(((*name).to_owned(), format!("!{field}")));
                refusal(0)
            };
        }

        refusal(0)
    }

    pub(crate) fn with_certificate() -> Self {
        Self {
            certificate_present: true,
            ..Self::new()
        }
    }

    /// The candidate daemon refuses, printing `output`.
    pub(crate) fn refusing_candidates(mut self) -> Self {
        self.candidate_refusal = Some("500 OOPS: bad bool value in config file".to_owned());
        self
    }

    /// The candidate daemon refuses and prints nothing, as the Debian family's
    /// build does for every refusal it has.
    pub(crate) fn refusing_candidates_silently(mut self) -> Self {
        self.candidate_refusal = Some(String::new());
        self
    }

    /// The host already holds `contents` at the live configuration path.
    pub(crate) fn with_live_config(self, contents: &str) -> Self {
        *self.live.lock().unwrap() = Some(contents.to_owned());
        self
    }

    /// A neighbouring operation commits `contents` right after this "host"
    /// swaps a daemon configuration in.
    pub(crate) fn with_a_neighbour_writing_after_the_swap(self, contents: &str) -> Self {
        *self.neighbour_writes_after_the_swap.lock().unwrap() = Some(contents.to_owned());
        self
    }

    /// The daemon comes up but nothing answers on the control port.
    pub(crate) fn silent_on_control_port(mut self) -> Self {
        self.control_port_answers = false;
        self
    }

    /// The control port answers with `line` instead of a `220` greeting.
    pub(crate) fn greeting_of(mut self, line: &str) -> Self {
        self.greeting = line.to_owned();
        self
    }

    /// The daemon is already running.
    pub(crate) fn with_running_daemon(self) -> Self {
        *self.running.lock().unwrap() = true;
        *self.enabled.lock().unwrap() = true;
        self
    }

    /// A listening bind of `[::]:0` is refused with `kind`.
    pub(crate) fn refusing_ipv6_bind(mut self, kind: ErrorKind) -> Self {
        self.ipv6_bind_error = Some(kind);
        self
    }

    /// The service manager refuses to start the unit.
    pub(crate) fn refusing_restart(mut self) -> Self {
        self.restart_succeeds = false;
        self
    }

    /// The live configuration file is there and cannot be read.
    pub(crate) fn unreadable_config(mut self) -> Self {
        self.config_readable = false;
        self
    }

    /// The config-write protocol fails mechanically, before any daemon is asked
    /// anything.
    pub(crate) fn unwritable_config(mut self) -> Self {
        self.config_writable = false;
        self
    }

    /// The service manager cannot be started at all.
    pub(crate) fn without_service_manager(mut self) -> Self {
        self.service_manager_available = false;
        self
    }

    /// No loopback port can be obtained for the candidate check.
    pub(crate) fn without_ephemeral_ports(mut self) -> Self {
        self.ephemeral_port_available = false;
        self
    }

    /// Certificate material is there and cannot be read.
    pub(crate) fn unreadable_certificate(mut self) -> Self {
        self.certificate_present = true;
        self.certificate_readable = false;
        self
    }

    /// The bytes at the live configuration path.
    pub(crate) fn live_config(&self) -> Option<String> {
        self.live.lock().unwrap().clone()
    }

    /// Every configuration ever written, in order.
    pub(crate) fn written_configs(&self) -> Vec<String> {
        self.written.lock().unwrap().clone()
    }

    /// Every program the host was asked to run, in order.
    pub(crate) fn spawns(&self) -> Vec<Spawn> {
        self.spawns.lock().unwrap().clone()
    }

    /// How many times the daemon was restarted.
    pub(crate) fn restart_count(&self) -> usize {
        self.spawns()
            .iter()
            .filter(|spawn| spawn.argv.get(1).is_some_and(|verb| verb == "restart"))
            .count()
    }

    /// Whether the unit is in the boot sequence.
    pub(crate) fn is_enabled(&self) -> bool {
        *self.enabled.lock().unwrap()
    }

    /// Records `argv` and answers the way the service manager would.
    fn service_manager_answer(&self, program: &str, arguments: &[&str]) -> CommandOutcome {
        self.record(program, arguments, None);

        // A mount unit is answered on its own terms: the FTPS daemon's unit and
        // an account's `.mount` unit go to the same program, and conflating them
        // would let a jail teardown report the daemon's state.
        if let Some(unit) = arguments
            .iter()
            .find(|argument| argument.ends_with(".mount"))
        {
            return self.mount_unit_answer(arguments.first().copied(), unit);
        }

        match arguments.first().copied() {
            Some("restart") => {
                *self.running.lock().unwrap() = self.restart_succeeds;
                CommandOutcome {
                    status: i32::from(!self.restart_succeeds),
                    stdout: String::new(),
                    stderr: String::new(),
                }
            }
            Some("is-active") => CommandOutcome {
                status: if *self.running.lock().unwrap() {
                    0
                } else {
                    NOT_ACTIVE
                },
                stdout: String::new(),
                stderr: String::new(),
            },
            Some("disable") | Some("stop") => {
                *self.running.lock().unwrap() = false;
                if arguments.first().copied() == Some("disable") {
                    *self.enabled.lock().unwrap() = false;
                }
                CommandOutcome {
                    status: 0,
                    stdout: String::new(),
                    stderr: String::new(),
                }
            }
            _ => CommandOutcome {
                status: 0,
                stdout: String::new(),
                stderr: String::new(),
            },
        }
    }

    /// Answers a service-manager call that names an account's `.mount` unit.
    ///
    /// The bind mount is the one piece of state a jail teardown's correctness
    /// turns on, so the fake models it rather than answering zero: `enable
    /// --now` brings it up, `disable --now` takes it down, and while it is up
    /// the mount point refuses to be removed — which is what `remove_dir`
    /// answers for a directory that is not empty.
    fn mount_unit_answer(&self, verb: Option<&str>, _unit: &str) -> CommandOutcome {
        match verb {
            Some("enable") => {
                *self.mounted.lock().unwrap() = true;
                refusal(0)
            }
            Some("disable") => {
                if self.unit_stop_status == 0 && !self.stuck_mount {
                    *self.mounted.lock().unwrap() = false;
                }
                refusal(self.unit_stop_status)
            }
            _ => refusal(0),
        }
    }
}

/// An outcome carrying `status` and nothing else.
///
/// Named for the case that matters — a tool refusing — though a zero passes
/// through it too: every spawn this fake answers prints nothing, because no
/// operation in the area reads a login tool's output.
fn refusal(status: i32) -> CommandOutcome {
    CommandOutcome {
        status,
        stdout: String::new(),
        stderr: String::new(),
    }
}

impl FtpsHost for FakeFtpsHost {
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, FtpsError> {
        if program == distro().useradd_binary() {
            self.record(program, arguments, None);
            return Ok(self.useradd_answer(arguments));
        }
        if program == distro().userdel_binary() {
            self.record(program, arguments, None);
            return Ok(self.userdel_answer(arguments));
        }
        if program == distro().getent_binary() {
            self.record(program, arguments, None);
            return Ok(self.getent_answer(arguments));
        }
        if program == distro().usermod_binary() {
            self.record(program, arguments, None);
            return Ok(self.usermod_answer(arguments));
        }

        if !self.service_manager_available {
            return Err(FtpsError::program_unavailable());
        }
        Ok(self.service_manager_answer(program, arguments))
    }

    fn read_config(&self, target: &Path) -> Result<Option<String>, FtpsError> {
        if !self.config_readable {
            return Err(FtpsError::ConfigUnreadable);
        }

        let target = target.to_string_lossy().into_owned();
        if target.ends_with(".mount") {
            // A jail's unit is a different file from the daemon's configuration,
            // and answering one for the other would let the jail step decide it
            // had nothing to write on the strength of the daemon's config.
            return Ok(self
                .unit_contents
                .lock()
                .unwrap()
                .iter()
                .find(|(path, _)| *path == target)
                .map(|(_, contents)| contents.clone()));
        }

        Ok(self.live.lock().unwrap().clone())
    }

    /// Models the config-write protocol: swap first, then the validator, then the
    /// reload, restoring the previous content if either refuses.
    ///
    /// A faithful model and not a shortcut — the order is what the tests are
    /// about. The real protocol renames before it validates, so a refusal has to
    /// leave the previous bytes back at the target rather than never having left
    /// them there.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), FtpsError> {
        if !self.config_writable {
            return Err(FtpsError::ConfigWrite {
                reason: "the temporary file could not be written".to_owned(),
            });
        }

        let unit = target.to_string_lossy().ends_with(".mount");
        let previous = self.live.lock().unwrap().clone();
        if unit {
            // A jail's mount unit is not the daemon's configuration, and a fake
            // that overwrote one with the other would let a login test pass
            // against a daemon config the login work had silently replaced.
            self.written_units
                .lock()
                .unwrap()
                .push(target.to_string_lossy().into_owned());
            self.unit_files
                .lock()
                .unwrap()
                .push(target.to_string_lossy().into_owned());
            self.unit_contents
                .lock()
                .unwrap()
                .push((target.to_string_lossy().into_owned(), contents.to_owned()));
        } else {
            *self.live.lock().unwrap() = Some(contents.to_owned());
            self.written.lock().unwrap().push(contents.to_owned());

            // The neighbour's write lands AFTER ours, once, so a rollback that
            // reads the live file back sees a configuration that is not the one
            // this operation wrote.
            if let Some(theirs) = self.neighbour_writes_after_the_swap.lock().unwrap().take() {
                *self.live.lock().unwrap() = Some(theirs);
            }
        }

        for (program, arguments) in [
            (validator.program, validator.arguments),
            (reload.program, reload.arguments),
        ] {
            if !self.service_manager_available {
                *self.live.lock().unwrap() = previous;
                return Err(FtpsError::program_unavailable());
            }
            if self.service_manager_answer(program, arguments).status != 0 {
                *self.live.lock().unwrap() = previous;
                return Err(FtpsError::ServiceRefused {
                    unit: AgentPaths::FTPS_UNIT.to_owned(),
                });
            }
        }

        Ok(())
    }

    fn run_candidate(
        &self,
        program: &str,
        _contents: &str,
        arguments: &[&str],
        _deadline: Duration,
    ) -> Result<CandidateOutcome, FtpsError> {
        self.record(program, arguments, None);

        match &self.candidate_refusal {
            Some(output) => Ok(CandidateOutcome::Exited {
                output: output.clone(),
            }),
            None => Ok(CandidateOutcome::StillRunning),
        }
    }

    fn ephemeral_port(&self) -> Result<u16, FtpsError> {
        if self.ephemeral_port_available {
            Ok(50_000)
        } else {
            Err(FtpsError::program_unavailable())
        }
    }

    fn bind_ipv6_listener(&self) -> std::io::Result<()> {
        match self.ipv6_bind_error {
            Some(kind) => Err(std::io::Error::new(kind, "refused by the fake kernel")),
            None => Ok(()),
        }
    }

    fn control_port_greeting(&self, _port: u16) -> Option<String> {
        if *self.running.lock().unwrap() && self.control_port_answers {
            Some(self.greeting.clone())
        } else {
            None
        }
    }

    fn run_with_stdin(
        &self,
        program: &str,
        arguments: &[&str],
        stdin: &str,
    ) -> Result<CommandOutcome, FtpsError> {
        self.record(program, arguments, Some(stdin));

        if self.chpasswd_status == 0
            && let Some((name, _)) = stdin.trim_end_matches('\n').split_once(':')
        {
            // What the tool does, and the whole reason the pre-state is read
            // first: the field is REPLACED, marker and all.
            let mut shadow = self.shadow.lock().unwrap();
            shadow.retain(|(held, _)| held != name);
            shadow.push((name.to_owned(), CHPASSWD_HASH.to_owned()));
        }

        Ok(refusal(self.chpasswd_status))
    }

    fn account_ownership(&self, _account: &AccountName) -> Result<AccountOwnership, FtpsError> {
        *self.ownership_questions.lock().unwrap() += 1;

        let mut answers = self.ownerships.lock().unwrap();
        // The last configured answer repeats, so a test that does not care about
        // the re-read configures one answer and is asked twice.
        let answer = if answers.len() > 1 {
            answers.remove(0)
        } else {
            answers.first().copied().flatten()
        };

        answer.ok_or(FtpsError::AccountMissing)
    }

    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), FtpsError> {
        let path = path.to_string_lossy().into_owned();
        let mut directories = self.directories.lock().unwrap();
        if let Some(existing) = directories
            .iter_mut()
            .find(|(recorded, _)| *recorded == path)
        {
            existing.1 = mode;
        } else {
            directories.push((path, mode));
        }

        Ok(())
    }

    fn account_logins(
        &self,
        _passwd_database: &str,
        account: &AccountName,
        jail_directory: &str,
    ) -> Result<Vec<FtpsUserName>, FtpsError> {
        *self.login_enumerations.lock().unwrap() += 1;

        if !self.passwd_readable {
            return Err(FtpsError::AccountMissing);
        }

        // The same two predicates the real implementation applies, and in the
        // same order: the home must be exactly this account's jail, and the name
        // must decode through the constructor that built it. A fake that
        // answered its own list would let the teardown pass while the real one
        // enumerated the wrong rows.
        let mut logins: Vec<FtpsUserName> = self
            .logins
            .lock()
            .unwrap()
            .iter()
            .filter(|login| login.home == jail_directory)
            .filter_map(|login| FtpsUserName::decode(account, &login.name))
            .collect();
        logins.sort_by(|left, right| left.as_str().cmp(right.as_str()));
        logins.dedup();

        Ok(logins)
    }

    fn path_exists(&self, path: &Path) -> bool {
        let path = path.to_string_lossy().into_owned();

        self.unit_file_exists(&path) || self.directory_still_exists(&path)
    }

    fn remove_file(&self, path: &Path) -> Result<(), FtpsError> {
        let path = path.to_string_lossy().into_owned();
        self.unit_files
            .lock()
            .unwrap()
            .retain(|recorded| *recorded != path);

        Ok(())
    }

    fn remove_directory(&self, path: &Path) -> Result<(), FtpsError> {
        let path = path.to_string_lossy().into_owned();

        // `remove_dir`, modelled rather than assumed away. It refuses a
        // directory that is not empty, and the two ways one of these is not
        // empty are the ones that matter: a live bind mount over the mount
        // point, and the mount point still sitting inside the jail.
        if path == self.jail().mount_point() && *self.mounted.lock().unwrap() {
            return Err(FtpsError::JailFailed);
        }
        let prefix = format!("{path}/");
        if self
            .directories
            .lock()
            .unwrap()
            .iter()
            .any(|(recorded, _)| recorded.starts_with(&prefix))
        {
            return Err(FtpsError::JailFailed);
        }

        self.directories
            .lock()
            .unwrap()
            .retain(|(recorded, _)| *recorded != path);

        Ok(())
    }

    fn certificate_state(&self, domain: &Domain) -> Result<CertificateState, FtpsError> {
        if !self.certificate_readable {
            return Err(FtpsError::ConfigUnreadable);
        }

        let certificate = SiteCertificate::for_domain(domain);
        Ok(CertificateState {
            certificate_path: certificate
                .certificate_path()
                .to_string_lossy()
                .into_owned(),
            private_key_path: certificate.key_path().to_string_lossy().into_owned(),
            present: self.certificate_present,
            is_self_signed_placeholder: false,
        })
    }
}

/// The Debian family's real adapter, which is what these operations are given.
///
/// The real one and not a fake: every platform fact this area reads — the
/// service manager's path, the vsftpd binary, the three TLS option spellings —
/// is a value the adapter already pins with its own tests, and a fake adapter
/// here would let this area pass against spellings no host has.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The configuration every enable test applies.
pub(crate) fn configuration() -> FtpsConfiguration {
    FtpsConfiguration {
        hostname: Domain::parse(HOSTNAME).unwrap(),
        passive_port_min: Port::parse(49_152).unwrap(),
        passive_port_max: Port::parse(50_192).unwrap(),
        passive_address: None,
        max_clients: 50,
    }
}

/// The request every login test creates: `<account>_<name>`, with `password`.
pub(crate) fn request(account: &str, name: &str, password: &str) -> FtpsUserRequest {
    let account = AccountName::parse(account).unwrap();

    FtpsUserRequest {
        user: FtpsUserName::for_account(&account, name).unwrap(),
        password: Password::parse(password).unwrap(),
        account,
    }
}

/// A validated account name.
pub(crate) fn account(name: &str) -> AccountName {
    AccountName::parse(name).unwrap()
}

/// The same configuration, advertising `address` in its `PASV` reply.
pub(crate) fn configuration_behind_nat(address: &str) -> FtpsConfiguration {
    FtpsConfiguration {
        passive_address: Some(PassiveAddress::parse(address).unwrap()),
        ..configuration()
    }
}
