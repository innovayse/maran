//! The account operations themselves, over whatever [`SystemHost`] they are given.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::system_accounts::system_accounts;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
use maran_agent_core::validation::system::name::AccountName;

use maran_distro::{AdapterBinary, DistroAdapter};

use crate::accounts::account_lock::take_account_lock;
use crate::accounts::end_account_processes::end_account_processes;
use crate::accounts::model::home_group_repair_refusal::HomeGroupRepairRefusal;
use crate::accounts::model::home_group_repair_report::HomeGroupRepairReport;
use crate::accounts::model::quota_state::QuotaState;
use crate::accounts::model::quota_unenforceable_reason::QuotaUnenforceableReason;
use crate::accounts::model::refused_home::RefusedHome;
use crate::accounts::model::repaired_home::RepairedHome;
use crate::accounts::quota_blocks::QuotaBlocks;
use crate::accounts::quota_enforceability::classify as classify_quota_enforceability;
use crate::accounts::quota_enforceability::quota_device_for;
use crate::accounts::{
    AccountError, AccountSuspensionState, AccountUsage, CreatedAccount, StoredPassword, SystemHost,
};
use crate::cron::cron_lock::cron_lock;
use crate::cron::{CronError, CronHost, NO_CRONTAB_MARKER, inspect_account_cron};
use crate::db::{DbHost, drop_account_databases};
use crate::ftps::{FtpsHost, remove_account_ftps};
use crate::logins::{LoginsError, LoginsHost, account_logins};
use crate::php::{PhpHost, remove_account_pools};
use crate::sftp::{SftpHost, remove_account_sftp};
use crate::sites::{SiteHost, inspect_account_sites};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

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

/// The database `getent` is asked for when the question is about a group's
/// numeric id.
///
/// Asked through `getent` rather than resolved with `getgrnam_r` directly, for
/// the same reason [`AccountOperations::stored_password`] asks `getent` for a
/// password field instead of reading `/etc/shadow`: the host's configured name
/// service, not only the local file, and — the reason that matters here more
/// than there — the same [`SystemHost`] seam every other fact this area reads
/// goes through, so a repair that decides whether a home's group is already
/// correct is testable against a fake host instead of the machine's own group
/// database.
const GROUP_DATABASE: &str = "group";

