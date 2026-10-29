//! Whether an account rpc holds the runtime's workers while it waits.
//!
//! The account operations spawn `useradd`, `chgrp`, `chmod`, `setquota`, `id`
//! and `quota` and wait on each. This service awaited them on the async
//! worker itself — no `spawn_blocking` anywhere in the file — so for as long
//! as one account creation was inside `useradd`, every other in-flight
//! command on that worker was stopped, with no symptom but an unrelated
//! timeout under load.
//!
//! Nothing in the existing suite could see that: every account test drives
//! `ops::accounts` synchronously with a fake that returns at once, and a fake
//! that never blocks stalls nothing. The test below therefore supplies a fake
//! that DOES block, on a runtime with exactly one worker, and asks the only
//! question that distinguishes the two arrangements: while one rpc is inside
//! its spawn, does a second rpc still answer? On the blocking pool it answers
//! in milliseconds; on the worker it cannot answer until the first spawn
//! returns.
//!
//! One worker rather than the default count deliberately. With several
//! workers a stalled one is hidden by its neighbours, which is exactly why
//! the defect survived so long in production shape.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::system_account::SystemAccount;
use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};
use maran_ops::accounts::{AccountError, AccountOperations, CommandOutcome, SystemHost};
use maran_ops::cron::model::cron_run_record::CronRunRecord;
use maran_ops::cron::{CronError, CronHost};
use maran_ops::db::{DbError, DbHost};
use maran_ops::ftps::{CandidateOutcome, FtpsError, FtpsHost};
use maran_ops::logins::{LoginsError, LoginsHost};
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{CommandOutcome as SafeWriteOutcome, ConfigHost, SafeWriteError};
use maran_ops::sftp::{AccountOwnership, SftpError, SftpHost};
use maran_ops::sites::{SiteHost, SitesOpError};
use maran_ops::ssl::CertificateState;
use tonic::Request;

use crate::proto::accounts_service_server::AccountsService;
use crate::proto::{
    AgentError, CreateAccountRequest, DeleteAccountRequest, ErrorCode,
    GetAccountSuspensionStateRequest, GetAccountUsageRequest, LoginPasswordState,
    SetAccountQuotaRequest, SuspendAccountRequest, UnsuspendAccountRequest,
    create_account_response, delete_account_response, get_account_suspension_state_response,
    get_account_usage_response, set_account_quota_response, suspend_account_response,
    unsuspend_account_response,
};
use crate::services::accounts::accounts_service::AccountsServiceImpl;

/// The account whose creation blocks inside the fake's spawn.
const SLOW_ACCOUNT: &str = "slowacct";

/// The account the second, concurrent rpc asks about.
const FAST_ACCOUNT: &str = "fastacct";

/// How long the slow account's spawn blocks.
///
/// Long enough that a second rpc stalled behind it cannot be mistaken for a
/// slow one, and short enough to keep the suite quick. The assertion's budget
/// below is a small fraction of it, so the two verdicts are separated by most
/// of a second in either direction rather than by a margin a loaded CI box
/// could close.
const BLOCKING_SPAWN: Duration = Duration::from_millis(1500);

/// How long the second rpc may take, measured from the moment the first one
/// entered its blocking spawn.
const SECOND_RPC_BUDGET: Duration = Duration::from_millis(500);

/// The uid the fake's `id -u` reports, as the digits the operation parses.
const FAKE_UID_OUTPUT: &str = "1000";

/// A system host that blocks for [`BLOCKING_SPAWN`] on one account and answers
/// at once for every other.
///
/// It stands in for the spawn itself: `useradd` on a real host takes long
/// enough to matter, and the only property under test is what the runtime is
/// doing while a spawn has not returned. The instant the block BEGAN is
/// recorded, because it is the only clock reading this test can trust — a
/// reading taken by the test's own future would itself be delayed by the
/// stall it is trying to measure.
struct BlockingSystemHost {
    /// When the blocking call began, set once by whichever thread runs it.
    blocking_began: Arc<Mutex<Option<Instant>>>,
    /// The local password database the host answers, which is what every
    /// operation on an existing account resolves its managed account from.
    passwd: String,
    /// How many times the host was asked to spawn a program or measure a tree:
    /// the two ways an operation reaches the machine as root. Zero is the
    /// proof that a refused request ran nothing.
    effects: Arc<Mutex<usize>>,
}

impl BlockingSystemHost {
    /// Counts one reach into the machine.
    fn touch(&self) {
        *self
            .effects
            .lock()
            .expect("the effect counter is not held across a panic") += 1;
    }
}

