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

use std::path::Path;
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::{DistroFamily, adapter_for};
use maran_ops::accounts::{AccountError, AccountOperations, CommandOutcome, SystemHost};
use maran_ops::db::{DbError, DbHost};
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{CommandOutcome as SafeWriteOutcome, ConfigHost, SafeWriteError};
use maran_ops::sftp::{AccountOwnership, SftpError, SftpHost};
use tonic::Request;

use crate::proto::accounts_service_server::AccountsService;
use crate::proto::{
    CreateAccountRequest, GetAccountUsageRequest, create_account_response,
    get_account_usage_response,
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
}

impl SystemHost for BlockingSystemHost {
    /// Answers every program the operations run: success, with the digits the
    /// uid lookup parses. What each program did is not what this file tests.
    fn run(&self, _program: &str, _arguments: &[&str]) -> Result<CommandOutcome, AccountError> {
        Ok(CommandOutcome {
            status: 0,
            stdout: FAKE_UID_OUTPUT.to_owned(),
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
        Ok(0)
    }
}

/// The php host the service holds for the deletion cascade.
///
/// Deletion is not driven here, so every method says it is unreachable rather
/// than pretending to work — a fake that answered would invite a later test to
/// depend on an answer nobody designed.
struct UnusedPhpHost;

impl ConfigHost for UnusedPhpHost {
    /// Unreachable: no rpc driven here writes a pool.
    fn run(&self, _program: &str, _arguments: &[&str]) -> Result<SafeWriteOutcome, SafeWriteError> {
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
}

/// The database host the service holds for the deletion cascade.
struct UnusedDbHost;

impl DbHost for UnusedDbHost {
    /// Unreachable: no rpc driven here drops a database.
    fn execute(&self, _statement: &str) -> Result<String, DbError> {
        unreachable!("no rpc in this file reaches the database host")
    }
}

/// The SFTP host the service holds for the deletion cascade.
struct UnusedSftpHost;

impl SftpHost for UnusedSftpHost {
    /// Unreachable: no rpc driven here touches a login.
    fn run(
        &self,
        _program: &str,
        _arguments: &[&str],
        _stdin: Option<&str>,
    ) -> Result<CommandOutcome, SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
    }

    /// Unreachable: see [`UnusedSftpHost`].
    fn account_ownership(&self, _account: &AccountName) -> Result<AccountOwnership, SftpError> {
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

    /// Unreachable: see [`UnusedSftpHost`].
    fn account_logins(
        &self,
        _passwd_database: &str,
        _account: &AccountName,
        _jail_directory: &str,
    ) -> Result<Vec<SftpUserName>, SftpError> {
        unreachable!("no rpc in this file reaches the sftp host")
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

/// The service under test, and the cell its blocking host records into.
type ServiceUnderTest =
    AccountsServiceImpl<BlockingSystemHost, UnusedPhpHost, UnusedDbHost, UnusedSftpHost>;

/// Builds the service the way `server.rs` builds it, over the blocking host.
fn service() -> (Arc<ServiceUnderTest>, Arc<Mutex<Option<Instant>>>) {
    let blocking_began = Arc::new(Mutex::new(None));
    let host = BlockingSystemHost {
        blocking_began: Arc::clone(&blocking_began),
    };
    let distro = adapter_for(DistroFamily::Debian);

    (
        Arc::new(AccountsServiceImpl::new(
            AccountOperations::new(host, distro),
            UnusedPhpHost,
            UnusedDbHost,
            UnusedSftpHost,
        )),
        blocking_began,
    )
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
