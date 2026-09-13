//! The account operations themselves, over whatever [`SystemHost`] they are given.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

use maran_distro::DistroAdapter;

use crate::accounts::account_lock::take_account_lock;
use crate::accounts::quota_blocks::QuotaBlocks;
use crate::accounts::{
    AccountError, AccountSuspensionState, AccountUsage, CreatedAccount, StoredPassword, SystemHost,
};
use crate::cron::cron_lock::cron_lock;
use crate::cron::{CronHost, NO_CRONTAB_MARKER, inspect_account_cron};
use crate::db::{DbHost, drop_account_databases};
use crate::ftps::{FtpsHost, remove_account_ftps};
use crate::logins::{LoginsHost, account_logins};
use crate::php::{PhpHost, remove_account_pools};
use crate::sftp::{SftpHost, remove_account_sftp};
use crate::sites::{SiteHost, inspect_account_sites};

/// The argument that makes `passwd` REPORT a login's state instead of changing
/// it.
///
/// Named so that the one place this agent runs `passwd` cannot be read as the
/// place it sets a password: `-S` is the whole difference.
const PASSWORD_STATUS_ARGUMENT: &str = "-S";

/// What `passwd -S` prints in its second field for a locked password — the
/// FIRST LETTER of it, because the two families do not spell the rest the same.
///
/// Measured on both polygon images rather than assumed, because assuming cost
/// the RHEL family a working suspension:
///
/// ```text
/// ubuntu24:  u1 P  …   u1 L  …        (Debian shadow-utils)
/// alma9:     u1 PS …   u1 LK …        (RHEL's passwd)
/// ```
///
/// An exact comparison against `"L"` is therefore true on Debian and FALSE on
/// every RHEL host, which made the login half of the suspension attestation
/// report every account as unlocked there and refuse every suspension. Matching
/// the first letter is unambiguous on both: the other states are `P`/`PS` for a
/// usable password and `NP` for none at all, and neither begins with `L`.
const LOCKED_PASSWORD_PREFIX: char = 'L';

/// The database `getent` is asked for when the question is about a password.
const SHADOW_DATABASE: &str = "shadow";

/// The separator between the fields of a shadow entry.
const SHADOW_FIELD_SEPARATOR: char = ':';