impl SystemHost for BlockingSystemHost {
    /// Answers every program the operations run: success, with the digits the
    /// uid lookup parses. What each program did is not what this file tests.
    ///
    /// The two exceptions are the programs whose OUTPUT is read rather than
    /// merely run, both of them for the suspension state: `passwd -S`, whose
    /// first line carries the locked flag, and `getent shadow`, whose password
    /// field carries what is actually behind that flag. They answer as an
    /// ordinary hosting account does — locked, with no password hash at all —
    /// which is the pair `passwd -S` alone cannot express. Anything else would
    /// make that rpc fail here for a reason that has nothing to do with what
    /// this file tests.
    fn run(
        &self,
        _program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, AccountError> {
        self.touch();
        let stdout = match arguments {
            ["-S", username] => format!("{username} L 2026-01-01 0 99999 7 -1\n"),
            ["--", "shadow", username] => format!("{username}:!:20704:0:99999:7:::\n"),
            // `quotaon -p <mountpoint>`, for the usage rpc's own enforceability
            // check: answered as an enforceable filesystem, since what this
            // file tests is scheduling, not quota classification.
            ["-p", "--", _] => "Quota for users are enabled on mountpoint /home\n".to_owned(),
            _ => FAKE_UID_OUTPUT.to_owned(),
        };

        Ok(CommandOutcome {
            status: 0,
            stdout,
            stderr: String::new(),
        })
    }

    /// Reports the slow account absent — and takes [`BLOCKING_SPAWN`] to say
    /// so — and every other account present.
    ///
    /// Absent is what lets the creation proceed past its idempotence check;
    /// present is what lets the usage reading proceed past its own.
    fn user_exists(&self, username: &str) -> Result<bool, AccountError> {
        if username != SLOW_ACCOUNT {
            return Ok(true);
        }

        *self
            .blocking_began
            .lock()
            .expect("the recorded instant is not held across a panic") = Some(Instant::now());
        thread::sleep(BLOCKING_SPAWN);

        Ok(false)
    }

    /// An empty home; the usage rpc reads the number but asserts nothing on it.
    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        self.touch();
        Ok(0)
    }

    /// The configured password database: the two hosting accounts this file
    /// drives by default, or whatever a refusal test installed.
    fn read_password_database(&self, _path: &str) -> Result<String, AccountError> {
        Ok(self.passwd.clone())
    }

    /// A filesystem mounted with quota accounting — the usage rpc's own
    /// enforceability check reads this, and this file tests scheduling, not
    /// quota classification.
    fn read_mounts(&self) -> Result<String, AccountError> {
        Ok("/dev/sda1 /home ext4 rw,relatime,usrquota 0 0\n".to_owned())
    }

    /// Unreachable: no rpc driven in this file repairs a home's group.
    fn home_metadata(
        &self,
        _path: &str,
    ) -> Result<Option<maran_ops::accounts::model::home_metadata::HomeMetadata>, AccountError> {
        unreachable!("no rpc in this file inspects a home's metadata")
    }

    /// Returns at once: virtual time, and no rpc driven here deletes an account.
    fn pause(&self, _interval: Duration) {}
}

/// The php host the service holds for the deletion cascade.
///
/// Deletion is not driven here, so every method says it is unreachable rather
/// than pretending to work — a fake that answered would invite a later test to
/// depend on an answer nobody designed.
struct UnusedPhpHost;

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
struct UnusedDbHost;

impl DbHost for UnusedDbHost {
    /// Unreachable: no rpc driven here drops a database.
    fn execute(&self, _statement: &str) -> Result<String, DbError> {
        unreachable!("no rpc in this file reaches the database host")
    }
}

/// The SFTP host the service holds for the deletion cascade and the suspension
/// state.
struct UnusedSftpHost;

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
}

/// The FTPS host the service holds for the deletion cascade.
///
/// Every method is `unreachable!`: no rpc driven in this file deletes an
/// account, which is the only operation that reaches this host. The service
/// holds it so that a deletion can take the account's FTPS logins, jail and
/// bind mount with it, and that path has its own tests in `ops`.
struct UnusedFtpsHost;

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
struct UnusedLoginsHost;

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
}

/// The site host the service holds for the suspension state.
///
/// It serves NOTHING and says so readably, which is the one shape this file
/// needs: what is under test here is the mapping of an answer onto the wire,
/// not how the answer is computed — `inspect_account_sites` has its own tests
/// for that against a fake holding real vhost text.
struct UnusedSiteHost;

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
struct UnusedCronHost;

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
}

