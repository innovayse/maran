//! The [`SftpHost`] half of [`FakeSftpHost`]: how the fake answers each host
//! method.
//!
//! A child of `fake_sftp_host.rs` so it reads the fake's private state without
//! widening a single field; the state, its builders and its observers stay in
//! the parent.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::AdapterBinary;

use super::{FakeSftpHost, NAME_IN_USE, NO_SUCH_USER, PasswdEntry, RecordedConfig, RecordedSpawn};
use crate::safe_write::model::{ConfigFile, Reload, Validator};
use crate::sftp::model::account_ownership::AccountOwnership;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

impl SftpHost for FakeSftpHost {
    /// Records the spawn, then answers as the tool would.
    ///
    /// A program the fake does not know panics rather than answering blandly: a
    /// fake that shrugs at an unexpected tool is a fake that lets an operation
    /// run anything and still pass.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        stdin: Option<&str>,
        class: CallClass,
    ) -> Result<CommandOutcome, SftpError> {
        let program = program.as_str();
        self.spawns.lock().unwrap().push(RecordedSpawn {
            argv: Self::command_of(program, arguments),
            stdin: stdin.unwrap_or_default().to_owned(),
            class,
        });

        let timed_out = self.times_out.lock().unwrap().clone();
        if timed_out.is_some_and(|name| program.ends_with(name.as_str())) {
            return Err(SftpError::Bound(BoundExceeded::TimedOut {
                program: program.to_owned(),
                deadline: class.deadline(),
            }));
        }

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
        } else if program.ends_with("pkill") {
            let (status, stdout) = self.pkill_answer.lock().unwrap().clone();
            return Ok(CommandOutcome {
                status,
                stdout,
                stderr: String::new(),
            });
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

    /// Decides through the shipped [`ManagedAccount`] rule over a one-row
    /// password text the fake composes from the ids and home it holds, so the
    /// fake never has an opinion of its own about who counts as managed.
    fn managed_account(
        &self,
        passwd_database: &str,
        account: &AccountName,
    ) -> Result<ManagedAccount, SftpError> {
        assert!(
            passwd_database.starts_with('/'),
            "the password database must be an absolute path from the adapter: {passwd_database}"
        );

        if !*self.account_exists.lock().unwrap() {
            return Err(SftpError::AccountMissing);
        }

        let (uid, gid) = *self.account_ids.lock().unwrap();
        let home = self
            .account_home
            .lock()
            .unwrap()
            .clone()
            .unwrap_or_else(|| format!("/home/{}", account.as_str()));
        let passwd = format!("{}:x:{uid}:{gid}::{home}:/bin/sh\n", account.as_str());

        ManagedAccount::resolve(account.clone(), &passwd).map_err(SftpError::from_managed_account)
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
        if *self.config_times_out.lock().unwrap() {
            return Err(SftpError::Bound(BoundExceeded::TimedOut {
                program: validator.program.as_str().to_owned(),
                deadline: validator.class.deadline(),
            }));
        }
        if *self.config_refuses.lock().unwrap() {
            return Err(SftpError::JailFailed);
        }

        self.configs.lock().unwrap().push(RecordedConfig {
            target: target.display().to_string(),
            contents: contents.to_owned(),
            validator: Self::command_of(validator.program.as_str(), validator.arguments),
            validator_class: validator.class,
            reload: Self::command_of(reload.program.as_str(), reload.arguments),
        });

        Ok(())
    }

    /// Every name in the fake's password database, or a refusal when a test
    /// made it unreadable.
    fn login_names(&self, _passwd_database: &str) -> Result<Vec<String>, SftpError> {
        if *self.passwd_unreadable.lock().unwrap() {
            return Err(SftpError::PasswdUnreadable);
        }

        Ok(self.users())
    }

    /// The names of the held paths that sit directly in the key directory.
    fn key_file_names(&self) -> Result<Vec<String>, SftpError> {
        let prefix = format!("{}/", AgentPaths::SFTP_AUTHORIZED_KEYS_DIRECTORY);

        Ok(self
            .paths()
            .into_iter()
            .filter_map(|path| path.strip_prefix(&prefix).map(str::to_owned))
            .collect())
    }

    /// Records the key file as a config write with no reload, and puts it on
    /// the "host" so `path_exists` and a removal see it.
    fn write_key_file(
        &self,
        file: &ConfigFile<'_>,
        validator: &Validator<'_>,
    ) -> Result<(), SftpError> {
        if *self.config_refuses.lock().unwrap() {
            return Err(SftpError::KeyFileFailed);
        }

        let target = file.target.display().to_string();
        self.configs.lock().unwrap().push(RecordedConfig {
            target: target.clone(),
            contents: file.contents.to_owned(),
            validator: Self::command_of(validator.program.as_str(), validator.arguments),
            validator_class: validator.class,
            reload: Vec::new(),
        });
        let mut paths = self.paths.lock().unwrap();
        paths.retain(|held| *held != target);
        paths.push(target);

        Ok(())
    }
}
