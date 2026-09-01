//! The account operations themselves, over whatever [`SystemHost`] they are given.

use maran_agent_core::validation::name::AccountName;

use maran_distro::DistroAdapter;

use crate::accounts::quota_blocks::QuotaBlocks;
use crate::accounts::{AccountError, AccountUsage, CreatedAccount, SystemHost};
use crate::php::{PhpHost, remove_account_pools};

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
    /// and [`AccountError::CommandFailed`] when `useradd`, the home permission step
    /// (see [`AccountOperations::open_home_to_the_web_server`]) or `setquota` refuses.
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

        self.open_home_to_the_web_server(&home)?;

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

    /// Removes the system user, every php-fpm pool it owns, and everything under
    /// its home directory.
    ///
    /// Measures the tree before removing it, so the caller can report what was
    /// freed. Databases and FTP users are NOT removed here: the backend drops those
    /// through their own services first, so that each deletion is separately audited
    /// (see `proto/agent/v1/accounts.proto`).
    ///
    /// **The pools go first, and the order is the whole of the risk.** A pool file
    /// names the account it runs as, and php-fpm resolves that name at startup, so
    /// the two orders are not two ways of arriving at the same place:
    ///
    /// - Pools first: while the account still exists every pool file is valid, so
    ///   `php-fpm -t` passes after each removal and each master reloads cleanly. At
    ///   every intermediate moment the host is in a state php-fpm will start from.
    /// - `userdel` first: every remaining pool instantly names a user that no longer
    ///   resolves. `php-fpm -t` then answers `cannot get uid for user '<account>'`
    ///   and the removal protocol — which validates AFTER unlinking and restores the
    ///   file when validation refuses — puts the pool back and reports failure. The
    ///   file becomes unremovable by the very operation meant to remove it, and the
    ///   host is left one reload away from having no PHP for any tenant.
    ///
    /// That second ordering is not hypothetical: it is the state the agent shipped
    /// in, because nothing removed a pool at all.
    ///
    /// The pool removal is NOT best-effort. A refusal aborts the deletion with the
    /// account still present, which is the recoverable half: an account that is
    /// still there can be deleted again once whatever refused is fixed, whereas an
    /// account that is gone with its pool left behind cannot be repaired by any
    /// operation this agent has.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::NotFound`] when the account does not exist, and
    /// [`AccountError::PoolRemoval`] when one of its pools could not be taken away —
    /// in which case `userdel` has not been run.
    pub fn delete(&self, php_host: &dyn PhpHost, name: &AccountName) -> Result<u64, AccountError> {
        let username = self.require_existing(name)?;
        let bytes_freed = self
            .host
            .directory_size(&Self::home_directory(name))
            .unwrap_or(0);

        remove_account_pools(php_host, self.distro, name)?;

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

        let outcome = self.host.run("quota", &["-u", "-w", &username])?;
        let quota_bytes = QuotaBlocks::parse_hard_limit(&outcome.stdout)
            .map(QuotaBlocks::to_bytes)
            .unwrap_or(0);

        Ok(AccountUsage {
            used_bytes,
            quota_bytes,
        })
    }

    /// Applies a quota to a user already known to exist.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] when `setquota` refuses.
    fn apply_quota(&self, username: &str, quota_bytes: u64) -> Result<(), AccountError> {
        let blocks = QuotaBlocks::from_bytes(quota_bytes).as_argument();

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

    /// Makes the account's home traversable by the web server, and by nothing else.
    ///
    /// `useradd --create-home` leaves the home `0750 <account>:<account>`, and the web
    /// server's user is in no group that can enter it. That is not a theoretical
    /// problem: a real nginx serving a site under such a home logs
    /// `stat() "/home/<account>/sites/<domain>/" failed (13: Permission denied)` and
    /// refuses every request, static site and PHP site alike. Every document root this
    /// agent creates is inside a home, so with the mode as `useradd` leaves it, no site
    /// this panel creates can be served at all.
    ///
    /// The fix is a GROUP and not a traversal bit, and that difference is the whole
    /// security decision here:
    ///
    /// - `chmod o+x /home/<account>` also works, and is what the reproduction used. It
    ///   opens the home to EVERY local user on the machine — every other customer's PHP
    ///   worker, every FTP session, every cron job — because "other" is not a principal,
    ///   it is everyone who is neither the owner nor the group. On a shared hosting
    ///   server that is exactly the set of people who must not be able to walk into each
    ///   other's homes.
    /// - Group-owning the home by the web server's group and keeping it `0750` grants
    ///   the traversal to one principal, the one that has to have it. "Other" still gets
    ///   nothing.
    ///
    /// What that group can then reach is bounded by the modes INSIDE the home, which are
    /// the account's own: `r-x` on the home lets the server walk through it, and every
    /// file below still answers according to its own bits. A customer who makes a file
    /// unreadable still has an unreadable file.
    ///
    /// It runs on creation and nowhere else. Re-applying it to an existing account would
    /// be the agent re-owning a directory it did not create, which is the one mistake
    /// [`AccountOperations::create`] refuses to make for a pre-existing user.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] when `chgrp` or `chmod` refuses — most
    /// plausibly because the web server's group does not exist, i.e. because no web
    /// server is installed. That is a failure and not something to shrug at: an account
    /// whose home the web server cannot enter is an account whose sites cannot be
    /// served, and reporting the creation as a success would hide it until a customer
    /// noticed.
    fn open_home_to_the_web_server(&self, home: &str) -> Result<(), AccountError> {
        // `--no-dereference`, so a symlink standing where the home should be is
        // re-grouped as the link rather than followed to whatever it points at. Nothing
        // should be able to plant one — `useradd` created this directory a line ago and
        // the account has no shell — but the flag costs nothing, and the alternative is a
        // root process following a link it did not verify.
        self.expect_success(
            "chgrp",
            &["--no-dereference", self.distro.web_server_group(), home],
        )?;

        // Restated rather than assumed. `useradd` honours `HOME_MODE`/`UMASK` from
        // /etc/login.defs, so the mode a home is born with is a host setting an operator
        // can change; a home left world-readable by such a setting would undo the whole
        // point of the group above, and this is the line that says what the panel
        // requires. Ownership of the home itself is `useradd`'s and is untouched — the
        // account owns its own home; only the group changed.
        self.expect_success("chmod", &["0750", home])
    }
}

#[cfg(test)]
#[path = "../tests/accounts/account_operations_tests.rs"]
mod tests;