/// The service under test, and the cell its blocking host records into.
type ServiceUnderTest = AccountsServiceImpl<
    BlockingSystemHost,
    UnusedPhpHost,
    UnusedDbHost,
    UnusedSftpHost,
    UnusedFtpsHost,
    UnusedSiteHost,
    UnusedCronHost,
    UnusedLoginsHost,
>;

/// The service under test with the two cells its host records into: when the
/// blocking call began, and how many state-changing calls reached the host.
type ServiceAndCells = (
    Arc<ServiceUnderTest>,
    Arc<Mutex<Option<Instant>>>,
    Arc<Mutex<usize>>,
);

/// The password database of the two hosting accounts this file drives: ids
/// above any floor, each home exactly under `/home`.
fn standard_passwd() -> String {
    format!(
        "{SLOW_ACCOUNT}:x:1001:1001::/home/{SLOW_ACCOUNT}:/usr/sbin/nologin\n\
         {FAST_ACCOUNT}:x:1002:1002::/home/{FAST_ACCOUNT}:/usr/sbin/nologin\n"
    )
}

/// Builds the service the way `server.rs` builds it, over a host answering
/// `passwd`, and returns the cells the host records into.
fn service_over(passwd: String) -> ServiceAndCells {
    let blocking_began = Arc::new(Mutex::new(None));
    let effects = Arc::new(Mutex::new(0));
    let host = BlockingSystemHost {
        blocking_began: Arc::clone(&blocking_began),
        passwd,
        effects: Arc::clone(&effects),
    };
    let distro = adapter_for(DistroFamily::Debian);

    (
        Arc::new(AccountsServiceImpl::new(
            AccountOperations::new(host, distro),
            UnusedPhpHost,
            UnusedDbHost,
            UnusedSftpHost,
            UnusedFtpsHost,
            UnusedSiteHost,
            UnusedCronHost,
            UnusedLoginsHost,
        )),
        blocking_began,
        effects,
    )
}

/// Builds the service over the two ordinary hosting accounts.
fn service() -> (Arc<ServiceUnderTest>, Arc<Mutex<Option<Instant>>>) {
    let (service, blocking_began, _) = service_over(standard_passwd());

    (service, blocking_began)
}

/// The moment the blocking host entered its spawn, waited for by yielding
/// rather than by sleeping.
///
/// Yielding is what makes this honest on the broken arrangement too: the
/// spawned rpc and this loop share the one worker, so the loop gives the
/// worker up and the rpc gets it. Whether the loop then resumes in
/// milliseconds or only after the block has finished is the whole question,
/// and it is answered by the recorded instant rather than by this loop.
async fn blocking_start(cell: &Arc<Mutex<Option<Instant>>>) -> Instant {
    loop {
        if let Some(began) = *cell.lock().expect("the recorded instant is readable") {
            return began;
        }
        tokio::task::yield_now().await;
    }
}

/// A second rpc must answer while the first is still inside its spawn.
///
/// This is the regression test for the accounts service awaiting its process
/// spawns on the runtime worker. With one worker and no `spawn_blocking`, the
/// usage rpc cannot be polled until the creation's `useradd` has returned, so
/// it answers [`BLOCKING_SPAWN`] late; with the spawn on the blocking pool it
/// answers at once. The assertion is on the elapsed time measured FROM the
/// blocking host's own start instant, which is the only reading a stalled
/// worker cannot distort.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn a_second_rpc_answers_while_an_account_creation_is_inside_its_spawn() {
    let (service, blocking_began) = service();

    let creating = tokio::spawn({
        let service = Arc::clone(&service);
        async move {
            service
                .create_account(Request::new(CreateAccountRequest {
                    username: SLOW_ACCOUNT.to_owned(),
                    quota_bytes: 0,
                }))
                .await
        }
    });

    let began = blocking_start(&blocking_began).await;

    // Spawned rather than awaited here. A `#[tokio::test(flavor =
    // "multi_thread")]` body is driven by `block_on` on the harness's own
    // thread, which is NOT one of the worker threads: awaiting the second rpc
    // inline would run it on a thread the first rpc could never have stalled,
    // and the test would pass on the defect it exists to catch. Both rpcs are
    // therefore tasks, and the single worker they share is the thing under
    // test.
    let usage = tokio::spawn({
        let service = Arc::clone(&service);
        async move {
            service
                .get_account_usage(Request::new(GetAccountUsageRequest {
                    username: FAST_ACCOUNT.to_owned(),
                }))
                .await
        }
    })
    .await
    .unwrap()
    .unwrap();
    let answered_after = began.elapsed();

    assert!(
        matches!(
            usage.into_inner().result,
            Some(get_account_usage_response::Result::Ok(_))
        ),
        "the second rpc must answer, and answer successfully"
    );
    assert!(
        answered_after < SECOND_RPC_BUDGET,
        "the second rpc answered {answered_after:?} after the first entered a {BLOCKING_SPAWN:?} \
         spawn, so it was queued behind it: the operations are being awaited on the runtime \
         worker instead of the blocking pool"
    );

    let created = creating.await.unwrap().unwrap();
    assert!(
        matches!(
            created.into_inner().result,
            Some(create_account_response::Result::Ok(_))
        ),
        "the first rpc must still complete normally"
    );
}