/// Which field of a `getent group` row holds the numeric group id, counting
/// from zero.
///
/// The row's shape is `<name>:<password>:<gid>:<members>`.
const GROUP_GID_FIELD: usize = 2;

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
///
/// **Every operation on an existing account first resolves a
/// [`ManagedAccount`]**, the proof that the host user behind the name is a
/// hosting account, and only that proof reaches `usermod`, `userdel`,
/// `setquota`, `quota`, `passwd`, `getent` and `crontab -u`. A bare
/// [`AccountName`] proves spelling only, and `root` is spelled correctly.
/// `create` is the one operation with no user to resolve; it refuses a reserved
/// name instead, before anything runs.
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
    ///
    /// The same string [`ManagedAccount`] compares a passwd row's home with, and
    /// for the same reason: both are built from `AgentPaths::ACCOUNT_HOME_ROOT`,
    /// so the home an account is created at and the home it is later accepted at
    /// cannot drift apart.
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
    /// A reserved name is refused FIRST, before any host fact is read and before
    /// `useradd`, and is not reported as [`AccountError::AlreadyExists`]: the
    /// refusal is a decision about the name and costs no lookup, so it cannot be
    /// used to ask whether such a user exists. A name that is not reserved but
    /// already belongs to ANY user, hosting account or not, is still
    /// [`AccountError::AlreadyExists`] and is never adopted.
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotManaged`] when the name is reserved, before anything
    ///   ran; or when the user this call just created would not be one this
    ///   agent could manage afterwards (the host's id floor moved, say), in which
    ///   case the creation is rolled back.
    /// - [`AccountError::AlreadyExists`] when the user is already present.
    /// - [`AccountError::CommandFailed`] when `useradd`, the step that gives the
    ///   home directory to the web server's group, or `setquota` refuses.
    /// - [`AccountError::Bound`] when a program it ran outlived its deadline or its output ceiling and
    ///   was killed with its whole process group. Not a verdict: the step may have taken
    ///   effect on the host, so the caller reads the account again before it believes
    ///   either outcome.
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
        ManagedAccount::refuse_reserved(name).map_err(|refusal| {
            tracing::warn!(
                username = name.as_str(),
                ?refusal,
                "refused to create an account with a reserved name"
            );
            AccountError::NotManaged {
                username: name.as_str().to_owned(),
                refusal,
            }
        })?;

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
        //
        // `--` before the name: `useradd` reads its options with `getopt_long`, which ends
        // option parsing at `--` on both families (shadow-utils is the same package on
        // Debian and RHEL), so a name can never be read as an option.
        //
        // A `useradd` killed at its deadline may have made the user or its home before it
        // was stopped. Only that case is probed: when the user exists afterwards, this call
        // created it (`user_exists` answered no a moment ago and the account lock is held),
        // so the same rollback as any later step removes it and a retry is not met with
        // `AlreadyExists`. Any other `useradd` failure is rolled back by shadow-utils itself.
        if let Err(failure) = self.expect_success(
            &self.distro.useradd_binary(),
            &[
                "--create-home",
                "--home-dir",
                &home,
                "--shell",
                self.distro.nologin_shell(),
                "--user-group",
                END_OF_OPTIONS,
                username,
            ],
            CallClass::Mutation,
        ) {
            if matches!(failure, AccountError::Bound(_))
                && self.host.user_exists(username).unwrap_or(false)
            {
                return Err(self.undo_creation(username, failure));
            }
            return Err(failure);
        }

        // Everything from here on is rolled back by `undo_creation` if it fails, because
        // `useradd` has already happened and nothing above this line will undo it. Without
        // the rollback a failure left the system user, its group and its home behind while
        // the panel's own record was never written — and the account could then never be
        // created, because the `user_exists` check, which follows the reserved-name refusal
        // at the top of this function, answered AlreadyExists for a name the panel does not
        // know (issue #75). Each step below
        // returns through `undo_creation` rather than `?` for that reason.
        if let Err(failure) = self.open_home_to_the_web_server(&home) {
            return Err(self.undo_creation(username, failure));
        }

        // apply_quota, not set_quota: the public one confirms the account exists, and
        // asking that one line after creating it is a second `id` per creation for an
        // answer already known.
        if let Err(failure) = self.apply_quota(username, quota_bytes) {
            return Err(self.undo_creation(username, failure));
        }

        let uid = match self.read_uid(username) {
            Ok(uid) => uid,
            Err(failure) => return Err(self.undo_creation(username, failure)),
        };

        // The account just made must be one this agent will agree to manage. A host
        // whose id floor moved between `useradd` and here, or whose `useradd` placed
        // the home elsewhere, would otherwise be left with a user that every later
        // operation refuses and that nothing can then delete.
        if let Err(failure) = self.resolve_managed(name) {
            return Err(self.undo_creation(username, failure));
        }

        Ok(CreatedAccount {
            home_directory: home,
            uid,
        })
    }

    /// Removes what [`AccountOperations::create`] had made when a later step of it fails,
    /// and answers with the failure that caused the rollback.
    ///
    /// `userdel --remove` is the whole of it, and that is a statement about WHEN this runs
    /// rather than a shortcut: a creation that has not returned yet has no PHP pool, no
    /// crontab, no SFTP login and no database, so none of the ordering that
    /// [`AccountOperations::delete_under_lock`] documents applies here. The home is at most
    /// a copy of `/etc/skel` and is never a mount point of anything.
    ///
    /// This is the one place a bare name reaches `userdel`, and it is sanctioned by what
    /// precedes it: the name passed [`ManagedAccount::refuse_reserved`], `user_exists`
    /// answered that no user bore it, and `useradd` made it one statement earlier, so
    /// the only user this can remove is the one this call created.
    ///
    /// The ORIGINAL failure is what comes back, never the rollback's own. A caller asked to
    /// create an account; the useful answer is why that could not be done, and a rollback
    /// error reported in its place would describe cleanup the caller never requested. When
    /// the rollback itself fails the account is left orphaned after all — rare, and the one
    /// case worth a log line, because the operator then has to remove the user by hand
    /// before any retry can succeed.
    fn undo_creation(&self, username: &str, failure: AccountError) -> AccountError {
        // `Mutation` and not `Bulk`: the account was created a moment ago, so its home holds
        // only the skeleton and the removal is short. A wedged `userdel` here must not hold
        // the account lock for the twelve hours of the `Bulk` class.
        if let Err(rollback_failure) = self.expect_success(
            &self.distro.userdel_binary(),
            &["--remove", END_OF_OPTIONS, username],
            CallClass::Mutation,
        ) {
            tracing::warn!(
                username,
                %rollback_failure,
                %failure,
                "an account creation failed and its rollback failed too, so the system user \
                 is left behind and must be removed by hand before a retry can succeed"
            );
        }

        failure
    }

    /// Suspends the account: its shell is locked and its password disabled.
    ///
    /// Idempotent: suspending a suspended account succeeds and changes nothing,
    /// because a billing system calls this on every overdue invoice and must not
    /// have to remember what it already did.
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotFound`] when the host has no such account.
    /// - [`AccountError::NotManaged`] when the user exists but is not a hosting
    ///   account (a reserved name, a system identity, a home elsewhere). Nothing
    ///   was run.
    /// - [`AccountError::HomeInspection`] when the password database cannot be
    ///   read; the operation refuses rather than guess.
    /// - [`AccountError::Bound`] when `usermod` outlived its deadline or its output ceiling and
    ///   was killed with its whole process group. Not a verdict: the step may have taken
    ///   effect on the host, so the caller reads the account again before it believes
    ///   either outcome.
    pub fn suspend(&self, name: &AccountName) -> Result<(), AccountError> {
        let account = self.resolve_managed(name)?;
        let username = account.name().as_str();

        // Both, not either: `--lock` prefixes the password hash so no password can
        // match, and the nologin shell stops any authentication method that does not
        // consult the password at all — an SSH key already in place, for instance.
        //
        // The account already has the nologin shell from creation; setting it again is
        // what makes suspension correct for an account whose shell was changed by hand.
        self.expect_success(
            &self.distro.usermod_binary(),
            &["--lock", END_OF_OPTIONS, username],
            CallClass::Mutation,
        )?;
        self.expect_success(
            &self.distro.usermod_binary(),
            &[
                "--shell",
                self.distro.nologin_shell(),
                END_OF_OPTIONS,
                username,
            ],
            CallClass::Mutation,
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
    /// - [`AccountError::NotFound`] when the host has no such account.
    /// - [`AccountError::NotManaged`] when the user exists but is not a hosting
    ///   account. Nothing was run.
    /// - [`AccountError::HomeInspection`] when the password database cannot be
    ///   read.
    /// - [`AccountError::CommandFailed`] when the shadow entry cannot be read,
    ///   when `usermod --unlock` refuses a genuinely locked account, or when
    ///   the shell cannot be set.
    /// - [`AccountError::UnreadableOutput`] when the shadow entry does not have
    ///   the shape `<name>:<password>:…` for this account.
    /// - [`AccountError::Bound`] when `getent` or `usermod` outlived its deadline or its output ceiling and
    ///   was killed with its whole process group. Not a verdict: the step may have taken
    ///   effect on the host, so the caller reads the account again before it believes
    ///   either outcome.
    pub fn unsuspend(&self, name: &AccountName) -> Result<(), AccountError> {
        let account = self.resolve_managed(name)?;
        let username = account.name().as_str();

        if self.stored_password(username)? == StoredPassword::Locked {
            self.expect_success(
                &self.distro.usermod_binary(),
                &["--unlock", END_OF_OPTIONS, username],
                CallClass::Mutation,
            )?;
        }

        self.expect_success(
            &self.distro.usermod_binary(),
            &[
                "--shell",
                self.distro.nologin_shell(),
                END_OF_OPTIONS,
                username,
            ],
            CallClass::Mutation,
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
    /// - [`AccountError::NotFound`] when the host has no such account.
    /// - [`AccountError::NotManaged`] when the user exists but is not a hosting
    ///   account. Resolved FIRST, before any of the three inspections, because
    ///   the state of a user that is not an account is exactly what this must
    ///   not disclose: answering for any name would make it an oracle on every
    ///   user of the host.
    /// - [`AccountError::HomeInspection`] when the password database cannot be
    ///   read.
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
    /// - [`AccountError::Bound`] when `crontab`, `passwd`, `getent` or `id`
    ///   outlived its deadline and was killed: neither of the two inspection
    ///   variants above, because the read was stopped and not refused.
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
        let account = self.resolve_managed(name)?;
        let username = account.name().as_str();
        let sites = inspect_account_sites(site_host, account.name())?;
        // Both conversions are written out rather than ridden on `?`: the
        // blanket `From<SftpError>` means "the deletion did not happen", which
        // is the wrong sentence for an observation, and cron has no blanket
        // conversion for the same reason.
        //
        // A bound is the exception to both: a read that was killed at its deadline is not a
        // statement that the cron table or the logins could not be inspected, so it comes
        // back as the bound itself.
        let cron =
            inspect_account_cron(cron_host, account.name()).map_err(|error| match error {
                CronError::Bound(bound) => AccountError::Bound(bound),
                other => AccountError::CronInspection {
                    reason: other.to_string(),
                },
            })?;
        let logins =
            account_logins(logins_host, self.distro, account.name()).map_err(
                |error| match error {
                    LoginsError::Bound(bound) => AccountError::Bound(bound),
                    other => AccountError::SftpInspection {
                        reason: other.to_string(),
                    },
                },
            )?;

        Ok(AccountSuspensionState {
            login_locked: self.login_locked(username)?,
            login_password: self.stored_password(username)?,
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
    /// - The account's PROCESSES are killed and verified gone
    ///   (`end_account_processes`) after the last step that could start one and
    ///   before `userdel`. `userdel` is no safety net here: its refusal of a user
    ///   with live processes needs `CAP_SYS_PTRACE`, which this agent lacks by
    ///   design, so it would exit 0 and free a uid a survivor still runs as, which
    ///   the next account created would inherit. A survivor after the bounded
    ///   rounds stops the deletion with [`AccountError::ProcessesRemain`].
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
    /// - [`AccountError::NotFound`] when the host has no such account.
    /// - [`AccountError::NotManaged`] when the user exists but is not a hosting
    ///   account (root, a daemon's user, a login that shares an account's uid).
    ///   Refused as the first step inside the lock, so no database was dropped,
    ///   no login or jail removed and no crontab touched.
    /// - [`AccountError::IdentityChanged`] when the account was resolved again
    ///   immediately before `userdel` and its user or group id differed from the
    ///   first resolution.
    /// - [`AccountError::HomeInspection`] when the password database cannot be
    ///   read.
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
    /// - [`AccountError::ProcessesRemain`] when processes of the account's uid
    ///   were still running after every kill round. Raised after the crontab and
    ///   pool sweeps and before `userdel`, so the account is still present and the
    ///   same deletion converges once the survivor has exited.
    /// - [`AccountError::CommandFailed`] when `pkill` answered neither "matched"
    ///   nor "nothing matched", or `crontab` refused to remove the
    ///   account's table for any reason other than there not being one.
    /// - [`AccountError::Busy`] when another operation for this account — a
    ///   backup, a restore, an SFTP login creation, a pool write or another
    ///   deletion — is already running on this host.
    /// - [`AccountError::Bound`] when a program outlived its deadline and was
    ///   killed with its process group. Raised by a step before `userdel`, it
    ///   leaves the account in place like the cases above; raised by `userdel`
    ///   itself it says nothing about whether the account is gone, and the caller
    ///   reads the account again before it believes either answer.
    ///
    /// In every one of those cases except a bound at `userdel` itself, `userdel`
    /// has NOT been run.
    pub fn delete(
        &self,
        php_host: &dyn PhpHost,
        db_host: &dyn DbHost,
        sftp_host: &dyn SftpHost,
        ftps_host: &dyn FtpsHost,
        name: &AccountName,
    ) -> Result<u64, AccountError> {
        // Taken FIRST, before a single thing is read, and held for the whole
        // sequence by living in this scope. The managed account is resolved INSIDE
        // it (`delete_under_lock`), so the resolution and what it authorises are
        // one critical section; the lock stays outer and never waits. The guard is
        // owned, so every return below — including every `?` — releases it. Nothing the sequence calls
        // takes it again (`crate::accounts::account_lock` names the callers), so
        // there is no nesting to deadlock on.
        let _guard = take_account_lock(name).ok_or_else(|| AccountError::Busy {
            username: name.as_str().to_owned(),
        })?;

        self.delete_under_lock(php_host, db_host, sftp_host, ftps_host, name)
    }

    /// The deletion sequence itself, with the account's lock ALREADY held.
    ///
    /// It resolves the [`ManagedAccount`] as its very first statement, so a
    /// refused user costs no database drop, no login teardown and no
    /// `crontab -u <name> -r`, and resolves it AGAIN after the last wait,
    /// comparing user and group ids with the first resolution before `userdel`.
    ///
    /// Between the second crontab sweep and that second resolution it ends every
    /// process of the account's uid (`end_account_processes`): a bounded kill and
    /// verify loop that refuses with [`AccountError::ProcessesRemain`], before
    /// `userdel`, when something survives.
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
        let account = self.resolve_managed(name)?;
        let bytes_freed = self.host.directory_size(account.home()).unwrap_or(0);

        drop_account_databases(db_host, account.name())?;
        remove_account_sftp(sftp_host, self.distro, account.name())?;

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
        remove_account_ftps(ftps_host, self.distro, account.name())?;

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
            let _crontab = cron_lock(account.name());
            self.remove_crontab(&account)?;
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
        remove_account_pools(php_host, self.distro, account.name())?;

        // A second sweep of the spool, now under the crontab lock as well, because
        // the first one is several process spawns old: the pool sweep above is a
        // `php-fpm -t` and a reload per pool, and a crontab installed meanwhile
        // would otherwise survive `userdel` and be handed whole to the next tenant
        // of this name. Scoped to the statement, for the reason the first is.
        {
            let _crontab = cron_lock(account.name());
            self.remove_crontab(&account)?;
        }

        // The account's processes end HERE: after the last step that can start
        // one (the pool sweep reloads php-fpm, which runs workers as this uid),
        // and before the identity re-check so the freshest fact precedes the
        // irreversible step. `userdel` is not a guard: its busy-user refusal needs
        // `CAP_SYS_PTRACE`, which this agent's bounding set lacks by design, so
        // it would free the uid with a process still running as it and the next
        // `useradd` would hand that process to a new tenant. The lock held here
        // is outer and never waits, so the rounds' pauses add no wait-for edge.
        end_account_processes(&self.host, self.distro, &account)?;

        // Resolved again, immediately before the irreversible step, and the ids are
        // compared with the first resolution. The lock above excludes every
        // operation of this agent that could have removed the account since the
        // first check, so what this catches is the one thing the lock cannot see: a
        // `userdel` an operator ran by hand, or a second agent binary, possibly
        // followed by a different user being given the same name. A validated
        // `AccountName` proves the name is well formed; it has never proved the
        // account is still there or still the same user, and the sequence between
        // the two resolutions is many process spawns long.
        let confirmed = self.resolve_managed(name)?;
        if confirmed.uid() != account.uid() || confirmed.gid() != account.gid() {
            tracing::warn!(
                username = name.as_str(),
                "refused to delete an account whose user was replaced while the deletion ran"
            );
            return Err(AccountError::IdentityChanged {
                username: name.as_str().to_owned(),
            });
        }

        self.expect_success(
            &self.distro.userdel_binary(),
            &["--remove", END_OF_OPTIONS, confirmed.name().as_str()],
            CallClass::Bulk,
        )?;

        Ok(bytes_freed)
    }

    /// Sets the account's filesystem quota, replacing whatever was in force.
    ///
    /// A quota of zero removes the limit, which is what `setquota` means by zero.
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotFound`] when the host has no such account.
    /// - [`AccountError::NotManaged`] when the user exists but is not a hosting
    ///   account; no quota was set on it.
    /// - [`AccountError::HomeInspection`] when the password database or
    ///   `/proc/mounts` cannot be read.
    /// - [`AccountError::CommandFailed`] when `setquota` refuses.
    /// - [`AccountError::Bound`] when `quotaon` or `setquota` outlived its deadline or its output ceiling and
    ///   was killed with its whole process group. Not a verdict: the step may have taken
    ///   effect on the host, so the caller reads the account again before it believes
    ///   either outcome.
    pub fn set_quota(&self, name: &AccountName, quota_bytes: u64) -> Result<(), AccountError> {
        let account = self.resolve_managed(name)?;
        self.apply_quota(account.name().as_str(), quota_bytes)
    }

    /// Reads how much the account currently uses, and against what quota.
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotFound`] when the host has no such account.
    /// - [`AccountError::NotManaged`] when the user exists but is not a hosting
    ///   account; nothing was measured.
    /// - [`AccountError::HomeInspection`] when the password database cannot be
    ///   read.
    /// - [`AccountError::UnreadableOutput`] when `quota` prints something this
    ///   agent cannot parse.
    /// - [`AccountError::Bound`] when `quota` or `quotaon` outlived its deadline or its output
    ///   ceiling and was killed with its whole process group. Nothing was changed.
    pub fn usage(&self, name: &AccountName) -> Result<AccountUsage, AccountError> {
        let account = self.resolve_managed(name)?;
        let username = account.name().as_str();
        let used_bytes = self.host.directory_size(account.home())?;

        // Enforceability is decided FIRST, and only when it says yes is
        // `quota -u -w`'s hard-limit line trusted for a byte figure — the
        // rewrite Task 4 exists for. A filesystem that cannot enforce
        // anything must be reported as such regardless of what a stale
        // quota line (left over from before a remount, say) still prints.
        let quota = match self.quota_enforceability()? {
            Ok(()) => {
                let outcome = self.host.run(
                    &self.distro.quota_binary(),
                    &["-u", "-w", END_OF_OPTIONS, username],
                    CallClass::Query,
                )?;
                match QuotaBlocks::parse_hard_limit(&outcome.stdout) {
                    Some(blocks) => QuotaState::Enforced(blocks),
                    None => QuotaState::EnforceableButUnset,
                }
            }
            Err(reason) => QuotaState::NotEnforceable(reason),
        };

        Ok(AccountUsage { used_bytes, quota })
    }

    /// Whether the filesystem holding hosting accounts' homes can currently
    /// enforce a per-user disk quota.
    ///
    /// `Ok(())` means it can; `Err` carries the specific reason it cannot.
    /// See [`crate::accounts::quota_enforceability`] for the two
    /// observations this reads and what they can and cannot see (most
    /// notably: nothing here can see NFS server-side enforcement).
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandUnavailable`] when `quotaon` cannot be
    /// started, and [`AccountError::HomeInspection`] when `/proc/mounts`
    /// cannot be read. Neither is the same as "not enforceable" — both mean
    /// this agent could not even ASK the question. Returns [`AccountError::Bound`] when
    /// `quotaon` outlived its deadline and was killed, which is likewise not an answer.
    fn quota_enforceability(&self) -> Result<Result<(), QuotaUnenforceableReason>, AccountError> {
        let mounts = self.host.read_mounts()?;
        // The MOUNT POINT holding the home root, not the home root itself. `quotaon -p /home`
        // answers "not found or has no quota enabled" whenever /home is a directory of the root
        // filesystem — the stock Ubuntu layout — so this check reported quotas unenforceable on
        // every such server, by a route that could never have reported anything else (issue #73).
        let Some(device) = quota_device_for(&mounts, AgentPaths::ACCOUNT_HOME_ROOT) else {
            return Ok(Err(QuotaUnenforceableReason::MountedWithoutQuotaAccounting));
        };
        // A query: `quotaon -p` only prints whether accounting is on, it switches nothing.
        let quotaon = self.host.run(
            &self.distro.quotaon_binary(),
            &["-p", END_OF_OPTIONS, device],
            CallClass::Query,
        )?;

        Ok(classify_quota_enforceability(
            &mounts,
            &quotaon.stdout,
            AgentPaths::ACCOUNT_HOME_ROOT,
        ))
    }

    /// Applies a quota to a user already known to exist and to be a hosting account.
    ///
    /// Takes a bare name and is private on purpose: its two callers are
    /// [`AccountOperations::create`], for a user it created one statement earlier
    /// after refusing reserved names, and `set_quota`, which holds the resolved
    /// proof.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`] when `setquota` refuses and
    /// [`AccountError::Bound`] when it outlived its deadline and was killed.
    fn apply_quota(&self, username: &str, quota_bytes: u64) -> Result<(), AccountError> {
        let blocks = QuotaBlocks::from_bytes(quota_bytes).as_argument();

        // Soft and hard limits are set to the same value, and inode limits to zero
        // (unlimited). A soft limit below the hard one only buys a grace period the
        // panel has no way to explain to the customer.
        // The MOUNT POINT, for the reason quota_enforceability gives: setquota takes a mount point
        // or a device and refuses a plain directory, so passing the home root failed on every
        // server whose /home is not its own filesystem — which is every default Ubuntu install.
        let mounts = self.host.read_mounts()?;
        let device = quota_device_for(&mounts, AgentPaths::ACCOUNT_HOME_ROOT).ok_or_else(|| {
            AccountError::HomeInspection {
                reason: format!(
                    "no block device in /proc/mounts holds {}",
                    AgentPaths::ACCOUNT_HOME_ROOT
                ),
            }
        })?;

        self.expect_success(
            &self.distro.setquota_binary(),
            &[
                "-u",
                END_OF_OPTIONS,
                username,
                &blocks,
                &blocks,
                "0",
                "0",
                device,
            ],
            CallClass::Mutation,
        )
    }

    /// Resolves `name` to the hosting account of that name on this host, or refuses.
    ///
    /// The ONLY way a lifecycle operation turns a name into something it may act
    /// on as root. It reads the local password database through the host seam
    /// (the file [`ManagedAccount`] decides from; the type itself reads none) and
    /// asks the type, which refuses a reserved name, an absent or ambiguous row, a
    /// home that is not `<home root>/<name>` and a system identity.
    ///
    /// The result is a fact about one moment: it is never kept, and an operation
    /// that waits on anything resolves again afterwards (see
    /// [`AccountOperations::delete`]).
    ///
    /// # Errors
    ///
    /// - [`AccountError::NotFound`] when the local database has no single row of
    ///   that name.
    /// - [`AccountError::NotManaged`] for every other refusal; the log line names
    ///   which, the returned error does not.
    /// - [`AccountError::HomeInspection`] when the database cannot be read, in
    ///   which case nothing is acted on.
    fn resolve_managed(&self, name: &AccountName) -> Result<ManagedAccount, AccountError> {
        let passwd = self
            .host
            .read_password_database(self.distro.passwd_database())?;

        match ManagedAccount::resolve(name.clone(), &passwd) {
            Ok(account) => Ok(account),
            Err(ManagedAccountError::NotFound) => Err(AccountError::NotFound {
                username: name.as_str().to_owned(),
            }),
            // `ManagedAccountError` is non-exhaustive, so any refusal added later
            // lands here and fails closed.
            Err(refusal) => {
                tracing::warn!(
                    username = name.as_str(),
                    ?refusal,
                    "refused to act on a user that is not a hosting account"
                );
                Err(AccountError::NotManaged {
                    username: name.as_str().to_owned(),
                    refusal,
                })
            }
        }
    }

    /// Reads a user's numeric uid.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::UnreadableOutput`] when `id` prints something other
    /// than a number, and [`AccountError::Bound`] when `id` outlived its deadline and was
    /// killed.
    fn read_uid(&self, username: &str) -> Result<u32, AccountError> {
        let program = self.distro.id_binary();
        let outcome = self.host.run(
            &program,
            &["-u", END_OF_OPTIONS, username],
            CallClass::Query,
        )?;
        outcome
            .stdout
            .trim()
            .parse::<u32>()
            .map_err(|_| AccountError::UnreadableOutput {
                program: program.to_string(),
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
    /// and measured as 2 on both families): an account `resolve_managed` has
    /// just resolved and which nevertheless has no shadow entry is a host in a
    /// state this agent will not guess about. And
    /// [`AccountError::UnreadableOutput`] when the first line is not
    /// `<name>:<password>:…` for this account.
    fn stored_password(&self, username: &str) -> Result<StoredPassword, AccountError> {
        let program = self.distro.getent_binary();
        let outcome = self.host.run(
            &program,
            &[END_OF_OPTIONS, SHADOW_DATABASE, username],
            CallClass::Query,
        )?;
        if outcome.status != 0 {
            return Err(AccountError::command_failed(program.as_str(), &outcome));
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
                program: program.to_string(),
            }),
        }
    }

    /// Reports whether the account's password is locked, from `passwd -S`.
    ///
    /// Reads the first line of the status output: the first field must be the
    /// account name and the second is the state, which starts with `L` when the
    /// login is locked.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::Bound`] when `passwd` outlived its deadline or its
    /// output ceiling, the host's error when it cannot be started, a command
    /// failure when it exits non-zero, and [`AccountError::UnreadableOutput`] when
    /// the first line does not name the account and a state.
    fn login_locked(&self, username: &str) -> Result<bool, AccountError> {
        let program = self.distro.passwd_binary();
        // No `--` here, unlike every other call in this file: `passwd` is shadow-utils' on
        // Debian and a `popt` program on RHEL, and only the first is known to end option
        // parsing at `--` (checked on both polygon images before it is added).
        let outcome = self.host.run(
            &program,
            &[PASSWORD_STATUS_ARGUMENT, username],
            CallClass::Query,
        )?;
        if outcome.status != 0 {
            return Err(AccountError::command_failed(program.as_str(), &outcome));
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
                program: program.to_string(),
            }),
        }
    }

    /// Removes the account's crontab from the host's cron spool.
    ///
    /// `crontab -u <account> -r`, which is the only correct way to write that
    /// spool. It takes the resolved [`ManagedAccount`] and not a name, because
    /// with a bare name this call was `crontab -u root -r` for a request that
    /// named root, which removes root's whole table, foreign lines included.
    ///
    /// Going through `crontab` is the only correct way to write that spool:
    /// where it lives, what owns it and how the daemon learns it changed are
    /// `crontab(1)`'s business on each family, and unlinking the file directly
    /// gets one of those wrong somewhere.
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
    /// this runs while the account still exists — `resolve_managed` has already
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
    /// while the account is still there to try again, and [`AccountError::Bound`] when
    /// `crontab` outlived its deadline and was killed.
    fn remove_crontab(&self, account: &ManagedAccount) -> Result<(), AccountError> {
        let program = self.distro.crontab_binary();
        // No `--`: `-r` is the last argument and no operand follows it, so there is nothing for
        // a guard to protect.
        let outcome = self.host.run(
            &program,
            &["-u", account.name().as_str(), "-r"],
            CallClass::Mutation,
        )?;
        if outcome.status == 0 || outcome.stderr.contains(NO_CRONTAB_MARKER) {
            return Ok(());
        }

        Err(AccountError::command_failed(program.as_str(), &outcome))
    }

    /// Runs a program and turns a non-zero exit into an error.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandFailed`], which names the program and its
    /// exit status. The tool's own sentence does not travel in the error — it is
    /// written to the agent's log by `AccountError::command_failed`, for the
    /// reason that variant sets out, or [`AccountError::Bound`] when the program outlived
    /// the deadline of `class` and was killed with its whole process group.
    fn expect_success(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<(), AccountError> {
        let outcome = self.host.run(program, arguments, class)?;
        if outcome.status == 0 {
            return Ok(());
        }

        Err(AccountError::command_failed(program.as_str(), &outcome))
    }

    /// Reports, or repairs, every hosting account whose home an earlier build
    /// left ungrouped for the web server.
    ///
    /// # The defect this exists for
    ///
    /// Before [`AccountOperations::create`] called
    /// `open_home_to_the_web_server`, an account's home
    /// was left `0750 <account>:<account>` — a mode and an owning group the
    /// web server's own user is in no group that satisfies. Every document
    /// root this agent creates is inside a home, so a site under such a home
    /// cannot be served: nginx logs `stat() … failed (13: Permission
    /// denied)` and answers every request with a refusal the panel explains
    /// nowhere. That fix is forward-only — it runs once, at creation, for the
    /// reason `open_home_to_the_web_server`'s own doc
    /// comment gives — so an account created before it shipped keeps the
    /// group its home was born with. This is the repair for those accounts.
    ///
    /// # The predicate: what makes a home need repair
    ///
    /// A user is a candidate at all only when it is a hosting account by
    /// [`ManagedAccount`]'s decision: a reserved name, a user with a system
    /// identity (and the right home) and a name with no single passwd row are not
    /// examined, and their homes are never re-grouped, whatever the web server's
    /// group is. A user refused for its home, whatever its ids, stays a candidate, so that it is reported by
    /// condition 1 below instead of vanishing from the report.
    ///
    /// A hosting account's home is repaired only when ALL of these hold, and
    /// it is refused — never guessed at — when any of them does not:
    ///
    /// 1. the account's OWN passwd row records its home as exactly
    ///    `<home root>/<account>`, the one path this agent has ever created a
    ///    home at — [`HomeGroupRepairRefusal::HomeNotAtExpectedPath`] otherwise;
    /// 2. that path exists — [`HomeGroupRepairRefusal::HomeMissing`] otherwise;
    /// 3. it is not a symlink — [`HomeGroupRepairRefusal::Symlink`] otherwise,
    ///    checked with `lstat` and never followed;
    /// 4. it is a directory — [`HomeGroupRepairRefusal::NotADirectory`]
    ///    otherwise;
    /// 5. it sits on the SAME filesystem as the account home root itself —
    ///    [`HomeGroupRepairRefusal::DifferentMount`] otherwise;
    /// 6. it is owned by the account's OWN uid, read from the same passwd row
    ///    as its home — [`HomeGroupRepairRefusal::OwnerMismatch`] otherwise.
    ///
    /// Only then is the home's current group compared against the web
    /// server's group: already equal reports as already correct, anything
    /// else is what gets `chgrp`'d — or would, in a report-only pass.
    ///
    /// **It cannot mistake an already-correct home for a broken one**: a home
    /// [`AccountOperations::create`] made today is already group-owned by the
    /// web server's group, so it satisfies every condition above and is
    /// simply reported as already correct. A clean host reports every account
    /// this way and sends no `chgrp` at all; so does a second run over a host
    /// this repair already fixed.
    ///
    /// # What this deliberately does not touch
    ///
    /// **The mode.** The defect this repairs is a wrong GROUP, not a wrong
    /// mode — `useradd --create-home` already leaves every home `0750`, which
    /// is exactly the mode `open_home_to_the_web_server`
    /// restates for a freshly created one. A pre-fix home therefore already
    /// carries the mode that lets the web server's group traverse it once it
    /// HOLDS that group; only the group column was ever wrong. Widening the
    /// mode here would be a change with no accompanying defect, on every
    /// customer's home on the host, from a repair that is supposed to touch
    /// one column of one directory. If an operator has since narrowed a
    /// home's mode by hand, that is the account's own choice — the same
    /// reason [`AccountOperations::create`] never re-applies itself to a
    /// pre-existing user — and this repair does not override it.
    ///
    /// # Errors
    ///
    /// - [`AccountError::HomeInspection`] when the password database or a
    ///   home's own metadata cannot be read at all — never confused with "the
    ///   home does not exist", which is [`HomeGroupRepairRefusal::HomeMissing`]
    ///   instead of an error.
    /// - [`AccountError::CommandFailed`] when `getent` cannot resolve the web
    ///   server's group at all — most plausibly because no web server is
    ///   installed, in which case there is nothing to re-group accounts INTO
    ///   and the whole pass refuses up front — or when `chgrp` itself refuses.
    /// - [`AccountError::UnreadableOutput`] when `getent`'s row for the web
    ///   server's group is not the shape this agent knows how to read.
    /// - [`AccountError::Bound`] when `getent` or `chgrp` outlived its deadline or its output ceiling and
    ///   was killed with its whole process group. Not a verdict: the step may have taken
    ///   effect on the host, so the caller reads the account again before it believes
    ///   either outcome.
    pub fn repair_home_groups(
        &self,
        report_only: bool,
    ) -> Result<HomeGroupRepairReport, AccountError> {
        let passwd = self
            .host
            .read_password_database(self.distro.passwd_database())?;
        let home_root_metadata = self
            .host
            .home_metadata(AgentPaths::ACCOUNT_HOME_ROOT)?
            .ok_or_else(|| AccountError::HomeInspection {
                reason: format!("{} does not exist", AgentPaths::ACCOUNT_HOME_ROOT),
            })?;
        let web_server_gid = self.web_server_group_gid()?;

        // Only users the type admits are candidates, plus the ones it refuses for their
        // home (whatever their ids, since the type checks the home first), which the predicate below reports as `HomeNotAtExpectedPath`
        // exactly as before. A reserved name (the operator's own login among them), a
        // system identity and a name that has no single row are not hosting accounts, so
        // they are not examined and no group of theirs is ever changed. The type is asked
        // per row against the rows parsed once here: the text is no longer re-parsed per
        // row and the id floors are read once for the whole pass.
        let rows = system_accounts(&passwd);
        let mut accounts: Vec<(AccountName, String, u32)> = Vec::new();
        for (account, answer) in ManagedAccount::resolve_each(&rows) {
            match answer {
                Ok(_) | Err(ManagedAccountError::WrongHome) => {
                    if let Some(row) = rows.iter().find(|row| row.name == account.as_str()) {
                        accounts.push((account, row.home.clone(), row.uid));
                    }
                }
                Err(_) => {}
            }
        }
        // Sorted and deduplicated for the reason `get_accounts_disk_usage`
        // sorts and deduplicates its own passwd rows: two calls against an
        // unchanged host answer in the same order. A name carried twice by a
        // hand-edited file never reaches here, because the type refuses an
        // ambiguous row, so the deduplication is a second, harmless guard.
        accounts.sort_by(|left, right| left.0.as_str().cmp(right.0.as_str()));
        accounts.dedup_by(|left, right| left.0 == right.0);

        let mut report = HomeGroupRepairReport {
            examined: accounts.len(),
            ..HomeGroupRepairReport::default()
        };

        for (account, recorded_home, uid) in accounts {
            let expected_home = Self::home_directory(&account);
            if recorded_home != expected_home {
                self.refuse_home(
                    &mut report,
                    account,
                    recorded_home,
                    HomeGroupRepairRefusal::HomeNotAtExpectedPath,
                );
                continue;
            }

            let Some(metadata) = self.host.home_metadata(&expected_home)? else {
                self.refuse_home(
                    &mut report,
                    account,
                    expected_home,
                    HomeGroupRepairRefusal::HomeMissing,
                );
                continue;
            };

            if metadata.is_symlink {
                self.refuse_home(
                    &mut report,
                    account,
                    expected_home,
                    HomeGroupRepairRefusal::Symlink,
                );
                continue;
            }

            if !metadata.is_directory {
                self.refuse_home(
                    &mut report,
                    account,
                    expected_home,
                    HomeGroupRepairRefusal::NotADirectory,
                );
                continue;
            }

            if metadata.device != home_root_metadata.device {
                self.refuse_home(
                    &mut report,
                    account,
                    expected_home,
                    HomeGroupRepairRefusal::DifferentMount,
                );
                continue;
            }

            if metadata.owner_uid != uid {
                self.refuse_home(
                    &mut report,
                    account,
                    expected_home,
                    HomeGroupRepairRefusal::OwnerMismatch,
                );
                continue;
            }

            if metadata.group_gid == web_server_gid {
                report.already_correct += 1;
                continue;
            }

            let repaired = RepairedHome {
                account,
                home: expected_home,
            };
            if report_only {
                report.would_repair.push(repaired);
                continue;
            }

            self.expect_success(
                &self.distro.chgrp_binary(),
                &[
                    "--no-dereference",
                    END_OF_OPTIONS,
                    self.distro.web_server_group(),
                    &repaired.home,
                ],
                CallClass::Mutation,
            )?;
            report.repaired.push(repaired);
        }

        Ok(report)
    }

    /// The numeric id of the web server's group, read with `getent group`.
    ///
    /// Asked fresh on every call rather than cached: the group is looked up
    /// once per repair pass, and a cached id would survive the group being
    /// removed and recreated with a different number, which is exactly the
    /// case [`crate::db`] and `agent-core`'s own id-resolution types refuse to
    /// let a caller cache around.
    ///
    /// # Errors
    ///
    /// [`AccountError::CommandFailed`] when `getent` exits non-zero — most
    /// plausibly because the group does not exist, i.e. no web server is
    /// installed — and [`AccountError::UnreadableOutput`] when its row is not
    /// `<name>:<password>:<gid>:…` or the gid field is not a number. Returns
    /// [`AccountError::Bound`] when `getent` outlived its deadline and was killed.
    fn web_server_group_gid(&self) -> Result<u32, AccountError> {
        let program = self.distro.getent_binary();
        let group = self.distro.web_server_group();
        let outcome = self.host.run(
            &program,
            &[END_OF_OPTIONS, GROUP_DATABASE, group],
            CallClass::Query,
        )?;
        if outcome.status != 0 {
            return Err(AccountError::command_failed(program.as_str(), &outcome));
        }

        let unreadable = || AccountError::UnreadableOutput {
            program: program.to_string(),
        };

        // The NAME is matched too, and not merely the gid field — the same
        // defence `stored_password` and `login_locked` take against a future
        // edit that drops the argument: `getent group` with no key lists
        // every group on the host, and matching only a field position would
        // read the first ROW'S gid as this one's.
        let mut fields = outcome.stdout.lines().next().unwrap_or_default().split(':');
        match (fields.clone().next(), fields.nth(GROUP_GID_FIELD)) {
            (Some(name), Some(field)) if name == group => field.parse().map_err(|_| unreadable()),
            _ => Err(unreadable()),
        }
    }

    /// Adds `account` to the report's refusals and says so in the log.
    ///
    /// The log line exists for the same reason `ops::db::repair_grants`'s own
    /// `refuse` writes one: a refusal is the answer for a home this repair
    /// cannot classify with certainty, and an operator reading the agent's own
    /// journal must be able to see that an account's home was left alone and
    /// why. An account name and a path are not secrets — both are already
    /// visible to any local user who can run `ls /home` — and neither is a
    /// credential.
    fn refuse_home(
        &self,
        report: &mut HomeGroupRepairReport,
        account: AccountName,
        home: String,
        reason: HomeGroupRepairRefusal,
    ) {
        tracing::warn!(
            account = account.as_str(),
            home = home.as_str(),
            "home group repair refused an account it cannot classify: {reason}"
        );
        report.refused.push(RefusedHome {
            account,
            home,
            reason,
        });
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
    /// noticed. Returns [`AccountError::Bound`] when either program outlived its deadline
    /// and was killed.
    fn open_home_to_the_web_server(&self, home: &str) -> Result<(), AccountError> {
        // `--no-dereference`, so a symlink standing where the home should be is
        // re-grouped as the link rather than followed to whatever it points at. Nothing
        // should be able to plant one — `useradd` created this directory a line ago and
        // the account has no shell — but the flag costs nothing, and the alternative is a
        // root process following a link it did not verify.
        self.expect_success(
            &self.distro.chgrp_binary(),
            &[
                "--no-dereference",
                END_OF_OPTIONS,
                self.distro.web_server_group(),
                home,
            ],
            CallClass::Mutation,
        )?;

        // Restated rather than assumed. `useradd` honours `HOME_MODE`/`UMASK` from
        // /etc/login.defs, so the mode a home is born with is a host setting an operator
        // can change; a home left world-readable by such a setting would undo the whole
        // point of the group above, and this is the line that says what the panel
        // requires. Ownership of the home itself is `useradd`'s and is untouched — the
        // account owns its own home; only the group changed.
        self.expect_success(
            &self.distro.chmod_binary(),
            &[END_OF_OPTIONS, "0750", home],
            CallClass::Mutation,
        )
    }
}

#[cfg(test)]
#[path = "../tests/accounts/account_operations_tests.rs"]
mod tests;
