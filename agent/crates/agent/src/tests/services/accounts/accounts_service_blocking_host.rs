//! The system host the accounts-service tests drive: it blocks inside one
//! account's spawn and counts every reach into the machine.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;
use maran_ops::accounts::{AccountError, CommandOutcome, SystemHost};

/// The account whose creation blocks inside the fake's spawn.
pub(crate) const SLOW_ACCOUNT: &str = "slowacct";

/// The account the second, concurrent rpc asks about.
pub(crate) const FAST_ACCOUNT: &str = "fastacct";

/// How long the slow account's spawn blocks.
///
/// Long enough that a second rpc stalled behind it cannot be mistaken for a
/// slow one, and short enough to keep the suite quick. The assertion's budget
/// below is a small fraction of it, so the two verdicts are separated by most
/// of a second in either direction rather than by a margin a loaded CI box
/// could close.
pub(crate) const BLOCKING_SPAWN: Duration = Duration::from_millis(1500);

/// How long the second rpc may take, measured from the moment the first one
/// entered its blocking spawn.
pub(crate) const SECOND_RPC_BUDGET: Duration = Duration::from_millis(500);

/// The uid the fake's `id -u` reports, as the digits the operation parses.
pub(crate) const FAKE_UID_OUTPUT: &str = "1000";

/// A system host that blocks for [`BLOCKING_SPAWN`] on one account and answers
/// at once for every other.
///
/// It stands in for the spawn itself: `useradd` on a real host takes long
/// enough to matter, and the only property under test is what the runtime is
/// doing while a spawn has not returned. The instant the block BEGAN is
/// recorded, because it is the only clock reading this test can trust — a
/// reading taken by the test's own future would itself be delayed by the
/// stall it is trying to measure.
pub(crate) struct BlockingSystemHost {
    /// When the blocking call began, set once by whichever thread runs it.
    pub(crate) blocking_began: Arc<Mutex<Option<Instant>>>,
    /// The local password database the host answers, which is what every
    /// operation on an existing account resolves its managed account from.
    pub(crate) passwd: String,
    /// How many times the host was asked to spawn a program or measure a tree:
    /// the two ways an operation reaches the machine as root. Zero is the
    /// proof that a refused request ran nothing.
    pub(crate) effects: Arc<Mutex<usize>>,
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

    /// No IPC objects exist: nothing there to find.
    fn read_sysv_ipc_table(
        &self,
        _kind: maran_ops::accounts::model::sysv_ipc_kind::SysvIpcKind,
    ) -> Result<String, AccountError> {
        Ok(String::new())
    }

    /// Returns at once: virtual time, and no rpc driven here deletes an account.
    fn pause(&self, _interval: Duration) {}

    fn available_bytes(&self, _directory: &std::path::Path) -> Result<u64, std::io::Error> {
        Ok(u64::MAX)
    }
}