/// The suspension state reaches the wire as an answer, not as a transport
/// status, and each of its three parts lands in its own field.
///
/// The field mapping is what this pins. Two booleans side by side in one
/// message is exactly the shape that survives being swapped: a suspension
/// reported over an unreadable directory, or an unreadable directory reported
/// as a locked login, would both compile and both read as success.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn the_suspension_state_reaches_the_wire_as_an_answer_field_by_field() {
    let (service, _) = service();

    let response = service
        .get_account_suspension_state(Request::new(GetAccountSuspensionStateRequest {
            username: FAST_ACCOUNT.to_owned(),
        }))
        .await
        .expect("the rpc answers")
        .into_inner();

    let Some(get_account_suspension_state_response::Result::Ok(state)) = response.result else {
        panic!("a readable host answers with an ok, not with an error: {response:?}");
    };

    assert!(state.login_locked, "`passwd -S` reported the login as `L`");
    assert_eq!(
        state.login_password_state,
        LoginPasswordState::Absent as i32,
        "the shadow field held `!`: locked to `passwd -S`, and no password to unlock",
    );
    assert!(
        state.sites_directory_readable,
        "the directory was listed; an empty list must not be reported as a blind one",
    );
    assert!(state.sites.is_empty());

    // The cron and SFTP halves land in their own fields too. Both are the
    // "nothing there" answer, and both are reported as an OBSERVED nothing:
    // the host answered, it was not that nobody asked.
    assert_eq!(state.cron_entries_total, 0);
    assert_eq!(state.cron_entries_suspended, 0);
    assert_eq!(state.cron_foreign_lines, 0);
    assert!(state.sftp_logins.is_empty());
}

/// An invalid username is refused by the agent's own revalidation, in the
/// payload rather than as a transport error.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn a_suspension_state_asked_for_an_impossible_username_is_refused_in_the_payload() {
    let (service, _) = service();

    let response = service
        .get_account_suspension_state(Request::new(GetAccountSuspensionStateRequest {
            username: "../../etc".to_owned(),
        }))
        .await
        .expect("the rpc answers")
        .into_inner();

    let Some(get_account_suspension_state_response::Result::Error(error)) = response.result else {
        panic!("an impossible name is refused: {response:?}");
    };

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

/// The refusal inside an rpc's `oneof`, when it answered with one.
fn refusal_of<T>(
    result: Option<T>,
    pick: impl FnOnce(T) -> Option<AgentError>,
) -> Option<AgentError> {
    result.and_then(pick)
}