/// Which field of a shadow entry holds the password, counting from zero.
///
/// `<name>:<password>:<last change>:…` — the name is field 0 and the password
/// is field 1. Named rather than written as a literal `1`, because a bare
/// index next to `split` is the kind of thing a later edit moves by one
/// without anything noticing: the entry would still parse, and the agent would
/// classify a DATE as a password.
const SHADOW_PASSWORD_FIELD: usize = 1;

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

    /// The distribution adapter these operations ask for platform facts.
    ///
    /// Exposed for the same reason [`AccountOperations::host`] is: a test that
    /// wants to prove a tool was run at the path the adapter names has to be
    /// able to ask the adapter for that path, rather than repeating the literal
    /// and passing whatever the operation happens to do.
    #[must_use]
    pub fn distro(&self) -> &'static dyn DistroAdapter {
        self.distro
    }

    /// The absolute home directory of an account.
    ///
    /// Rooted at [`AgentPaths::ACCOUNT_HOME_ROOT`] and never at a literal of
    /// this area's own. `AgentPaths` is the single inventory of the locations
    /// the agent owns, and its own doc states why: it is what makes "the path
    /// this operation built" and "the path the check approved" the same path.
    /// This area used to keep a second constant with the same value, which made
    /// that promise true only for as long as nobody edited one of them —
    /// `resolve_in_home` roots its containment at `AgentPaths`, so a drift here
    /// would have been an operation building under a root the check does not
    /// approve.
    #[must_use]
    pub fn home_directory(name: &AccountName) -> String {
        format!("{}/{}", AgentPaths::ACCOUNT_HOME_ROOT, name.as_str())
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
    /// and [`AccountError::CommandFailed`] when `useradd`, the step that gives the
    /// home directory to the web server's group, or `setquota` refuses.
    ///
    /// That step is named in prose rather than linked: it is private, and a public
    /// doc comment linking a private item is an error under `-D warnings`, which is
    /// how CI runs rustdoc. Widening the method to satisfy a link would put a
    /// method in the public API for the sake of a cross-reference.
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
            self.distro.useradd_binary(),
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
        self.expect_success(self.distro.usermod_binary(), &["--lock", &username])?;
        self.expect_success(
            self.distro.usermod_binary(),
            &["--shell", self.distro.nologin_shell(), &username],
        )?;

        Ok(())
    }

    /// Reverses [`AccountOperations::suspend`].
    ///
    /// Idempotent, for the same reason suspension is.
    ///
    /// # Why the unlock is conditional, and why that is not a special case
    ///
    /// `usermod --unlock` refuses a login that has no password hash — "unlocking
    /// the user's password would result in a passwordless account" — and the two
    /// families spell that refusal differently: **exit 0 on Debian, exit 1 on
    /// RHEL**, measured on both polygon images and unchanged under `LC_ALL=C`.
    /// Every hosting account is exactly such a login: `useradd` leaves the field
    /// as `!`/`!!` and this agent never sets a password on an account's own
    /// entry — the credentials it hands a customer are SFTP logins. So an
    /// unconditional `--unlock` whose non-zero status is a failure **could not
    /// succeed on any RHEL host for any account**, and reactivation was
    /// impossible on half the supported matrix from the day this was written.
    ///
    /// The repair is not to forgive the exit status but to stop making the call
    /// that has nothing to do. `usermod --lock` on a passwordless login is a
    /// measured no-op on both families — the field keeps the exact bytes
    /// `useradd` wrote — so for such an account the suspension never locked a
    /// password and the reversal has none to restore. Asking
    /// [`StoredPassword`] which state the field is in answers that portably,
    /// and the unlock runs on the one state where it means something.
    ///
    /// What this deliberately does NOT do, each rejected on its own grounds:
    /// match `usermod`'s English sentence (locale-dependent, and this
    /// repository already has a finding where a translated message changed a
    /// parse's meaning); treat exit 1 as success (it would swallow "cannot
    /// update the password file" and every other real refusal of the same
    /// call); or decide from `passwd -S`, which reports `L`/`LK` for a
    /// passwordless login and for one locked over a real password alike and
    /// therefore cannot make this distinction at all.
    ///
    /// The lock is not weakened. Fewer inputs reach `--unlock` than before, not
    /// more, and a real `Locked` account is still unlocked with its status
    /// required to be zero.
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotFound`] when the account does not exist.
    /// - [`AccountError::CommandFailed`] when the shadow entry cannot be read,
    ///   when `usermod --unlock` refuses a genuinely locked account, or when
    ///   the shell cannot be set.
    /// - [`AccountError::UnreadableOutput`] when the shadow entry does not have
    ///   the shape `<name>:<password>:…` for this account.
    pub fn unsuspend(&self, name: &AccountName) -> Result<(), AccountError> {
        let username = self.require_existing(name)?;

        if self.stored_password(&username)? == StoredPassword::Locked {
            self.expect_success(self.distro.usermod_binary(), &["--unlock", &username])?;
        }

        self.expect_success(
            self.distro.usermod_binary(),
            &["--shell", self.distro.nologin_shell(), &username],
        )?;

        Ok(())
    }

    /// Reports what this host can be OBSERVED to be doing for the account.
    ///
    /// Read-only: it changes nothing, and it may be called on an account in
    /// any state. It exists so the panel can refuse to report a suspension it
    /// cannot see.
    ///
    /// # Why an observation and not a return value of `suspend`
    ///
    /// `suspend` returning `Ok(())` says two `usermod` invocations exited
    /// zero. It says nothing about whether the account's sites stopped
    /// serving, because this agent is not what stopped them — the panel drives
    /// `DisableSite` per site, and a site it has forgotten is never driven at
    /// all. Suspension's residue is on the HOST, so the check has to look at
    /// the host; the residue audit that verifies DELETION reads the panel's
    /// rows, which is right for deletion (its residue IS rows) and would be
    /// green over a serving site here.
    ///
    /// # Why it takes three hosts
    ///
    /// Because suspension's residue is spread across three subsystems and the
    /// evidence has to come from each of them in turn: the vhost directory,
    /// the account's crontab, and the password database. One host would mean
    /// one area answering about another's files, and the answer is worth
    /// exactly as much as the seam it came through.
    ///
    /// The third of them is the LOGIN host and not the SFTP one, because the
    /// question is "which credentials into this home exist" and an SFTP host
    /// can only answer about SFTP — which is how an FTPS login would have gone
    /// unlocked and unreported under a suspension that claimed to cover
    /// everything.
    ///
    /// # What it does not observe
    ///
    /// The account's databases, the panel's own web login, and the FOREIGN
    /// lines of the crontab — each stated on [`AccountSuspensionState`] with
    /// its reason. The foreign lines are counted rather than ignored.
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotFound`] when the account does not exist.
    /// - [`AccountError::SiteInspection`] when a vhost exists and cannot be
    ///   read — refusing to answer rather than answering about the files that
    ///   happened to be readable.
    /// - [`AccountError::CronInspection`] when the account's crontab exists
    ///   and cannot be read. An unreadable crontab is never reported as an
    ///   empty one: that is the answer that reads as "nothing is firing".
    /// - [`AccountError::SftpInspection`] when the password database cannot be
    ///   enumerated, or `passwd -S` refuses or prints something unreadable for
    ///   one of the account's logins. The variant is named for the area the
    ///   enumeration used to live in and now covers both protocols; renaming it
    ///   is a wire-visible change and belongs with the task that widens the
    ///   contract.
    /// - [`AccountError::CommandFailed`] or
    ///   [`AccountError::UnreadableOutput`] when `passwd -S` refuses or prints
    ///   something this agent cannot read for the account's OWN login.
    pub fn suspension_state(
        &self,
        site_host: &dyn SiteHost,
        cron_host: &dyn CronHost,
        logins_host: &dyn LoginsHost,
        name: &AccountName,
    ) -> Result<AccountSuspensionState, AccountError> {
        let username = self.require_existing(name)?;
        let sites = inspect_account_sites(site_host, name)?;
        // Both conversions are written out rather than ridden on `?`: the
        // blanket `From<SftpError>` means "the deletion did not happen", which
        // is the wrong sentence for an observation, and cron has no blanket
        // conversion for the same reason.
        let cron = inspect_account_cron(cron_host, name).map_err(|error| {
            AccountError::CronInspection {
                reason: error.to_string(),
            }
        })?;
        let logins = account_logins(logins_host, self.distro, name).map_err(|error| {
            AccountError::SftpInspection {
                reason: error.to_string(),
            }
        })?;

        Ok(AccountSuspensionState {
            login_locked: self.login_locked(&username)?,
            login_password: self.stored_password(&username)?,
            sites_directory_readable: sites.directory_readable,
            sites: sites.sites,
            cron,
            logins,
        })
    }

    /// Removes everything on this host that belongs to the account, then the
    /// account itself.
    ///
    /// The databases and their users, the SFTP logins with the account's jail
    /// and the bind mount that filled it, the FTPS logins with their own
    /// separate jail and mount, the crontab, every php-fpm pool, and finally the
    /// system user with everything under its home directory. Measures the tree
    /// before removing it, so the caller can report what was freed.
    ///
    /// # Why all of it happens here, and not in the panel one call at a time
    ///
    /// `userdel` touches neither MySQL, nor sshd, nor vsftpd. An account deletion that
    /// removed only the system user therefore left every `<account>_*` database
    /// on the server and every `<account>_*` login in the password database —
    /// and system user names are RECYCLED, so an account created again under
    /// the same name inherited the previous tenant's live data and a working
    /// credential into it. That is not a leak that can be repaired afterwards:
    /// nothing in the panel points at the orphans any more, and the second
    /// tenant is already inside them.
    ///
    /// Each area is asked what the HOST holds rather than being handed a list,
    /// because a list can only ever describe what the panel remembers creating.
    ///
    /// **Everything precedes `userdel`, and the order is the whole of the
    /// risk.**
    ///
    /// - A pool file names the account it runs as, and php-fpm resolves that
    ///   name at startup. Removed while the account still exists, every pool
    ///   file is valid, so `php-fpm -t` passes after each removal and each
    ///   master reloads cleanly. Removed after `userdel`, every remaining pool
    ///   instantly names a user that no longer resolves; `php-fpm -t` answers
    ///   `cannot get uid for user '<account>'`, and the removal protocol — which
    ///   validates AFTER unlinking and restores the file when validation refuses
    ///   — puts the pool back and reports failure. The file becomes unremovable
    ///   by the very operation meant to remove it, and the host is left one
    ///   reload away from having no PHP for any tenant.
    /// - The CRONTAB lives in the cron spool and not in the home, so `userdel
    ///   --remove` does not take it. Measured on both families rather than
    ///   assumed: the table survives as
    ///   `/var/spool/cron/crontabs/<name>` on the Debian family and
    ///   `/var/spool/cron/<name>` on the RHEL one, and neither `userdel` removes
    ///   either. The spool is keyed by NAME and the host recycles names, so an
    ///   account created again under the same one adopts the previous tenant's
    ///   table whole — `crontab -u <name> -l` prints it, and the panel presents
    ///   it on the new customer's screen as their own live entry.
    ///
    ///   Bounded precisely, because overstating it would be as wrong as missing
    ///   it: the inherited row is INERT. Every managed line names a `0600`
    ///   `.cmd` file under the old home, which went with the old account, so
    ///   what the new customer sees is a schedule pointing at a path that no
    ///   longer exists. Hygiene and confusion, not code execution. The
    ///   ownership half is smaller still: on the Debian family the surviving
    ///   file keeps the numeric uid it always had and `ls` merely renders that
    ///   uid as whoever holds it next — nothing chowns anything — and on the
    ///   RHEL family the file is root-owned throughout, so nothing changes at
    ///   all.
    /// - An SFTP login shares the account's uid, and `userdel` refuses to remove
    ///   a home another passwd entry still claims.
    /// - The account's home is BIND-MOUNTED inside its jail. Unmounting after
    ///   `userdel --remove` would mean `userdel` walking into a mount and
    ///   deleting the customer's files from inside the jail, and a mount left
    ///   behind afterwards points at a home that no longer exists — a state the
    ///   uninstaller refuses to clean up and a re-created account would inherit.
    ///
    /// That second ordering is not hypothetical for the pools: it is the state
    /// the agent shipped in, because nothing removed a pool at all.
    ///
    /// # The order is only a guarantee while nothing else is writing
    ///
    /// Every argument above is an argument about the order of THIS function's
    /// steps, and each of them was silently conditional on no other operation
    /// touching the account meanwhile. It was not: a `write_pool` landing
    /// between `remove_account_pools` and `userdel` puts back exactly the pool
    /// the ordering exists to prevent, and a `create_sftp_user` spanning
    /// `remove_account_sftp` and `userdel` leaves a login, a jail and a live
    /// password that nothing will ever look for again. So the sequence runs
    /// under the account's lock (`crate::accounts::account_lock`), which the
    /// pool writer, the SFTP login creation and the four backup operations take
    /// too. The order stays the order; the lock is what makes the order a
    /// statement about the host rather than about this function.
    ///
    /// **No step is best-effort.** The first refusal aborts the deletion with
    /// the account still present, which is the recoverable half: an account that
    /// is still there can be deleted again once whatever refused is fixed,
    /// whereas an account that is gone with its database, its login or its mount
    /// left behind cannot be repaired by any operation this agent has.
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotFound`] when the account does not exist.
    /// - [`AccountError::DatabaseRemoval`] when a database or a database user
    ///   could not be dropped.
    /// - [`AccountError::SftpRemoval`] when an SFTP login, its bind mount, its
    ///   jail or its unit could not be taken away.
    /// - [`AccountError::FtpsRemoval`] when an FTPS login, its bind mount, its
    ///   jail or its unit could not be taken away. Its own variant, because the
    ///   two protocols have different jail roots and an operator sent to the
    ///   wrong one finds nothing wrong there.
    /// - [`AccountError::PoolRemoval`] when one of its pools could not be taken
    ///   away.
    /// - [`AccountError::CommandFailed`] when `crontab` refused to remove the
    ///   account's table for any reason other than there not being one.
    /// - [`AccountError::Busy`] when another operation for this account — a
    ///   backup, a restore, an SFTP login creation, a pool write or another
    ///   deletion — is already running on this host.
    ///
    /// In every one of those cases `userdel` has NOT been run.
    pub fn delete(
        &self,
        php_host: &dyn PhpHost,
        db_host: &dyn DbHost,
        sftp_host: &dyn SftpHost,
        ftps_host: &dyn FtpsHost,
        name: &AccountName,
    ) -> Result<u64, AccountError> {
        // Taken FIRST, before a single thing is read, and held for the whole
        // sequence by living in this scope: the guard is owned, so every return
        // below — including every `?` — releases it. Nothing the sequence calls
        // takes it again (`crate::accounts::account_lock` names the callers), so
        // there is no nesting to deadlock on.
        let _guard = take_account_lock(name).ok_or_else(|| AccountError::Busy {
            username: name.as_str().to_owned(),
        })?;

        self.delete_under_lock(php_host, db_host, sftp_host, ftps_host, name)
    }

    /// The deletion sequence itself, with the account's lock ALREADY held.
    ///
    /// Split from [`AccountOperations::delete`] for the reason
    /// `restore_backup`'s body is split from its entry point: the lock is a
    /// process-wide static, and a unit test that drove the public entry would
    /// be one of a dozen tests contending for one account name on the
    /// harness's own threads — a flaky suite whose flake says nothing about the
    /// code. The public entry is one statement, the exclusion is tested for
    /// what it is, and everything else is exercised here.
    ///
    /// # Errors
    ///
    /// Every variant [`AccountOperations::delete`] documents except
    /// [`AccountError::Busy`], which is the entry point's own answer.
    fn delete_under_lock(
        &self,
        php_host: &dyn PhpHost,
        db_host: &dyn DbHost,
        sftp_host: &dyn SftpHost,
        ftps_host: &dyn FtpsHost,
        name: &AccountName,
    ) -> Result<u64, AccountError> {
        let username = self.require_existing(name)?;
        let bytes_freed = self
            .host
            .directory_size(&Self::home_directory(name))
            .unwrap_or(0);

        drop_account_databases(db_host, name)?;
        remove_account_sftp(sftp_host, self.distro, name)?;

        // Directly after the SFTP teardown, and before `userdel`, because it is
        // the same class of resource with the same failure and the same fix:
        // a `--non-unique` login carrying this account's uid, a bind mount of
        // this account's home, and a systemd unit that re-establishes that mount
        // on every boot. `userdel` takes none of the three, and `userdel
        // --remove` on an account whose home is still bind-mounted inside a jail
        // deletes the customer's files from inside that jail.
        //
        // Beside the SFTP step rather than merged with it, because the two are
        // blind to each other by construction: each enumerates the passwd
        // database filtered by ITS OWN jail directory, so neither can revoke the
        // other's logins or unmount the other's jail. An account may hold logins
        // of both kinds, and on most hosts it holds neither FTPS login nor FTPS
        // jail — which is why this step is a no-op that touches nothing when
        // FTPS was never enabled, rather than a refusal.
        //
        // Its own `AccountError::FtpsRemoval`, so a refusal here does not send
        // an operator to look at sshd's jail root while a vsftpd one is what is
        // still mounted. Like every other step it is NOT best-effort: the first
        // refusal aborts the deletion with the account still present, which is
        // the recoverable half.
        remove_account_ftps(ftps_host, self.distro, name)?;

        {
            // The SEVENTH writer of this account's crontab, and the only one
            // outside `ops::cron`. The other six take `cron_lock`; a deletion
            // that did not would be a removal racing an install, and an install
            // landing between it and `userdel` re-creates a spool for an
            // account that is about to stop existing. `userdel` does not remove
            // a spool file — measured on both families — and cron keys that
            // file by NAME on a host that recycles names, so the survivor is
            // handed whole to the next tenant of this name.
            //
            // It is `ops::cron`'s lock and not a second one for the same
            // account: two locks over one spool file are two locks and no
            // exclusion.
            //
            // Scoped to this statement rather than held to the end of the
            // deletion, because it WAITS. A guard held across `userdel` would
            // make every cron operation for this account wait on a `userdel`
            // that has nothing to do with the crontab, and the removal is the
            // only part of the sequence that touches one.
            let _crontab = cron_lock(name);
            self.remove_crontab(&username)?;
        }

        // LAST of the pre-`userdel` steps, and its position is still the point,
        // even now that the window it narrowed is closed. A pool written by
        // another operation after this sweep would survive `userdel` naming a
        // user that no longer resolves, which is the trap
        // `crate::php::remove_pool` exists to close and which takes PHP down
        // for every tenant on the host. `crate::php::write_pool` now takes the
        // same account lock this sequence holds, so such a write can only be
        // entirely before this deletion or refused with
        // `PhpOpError::AccountBusy`; the ordering stays because the sweep's own
        // `php-fpm -t` has to run while the account still exists — after
        // `userdel` every remaining pool of this account names a user that no
        // longer resolves, and the removal protocol validates AFTER unlinking,
        // so it would put the file back.
        remove_account_pools(php_host, self.distro, name)?;

        // Asked again, immediately before the irreversible step. The lock above
        // excludes every operation of this agent that could have removed the
        // account since the first check, so what this catches is the one thing
        // the lock cannot see: a `userdel` an operator ran by hand, or a second
        // agent binary. A validated `AccountName` proves the name is well
        // formed; it has never proved the account is still there, and the
        // sequence between the two checks is several process spawns long.
        let _ = self.require_existing(name)?;

        self.expect_success(self.distro.userdel_binary(), &["--remove", &username])?;

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

        let outcome = self
            .host
            .run(self.distro.quota_binary(), &["-u", "-w", &username])?;
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
            self.distro.setquota_binary(),
            &[
                "-u",
                username,
                &blocks,
                &blocks,
                "0",
                "0",
                AgentPaths::ACCOUNT_HOME_ROOT,
            ],
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
        let outcome = self.host.run(self.distro.id_binary(), &["-u", username])?;
        outcome
            .stdout
            .trim()
            .parse::<u32>()
            .map_err(|_| AccountError::UnreadableOutput {
                program: self.distro.id_binary().to_owned(),
            })
    }

    /// Reads whether the account's password is locked, as `passwd -S` reports
    /// it.
    ///
    /// The status line is `<name> <state> <last change> ...`, and the state is
    /// `L` for locked, `P` for a usable password and `NP` for none at all.
    /// Only `L` counts as locked here: `NP` is an account with NO password,
    /// which is not the same thing and must not be reported as suspended — an
    /// SSH key or any authentication method that does not consult the password
    /// still works against it.
    ///
    /// The NAME is matched too, and not merely the second field. `passwd -S`
    /// without a name prints the invoking user's line, and a future edit that
    /// dropped the argument would otherwise be reported as the account's own
    /// state.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] when `passwd` exits non-zero
    /// and [`AccountError::UnreadableOutput`] when its first line does not
    /// have that shape. Neither is answered with `false`: "the agent could not
    /// tell" and "the login is open" are different facts, and only one of them
    /// may be reported to a caller deciding whether an account is suspended.
    /// Reads what the account's shadow password field actually holds.
    ///
    /// `getent shadow <name>`, and never a read of `/etc/shadow` itself: the
    /// question is about ONE name, `getent` answers through the host's
    /// configured name service rather than only the local file, and asking for
    /// one key returns one line instead of pulling every hash on the host into
    /// a root process (rules/security.md item 8).
    ///
    /// The field is classified immediately and the bytes are dropped.
    /// [`StoredPassword`] holds no hash in any variant, and no error raised
    /// here carries the entry — [`AccountError::UnreadableOutput`] names the
    /// program and nothing else, precisely so that a malformed shadow line
    /// cannot be echoed into a log by the code that failed to parse it.
    ///
    /// The NAME is matched as well as the field, for the reason
    /// `login_locked` matches it: a future edit that dropped the argument
    /// would otherwise have some other account's entry classified as this
    /// one's.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] when `getent` exits non-zero.
    /// That includes its "key not found" status (2, documented in `getent(1)`
    /// and measured as 2 on both families): an account `require_existing` has
    /// just resolved and which nevertheless has no shadow entry is a host in a
    /// state this agent will not guess about. And
    /// [`AccountError::UnreadableOutput`] when the first line is not
    /// `<name>:<password>:…` for this account.
    fn stored_password(&self, username: &str) -> Result<StoredPassword, AccountError> {
        let program = self.distro.getent_binary();
        let outcome = self.host.run(program, &[SHADOW_DATABASE, username])?;
        if outcome.status != 0 {
            return Err(AccountError::command_failed(program, &outcome));
        }

        let mut fields = outcome
            .stdout
            .lines()
            .next()
            .unwrap_or_default()
            .split(SHADOW_FIELD_SEPARATOR);

        match (fields.next(), fields.nth(SHADOW_PASSWORD_FIELD - 1)) {
            (Some(name), Some(field)) if name == username => Ok(StoredPassword::classify(field)),
            _ => Err(AccountError::UnreadableOutput {
                program: program.to_owned(),
            }),
        }
    }

    fn login_locked(&self, username: &str) -> Result<bool, AccountError> {
        let program = self.distro.passwd_binary();
        let outcome = self
            .host
            .run(program, &[PASSWORD_STATUS_ARGUMENT, username])?;
        if outcome.status != 0 {
            return Err(AccountError::command_failed(program, &outcome));
        }

        let mut fields = outcome
            .stdout
            .lines()
            .next()
            .unwrap_or_default()
            .split_whitespace();

        match (fields.next(), fields.next()) {
            (Some(name), Some(state)) if name == username => {
                Ok(state.starts_with(LOCKED_PASSWORD_PREFIX))
            }
            _ => Err(AccountError::UnreadableOutput {
                program: program.to_owned(),
            }),
        }
    }

    /// Removes the account's crontab from the host's cron spool.
    ///
    /// `crontab -u <account> -r`, which is the only correct way to write that
    /// spool: where it lives, what owns it and how the daemon learns it changed
    /// are `crontab(1)`'s business on each family, and unlinking the file
    /// directly gets one of those wrong somewhere.
    ///
    /// An account that has no table is NOT a failure. Both cron lineages answer
    /// `no crontab for <account>` and exit non-zero for that, and every account
    /// that never used the feature is in exactly that state, so treating it as a
    /// refusal would make deleting an ordinary account impossible. The sentence
    /// is matched in standard ERROR and nowhere else, for the reason
    /// `crontab_spool` sets out at length: standard output carries the
    /// customer's own crontab, so an account that wrote that sentence into its
    /// table could otherwise decide what this function concludes.
    ///
    /// # Why a name, and why here rather than after `userdel`
    ///
    /// The spool is addressed by the account's NAME and never by its uid, and
    /// this runs while the account still exists — `require_existing` has already
    /// resolved it, and `userdel` has not run. Both halves matter, because the
    /// same recycling that creates the defect could just as easily make the
    /// cleanup remove somebody else's table:
    ///
    /// - By uid, the removal would be wrong the moment a uid was reused, and
    ///   `crontab(1)` offers no way to name one anyway.
    /// - Afterwards, the name no longer resolves at all: `crontab -u` answers
    ///   `user <name> unknown` for a deleted account, so the step could only be
    ///   done by unlinking a path this agent guessed — and a guessed path is
    ///   exactly what could point at whatever holds that name next.
    ///
    /// Run at this point, the name resolves to this account and to no other,
    /// and the removal is as exact as the deletion it belongs to. It is not a
    /// prefix match, so a neighbour whose name this one prefixes keeps its
    /// table.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] when `crontab` refused for any
    /// other reason, so a table that could not be removed stops the deletion
    /// while the account is still there to try again.
    fn remove_crontab(&self, username: &str) -> Result<(), AccountError> {
        let program = self.distro.crontab_binary();
        let outcome = self.host.run(program, &["-u", username, "-r"])?;
        if outcome.status == 0 || outcome.stderr.contains(NO_CRONTAB_MARKER) {
            return Ok(());
        }

        Err(AccountError::command_failed(program, &outcome))
    }

    /// Runs a program and turns a non-zero exit into an error.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`], which names the program and its
    /// exit status. The tool's own sentence does not travel in the error — it is
    /// written to the agent's log by `AccountError::command_failed`, for the
    /// reason that variant sets out.
    fn expect_success(&self, program: &str, arguments: &[&str]) -> Result<(), AccountError> {
        let outcome = self.host.run(program, arguments)?;
        if outcome.status == 0 {
            return Ok(());
        }

        Err(AccountError::command_failed(program, &outcome))
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
            self.distro.chgrp_binary(),
            &["--no-dereference", self.distro.web_server_group(), home],
        )?;

        // Restated rather than assumed. `useradd` honours `HOME_MODE`/`UMASK` from
        // /etc/login.defs, so the mode a home is born with is a host setting an operator
        // can change; a home left world-readable by such a setting would undo the whole
        // point of the group above, and this is the line that says what the panel
        // requires. Ownership of the home itself is `useradd`'s and is untouched — the
        // account owns its own home; only the group changed.
        self.expect_success(self.distro.chmod_binary(), &["0750", home])
    }
}

#[cfg(test)]
#[path = "../tests/accounts/account_operations_tests.rs"]
mod tests;
