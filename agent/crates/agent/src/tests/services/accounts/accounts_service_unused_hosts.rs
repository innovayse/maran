//! The hosts the accounts service holds for the deletion cascade.
//!
//! Deletion is not driven by these tests, so every host here says it is
//! unreachable.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};
use std::time::Duration;

use super::blocking_host::{FAST_ACCOUNT, SLOW_ACCOUNT};
use super::standard_passwd;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::system_account::SystemAccount;
use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::AdapterBinary;
use maran_ops::accounts::CommandOutcome;
use maran_ops::cron::model::cron_run_record::CronRunRecord;
use maran_ops::cron::{CronEnvironment, CronError, CronHost, CronRun};
use maran_ops::db::{DbError, DbHost};
use maran_ops::ftps::{CandidateOutcome, FtpsError, FtpsHost};
use maran_ops::logins::{LoginsError, LoginsHost};
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{CommandOutcome as SafeWriteOutcome, ConfigHost, SafeWriteError};
use maran_ops::sftp::{AccountOwnership, SftpError, SftpHost};
use maran_ops::sites::{SiteHost, SitesOpError};
use maran_ops::ssl::CertificateState;

/// The php host the service holds for the deletion cascade.
///
/// Deletion is not driven here, so every method says it is unreachable rather
/// than pretending to work — a fake that answered would invite a later test to
/// depend on an answer nobody designed.
pub(crate) struct UnusedPhpHost;

impl ConfigHost for UnusedPhpHost {
    /// Unreachable: no rpc driven here writes a pool.
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<SafeWriteOutcome, SafeWriteError> {
        unreachable!("no rpc in this file reaches the php host")
    }
}

impl PhpHost for UnusedPhpHost {
    /// Unreachable: see [`UnusedPhpHost`].
    fn directory_exists(&self, _path: &Path) -> bool {
        unreachable!("no rpc in this file reaches the php host")
    }

    /// Unreachable: see [`UnusedPhpHost`].
    fn read_config(&self, _path: &Path) -> Option<String> {
        unreachable!("no rpc in this file reaches the php host")
    }

    /// Unreachable: see [`UnusedPhpHost`].
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        unreachable!("no rpc in this file reaches the php host")
    }

    /// Unreachable: see [`UnusedPhpHost`].
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        unreachable!("no rpc in this file reaches the php host")
    }

    /// Unreachable: see [`UnusedPhpHost`].
    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        unreachable!("no rpc in this file reaches the php host")
    }

    /// Unreachable: see [`UnusedPhpHost`].
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        unreachable!("no rpc in this file reaches the php host")
    }

    /// Unreachable: see [`UnusedPhpHost`]. The startup pool-tree pass calls this,
    /// and no rpc does.
    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        unreachable!("no rpc in this file reaches the php host")
    }
}

/// The database host the service holds for the deletion cascade.
pub(crate) struct UnusedDbHost;

impl DbHost for UnusedDbHost {
    /// Unreachable: no rpc driven here drops a database.
    fn execute(&self, _statement: &str) -> Result<String, DbError> {
        unreachable!("no rpc in this file reaches the database host")
    }
}

/// The SFTP host the service holds for the deletion cascade and the suspension
/// state.
pub(crate) struct UnusedSftpHost;

impl SftpHost for UnusedSftpHost {
    /// Unreachable: no rpc driven here touches a login.
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _stdin: Option<&str>,
        _class: CallClass,
    ) -> Result<CommandOutcome, SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn account_ownership(&self, _account: &AccountName) -> Result<AccountOwnership, SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn managed_account(
        &self,
        _passwd_database: &str,
        _account: &AccountName,
    ) -> Result<ManagedAccount, SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// An account holding no SFTP login, read out of a password database that
    /// could be read.
    ///
    /// Not `unreachable!` like its neighbours: the suspension state enumerates
    /// the account's logins, and what this file pins is the mapping of an
    /// answer onto the wire, not how the answer is computed —
    /// `inspect_account_logins` has its own tests for that.
    fn account_logins(
        &self,
        _passwd_database: &str,
        _account: &AccountName,
        _jail_directory: &str,
    ) -> Result<Vec<SftpUserName>, SftpError> {
        Ok(Vec::new())
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn path_exists(&self, _path: &Path) -> bool {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn remove_file(&self, _path: &Path) -> Result<(), SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn remove_directory(&self, _path: &Path) -> Result<(), SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    fn write_key_file(
        &self,
        _file: &maran_ops::safe_write::model::ConfigFile<'_>,
        _validator: &maran_ops::safe_write::model::Validator<'_>,
    ) -> Result<(), SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn login_names(&self, _passwd_database: &str) -> Result<Vec<String>, SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn key_file_names(&self) -> Result<Vec<String>, SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }
}

