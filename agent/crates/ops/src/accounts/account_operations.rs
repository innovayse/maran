//! The account operations themselves, over whatever [`SystemHost`] they are given.

use maran_agent_core::validation::name::AccountName;

use maran_distro::DistroAdapter;

use crate::accounts::{AccountError, AccountUsage, CreatedAccount, SystemHost};

/// Where every account's home directory lives.
const HOME_ROOT: &str = "/home";

/// Account operations: create, suspend, unsuspend, delete, quota, usage.
///
/// Everything it does goes through the injected [`SystemHost`], so the decisions in
/// here are tested without a single real user being created.
pub struct AccountOperations<H: SystemHost> {
    /// The machine these operations run against.
    host: H,

    /// Where platform facts come from. Operations never branch on a distribution
    /// themselves (rules/rust.md "Distro adapter"); they ask.
    distro: &'static dyn DistroAdapter,
}

impl<H: SystemHost> AccountOperations<H> {
    /// Creates the operations bound to `host` and the host's distribution adapter.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn DistroAdapter) -> Self {
        Self { host, distro }
    }

    /// The machine these operations run against.
    ///
    /// Exposed so a test can read back what its recording host was asked to run:
    /// the argv is the thing worth pinning, since `useradd --create-home` and
    /// `useradd -m` differ by nothing a type can see.
    #[must_use]
    pub fn host(&self) -> &H {
        &self.host
    }

    /// The absolute home directory of an account.
    #[must_use]
    pub fn home_directory(name: &AccountName) -> String {
        format!("{HOME_ROOT}/{}", name.as_str())
    }

    /// Creates the system user and its home directory, then applies `quota_bytes`.
    ///
    /// Idempotent in the sense the contract requires: an account that already
    /// exists is reported as [`AccountError::AlreadyExists`] and is left exactly as
    /// it was. The agent deliberately does not "fix up" a pre-existing user to
    /// match — a home directory it did not create may hold somebody's data, and
    /// silently re-owning it is the one mistake that cannot be undone.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::AlreadyExists`] when the user is already present,
    /// and [`AccountError::CommandFailed`] when `useradd` refuses.
    pub fn create(
        &self,
        name: &AccountName,
        quota_bytes: u64,
    ) -> Result<CreatedAccount, AccountError> {
        let username = name.as_str();
        if self.host.user_exists(username)? {
            return Err(AccountError::AlreadyExists {
                username: username.to_owned(),
            });
        }

        let home = Self::home_directory(name);

        // --create-home makes the directory and copies /etc/skel; --user-group gives
        // the account a group of its own, which is what makes per-account file modes
        // meaningful. Arguments are passed as an array — never a shell string.
        self.expect_success(
            "useradd",
            &[
                "--create-home",
                "--home-dir",
                &home,
                "--shell",
                self.distro.nologin_shell(),
                "--user-group",
                username,
            ],
        )?;

        // apply_quota, not set_quota: the public one confirms the account exists, and
        // asking that one line after creating it is a second `id` per creation for an
        // answer already known.
        self.apply_quota(username, quota_bytes)?;

        Ok(CreatedAccount {
            home_directory: home,
            uid: self.read_uid(username)?,
        })
    }

    /// Suspends the account: its shell is locked and its password disabled.
    ///
    /// Idempotent: suspending a suspended account succeeds and changes nothing,
    /// because a billing system calls this on every overdue invoice and must not
    /// have to remember what it already did.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::NotFound`] when the account does not exist.
    pub fn suspend(&self, name: &AccountName) -> Result<(), AccountError> {
        let username = self.require_existing(name)?;

        // Both, not either: `--lock` prefixes the password hash so no password can
        // match, and the nologin shell stops any authentication method that does not
        // consult the password at all — an SSH key already in place, for instance.
        //
        // The account already has the nologin shell from creation; setting it again is
        // what makes suspension correct for an account whose shell was changed by hand.
        self.expect_success("usermod", &["--lock", &username])?;
        self.expect_success(
            "usermod",
            &["--shell", self.distro.nologin_shell(), &username],
        )?;

        Ok(())
    }

    /// Reverses [`AccountOperations::suspend`].
    ///
    /// Idempotent, for the same reason suspension is.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::NotFound`] when the account does not exist.
    pub fn unsuspend(&self, name: &AccountName) -> Result<(), AccountError> {
        let username = self.require_existing(name)?;

        self.expect_success("usermod", &["--unlock", &username])?;
        self.expect_success(
            "usermod",
            &["--shell", self.distro.nologin_shell(), &username],
        )?;

        Ok(())
    }

    /// Removes the system user and everything under its home directory.
    ///
    /// Measures the tree before removing it, so the caller can report what was
    /// freed. Databases and FTP users are NOT removed here: the backend drops those
    /// through their own services first, so that each deletion is separately audited
    /// (see `proto/agent/v1/accounts.proto`).
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::NotFound`] when the account does not exist.
    pub fn delete(&self, name: &AccountName) -> Result<u64, AccountError> {
        let username = self.require_existing(name)?;
        let bytes_freed = self
            .host
            .directory_size(&Self::home_directory(name))
            .unwrap_or(0);

        self.expect_success("userdel", &["--remove", &username])?;

        Ok(bytes_freed)
    }

    /// Sets the account's filesystem quota, replacing whatever was in force.
    ///
    /// A quota of zero removes the limit, which is what `setquota` means by zero.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::NotFound`] when the account does not exist, and
    /// [`AccountError::CommandFailed`] when `setquota` refuses.
    pub fn set_quota(&self, name: &AccountName, quota_bytes: u64) -> Result<(), AccountError> {
        let username = self.require_existing(name)?;
        self.apply_quota(&username, quota_bytes)
    }

    /// Reads how much the account currently uses, and against what quota.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::NotFound`] when the account does not exist, and
    /// [`AccountError::UnreadableOutput`] when `quota` prints something this agent
    /// cannot parse.
    pub fn usage(&self, name: &AccountName) -> Result<AccountUsage, AccountError> {
        let username = self.require_existing(name)?;
        let used_bytes = self.host.directory_size(&Self::home_directory(name))?;

        // -w gives one line per filesystem with no header, which is the only shape
        // worth parsing; the human table changes between releases.
        let outcome = self.host.run("quota", &["-u", "-w", &username])?;
        let quota_bytes = Self::parse_quota_blocks(&outcome.stdout)
            .map(|blocks| blocks.saturating_mul(1024))
            .unwrap_or(0);

        Ok(AccountUsage {
            used_bytes,
            quota_bytes,
        })
    }

    /// Reads the hard block limit out of `quota -w` output.
    ///
    /// Returns `None` when there is no quota line, which is the ordinary state of a
    /// filesystem mounted without quotas — not an error, just no limit.
    fn parse_quota_blocks(stdout: &str) -> Option<u64> {
        stdout
            .lines()
            .filter_map(|line| {
                let mut fields = line.split_whitespace();
                let filesystem = fields.next()?;
                if !filesystem.starts_with('/') {
                    return None;
                }

                // Fields after the filesystem: blocks, soft, hard, … The hard limit is
                // the third, and a trailing `*` marks a limit already exceeded.
                let _blocks = fields.next()?;
                let _soft = fields.next()?;
                let hard = fields.next()?;
                hard.trim_end_matches('*').parse::<u64>().ok()
            })
            .next()
    }

    /// Applies a quota to a user already known to exist.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] when `setquota` refuses.
    fn apply_quota(&self, username: &str, quota_bytes: u64) -> Result<(), AccountError> {
        // setquota counts in 1 KiB blocks. Rounding UP matters: rounding down would
        // hand out a quota smaller than the plan the customer paid for, and the
        // difference would only ever be noticed as an unexplained write failure.
        let blocks = quota_bytes.div_ceil(1024).to_string();

        // Soft and hard limits are set to the same value, and inode limits to zero
        // (unlimited). A soft limit below the hard one only buys a grace period the
        // panel has no way to explain to the customer.
        self.expect_success(
            "setquota",
            &["-u", username, &blocks, &blocks, "0", "0", HOME_ROOT],
        )
    }

    /// Confirms the account exists and returns its name.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::NotFound`] when it does not.
    fn require_existing(&self, name: &AccountName) -> Result<String, AccountError> {
        let username = name.as_str().to_owned();
        if self.host.user_exists(&username)? {
            Ok(username)
        } else {
            Err(AccountError::NotFound { username })
        }
    }

    /// Reads a user's numeric uid.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::UnreadableOutput`] when `id` prints something other
    /// than a number.
    fn read_uid(&self, username: &str) -> Result<u32, AccountError> {
        let outcome = self.host.run("id", &["-u", username])?;
        outcome
            .stdout
            .trim()
            .parse::<u32>()
            .map_err(|_| AccountError::UnreadableOutput {
                program: "id".to_owned(),
            })
    }

    /// Runs a program and turns a non-zero exit into an error.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] carrying the program's own stderr,
    /// which is what tells an operator which tool refused and why.
    fn expect_success(&self, program: &str, arguments: &[&str]) -> Result<(), AccountError> {
        let outcome = self.host.run(program, arguments)?;
        if outcome.status == 0 {
            return Ok(());
        }

        Err(AccountError::CommandFailed {
            program: program.to_owned(),
            status: outcome.status,
            stderr: outcome.stderr.trim().to_owned(),
        })
    }
}

#[cfg(test)]
#[path = "../tests/accounts/account_operations_tests.rs"]
mod tests;