/// Sends every rpc that acts on an account to `service`, naming `username`,
/// and returns each rpc's refusal (`None` when it answered with an ok).
async fn every_rpc_for(
    service: &Arc<ServiceUnderTest>,
    username: &str,
) -> Vec<(&'static str, Option<AgentError>)> {
    let username = username.to_owned();
    vec![
        (
            "create",
            refusal_of(
                service
                    .create_account(Request::new(CreateAccountRequest {
                        username: username.clone(),
                        quota_bytes: 0,
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    create_account_response::Result::Error(error) => Some(error),
                    create_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "suspend",
            refusal_of(
                service
                    .suspend_account(Request::new(SuspendAccountRequest {
                        username: username.clone(),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    suspend_account_response::Result::Error(error) => Some(error),
                    suspend_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "unsuspend",
            refusal_of(
                service
                    .unsuspend_account(Request::new(UnsuspendAccountRequest {
                        username: username.clone(),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    unsuspend_account_response::Result::Error(error) => Some(error),
                    unsuspend_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "delete",
            refusal_of(
                service
                    .delete_account(Request::new(DeleteAccountRequest {
                        username: username.clone(),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    delete_account_response::Result::Error(error) => Some(error),
                    delete_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "set_quota",
            refusal_of(
                service
                    .set_account_quota(Request::new(SetAccountQuotaRequest {
                        username: username.clone(),
                        quota_bytes: 1024,
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    set_account_quota_response::Result::Error(error) => Some(error),
                    set_account_quota_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "usage",
            refusal_of(
                service
                    .get_account_usage(Request::new(GetAccountUsageRequest {
                        username: username.clone(),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    get_account_usage_response::Result::Error(error) => Some(error),
                    get_account_usage_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "suspension_state",
            refusal_of(
                service
                    .get_account_suspension_state(Request::new(GetAccountSuspensionStateRequest {
                        username,
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    get_account_suspension_state_response::Result::Error(error) => Some(error),
                    get_account_suspension_state_response::Result::Ok(_) => None,
                },
            ),
        ),
    ]
}

/// Every rpc that acts on an account answers a reserved name with the typed
/// refusal, and nothing reaches the machine.
///
/// `create` is in the list for the same name: it is refused before the host is
/// asked whether such a user exists.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn every_account_rpc_refuses_root_with_invalid_input_and_runs_nothing() {
    let (service, _, effects) =
        service_over("root:x:0:0:root:/root:/bin/bash\n".to_owned() + &standard_passwd());

    for (rpc, refusal) in every_rpc_for(&service, "root").await {
        let error = refusal.unwrap_or_else(|| panic!("{rpc} must refuse root"));
        assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "{rpc} must answer a refusal of the request, not a fault or an absence"
        );
        assert_eq!(
            error.message, "account 'root' is not a hosting account this panel manages",
            "{rpc}"
        );
        assert!(error.tool_output.is_empty(), "{rpc}");
    }
    assert_eq!(
        *effects.lock().unwrap(),
        0,
        "a refused request must not spawn a program or measure a tree"
    );
}

/// An existing user that is not a hosting account is a refusal, an absent one
/// is not found, and the two are never confused with each other or with an
/// idempotency outcome.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn an_unmanaged_user_is_invalid_input_and_an_absent_user_is_not_found() {
    let passwd = standard_passwd()
        + "ubuntu:x:1000:1000::/home/ubuntu:/bin/bash\n\
           opsadmin:x:1003:1003::/srv/opsadmin:/bin/bash\n";
    let (service, _, effects) = service_over(passwd);

    for user in ["ubuntu", "opsadmin"] {
        for (rpc, refusal) in every_rpc_for(&service, user)
            .await
            .into_iter()
            .filter(|(rpc, _)| *rpc != "create")
        {
            let error = refusal.unwrap_or_else(|| panic!("{rpc} must refuse {user}"));
            assert_eq!(
                error.code,
                ErrorCode::InvalidInput as i32,
                "{rpc} on {user}"
            );
        }
    }
    assert_eq!(*effects.lock().unwrap(), 0);

    for (rpc, refusal) in every_rpc_for(&service, "ghostuser")
        .await
        .into_iter()
        .filter(|(rpc, _)| *rpc != "create")
    {
        let error = refusal.unwrap_or_else(|| panic!("{rpc} must refuse a user that is absent"));
        assert_eq!(error.code, ErrorCode::NotFound as i32, "{rpc}");
    }
}

/// The inverse control: a hosting account is still served over the same rpcs.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn a_hosting_account_is_still_suspended_and_unsuspended_over_the_rpc() {
    let (service, _, effects) = service_over(standard_passwd());

    let suspended = service
        .suspend_account(Request::new(SuspendAccountRequest {
            username: FAST_ACCOUNT.to_owned(),
        }))
        .await
        .unwrap()
        .into_inner();
    let unsuspended = service
        .unsuspend_account(Request::new(UnsuspendAccountRequest {
            username: FAST_ACCOUNT.to_owned(),
        }))
        .await
        .unwrap()
        .into_inner();

    assert!(
        matches!(
            suspended.result,
            Some(suspend_account_response::Result::Ok(_))
        ),
        "{suspended:?}"
    );
    assert!(
        matches!(
            unsuspended.result,
            Some(unsuspend_account_response::Result::Ok(_))
        ),
        "{unsuspended:?}"
    );
    assert!(
        *effects.lock().unwrap() > 0,
        "the accepted request must reach the host, or the refusals above prove nothing"
    );
}