/// The FTPS host the service holds for the deletion cascade.
///
/// Every method is `unreachable!`: no rpc driven in this file deletes an
/// account, which is the only operation that reaches this host. The service
/// holds it so that a deletion can take the account's FTPS logins, jail and
/// bind mount with it, and that path has its own tests in `ops`.
pub(crate) struct UnusedFtpsHost;

impl FtpsHost for UnusedFtpsHost {
    /// Unreachable: see [`UnusedFtpsHost`].
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn read_config(&self, _target: &Path) -> Result<Option<String>, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn run_candidate(
        &self,
        _program: &AdapterBinary,
        _contents: &str,
        _arguments: &[&str],
        _deadline: Duration,
    ) -> Result<CandidateOutcome, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn ephemeral_port(&self) -> Result<u16, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn bind_ipv6_listener(&self) -> std::io::Result<()> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn control_port_greeting(&self, _port: u16) -> Option<String> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn pause(&self, _duration: Duration) {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn monotonic_now(&self) -> Duration {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn certificate_state(&self, _domain: &Domain) -> Result<CertificateState, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn run_with_stdin(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _stdin: &str,
        _class: CallClass,
    ) -> Result<CommandOutcome, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn account_ownership(&self, _account: &AccountName) -> Result<AccountOwnership, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn managed_account(
        &self,
        _passwd_database: &str,
        _account: &AccountName,
    ) -> Result<ManagedAccount, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn account_logins(
        &self,
        _passwd_database: &str,
        _account: &AccountName,
        _jail_directory: &str,
    ) -> Result<Vec<FtpsUserName>, FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn path_exists(&self, _path: &Path) -> bool {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn remove_file(&self, _path: &Path) -> Result<(), FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }

    /// Unreachable: see [`UnusedFtpsHost`].
    fn remove_directory(&self, _path: &Path) -> Result<(), FtpsError> {
        unreachable!("no rpc in this file reaches the ftps host")
    }
}

/// The login host the service holds for the suspension state.
///
/// It answers with a password database holding the accounts this file drives
/// and no file-transfer login at all — an OBSERVED nothing, not a refusal to
/// look. Its `read_passwd` is not `unreachable!` like its neighbour's methods:
/// the suspension state enumerates the account's logins, and what this file
/// pins is the mapping of an answer onto the wire, not how the answer is
/// computed — `account_logins` has its own tests for that.
pub(crate) struct UnusedLoginsHost;

impl LoginsHost for UnusedLoginsHost {
    /// The two accounts this file drives, each with a home under `/home` and
    /// no login of either protocol beside it.
    fn read_passwd(&self, _passwd_database: &str) -> Result<Vec<SystemAccount>, LoginsError> {
        Ok([SLOW_ACCOUNT, FAST_ACCOUNT]
            .into_iter()
            .enumerate()
            .map(|(index, name)| {
                let id = 1001 + u32::try_from(index).unwrap_or_default();
                SystemAccount {
                    name: name.to_owned(),
                    uid: id,
                    gid: id,
                    home: format!("/home/{name}"),
                }
            })
            .collect())
    }

    /// Unreachable: an account with no login is asked about none.
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, LoginsError> {
        unreachable!("no login exists in this file, so none is asked about")
    }

    fn rename_if_present(
        &self,
        _from: &std::path::Path,
        _to: &std::path::Path,
    ) -> Result<(), LoginsError> {
        unreachable!("no login exists in this file, so no key file is moved")
    }
}

/// The site host the service holds for the suspension state.
///
/// It serves NOTHING and says so readably, which is the one shape this file
/// needs: what is under test here is the mapping of an answer onto the wire,
/// not how the answer is computed — `inspect_account_sites` has its own tests
/// for that against a fake holding real vhost text.
pub(crate) struct UnusedSiteHost;

impl SiteHost for UnusedSiteHost {
    /// An empty vhost directory that could be read.
    fn list_config_paths(&self) -> Result<Vec<PathBuf>, SitesOpError> {
        Ok(Vec::new())
    }

    /// Unreachable: see [`UnusedSiteHost`].
    fn read_config(&self, _path: &Path) -> Result<Option<String>, SitesOpError> {
        unreachable!("no rpc in this file reaches the site host")
    }

    /// Unreachable: see [`UnusedSiteHost`].
    fn create_site_log_directory(&self, _account: &AccountName) -> Result<(), SitesOpError> {
        unreachable!("no rpc in this file reaches the site host")
    }

    /// Unreachable: see [`UnusedSiteHost`].
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
    ) -> Result<(), SitesOpError> {
        unreachable!("no rpc in this file reaches the site host")
    }

    /// Unreachable: see [`UnusedSiteHost`].
    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        unreachable!("no rpc in this file reaches the site host")
    }

    /// Unreachable: see [`UnusedSiteHost`].
    fn create_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
        _refused_warnings: &[String],
    ) -> Result<(), SitesOpError> {
        unreachable!("no rpc in this file reaches the site host")
    }

    /// Unreachable: see [`UnusedSiteHost`].
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        unreachable!("no rpc in this file reaches the site host")
    }

    /// Unreachable: see [`UnusedSiteHost`].
    fn resolve_in_account_home(
        &self,
        _account: &AccountName,
        _relative: &Path,
    ) -> Result<PathBuf, SitesOpError> {
        unreachable!("no rpc in this file reaches the site host")
    }
}

/// The cron host the service holds for the suspension state.
///
/// An account with no crontab at all, which is the answer `crontab -l` gives
/// for every account before the panel installs anything. It is a real state and
/// not a stand-in for a failure: a crontab that cannot be READ is an error on
/// the rpc, and `inspect_account_cron` has its own tests for both.
pub(crate) struct UnusedCronHost;

impl CronHost for UnusedCronHost {
    /// Resolves the account against the two hosting accounts this file drives,
    /// as the real host resolves it against the password database.
    fn managed_account(&self, account: &AccountName) -> Result<ManagedAccount, CronError> {
        ManagedAccount::resolve(account.clone(), &standard_passwd()).map_err(CronError::Account)
    }

    /// No crontab at all.
    fn read_crontab(&self, _account: &ManagedAccount) -> Result<Option<String>, CronError> {
        Ok(None)
    }

    /// Unreachable: the suspension state never installs a table.
    fn install_crontab(&self, _account: &ManagedAccount, _contents: &str) -> Result<(), CronError> {
        unreachable!("no rpc in this file installs a crontab")
    }

    /// Unreachable: see [`UnusedCronHost`].
    fn new_entry_id(&self) -> Result<CronEntryId, CronError> {
        unreachable!("no rpc in this file mints an entry id")
    }

    /// Unreachable: see [`UnusedCronHost`].
    fn write_command_file(
        &self,
        _account: &ManagedAccount,
        _entry: &CronEntryId,
        _command: &CronCommand,
    ) -> Result<(), CronError> {
        unreachable!("no rpc in this file writes a command file")
    }

    /// Unreachable: see [`UnusedCronHost`].
    fn read_command_file(
        &self,
        _account: &ManagedAccount,
        _entry: &CronEntryId,
    ) -> Result<Option<String>, CronError> {
        unreachable!("no rpc in this file reads a command file")
    }

    /// Unreachable: see [`UnusedCronHost`].
    fn remove_entry_files(
        &self,
        _account: &ManagedAccount,
        _entry: &CronEntryId,
    ) -> Result<(), CronError> {
        unreachable!("no rpc in this file removes entry files")
    }

    /// Unreachable: see [`UnusedCronHost`].
    fn read_run_record(
        &self,
        _account: &ManagedAccount,
        _entry: &CronEntryId,
    ) -> Result<Option<CronRunRecord>, CronError> {
        unreachable!("no rpc in this file reads a run record")
    }

    /// Unreachable: see [`UnusedCronHost`].
    fn read_output_tail(
        &self,
        _account: &ManagedAccount,
        _entry: &CronEntryId,
        _max_bytes: usize,
    ) -> Result<Option<String>, CronError> {
        unreachable!("no rpc in this file reads an output tail")
    }

    /// Unreachable: see [`UnusedCronHost`].
    fn run_entry(
        &self,
        _account: &ManagedAccount,
        _entry: &CronEntryId,
        _environment: &[CronEnvironment],
    ) -> Result<CronRun, CronError> {
        unreachable!("no rpc in this file runs an entry")
    }
}
