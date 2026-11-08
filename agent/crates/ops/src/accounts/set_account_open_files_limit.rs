//! `SetAccountOpenFilesLimit`: the most files one session of an account may hold
//! open, held in the account's own `pam_limits` drop-in (issue #194).

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::open_files_limit::OpenFilesLimit;
use maran_templates::pam_limits::account_limits::AccountLimits;

use crate::accounts::{
    AccountError, AccountOperations, ResourceCeiling, SystemHost, take_account_lock,
};
use crate::safe_write::model::{ConfigFile, Validator};

/// The drop-in's mode: world-readable like every fragment the distributions
/// ship in the same directory, owned by root, written by nothing else. It holds
/// an account name and a figure, no secret.
const LIMITS_FILE_MODE: u32 = 0o644;

/// Holds the account's sessions to `limit` files open at once, or removes the
/// ceiling.
///
/// **What it reaches, stated rather than implied.** `pam_limits` applies the
/// drop-in to a process started inside a PAM session that loads it: an SSH or
/// SFTP login, `su`, a login shell. It does NOT reach the php-fpm workers of the
/// account's pools, which are children of the php-fpm master and not of any
/// session of the account, and it does not reach a cron job on a host whose cron
/// PAM stack does not load the module. The limit applies from the account's NEXT
/// session; a session already open keeps the one it started with. Whether
/// `pam_limits` is in each family's sshd stack is measured on the polygon
/// (`account_limits_on_a_real_host.rs`), not assumed here.
///
/// **How it is written.** [`Unlimited`](ResourceCeiling::Unlimited) removes
/// `<limits dir>/maran-<account>.conf`: an account with no ceiling carries no
/// file. A figure renders the file from the template and writes it through
/// `safe_write::write_unreloaded_config` (temporary file beside the target,
/// `fsync`, atomic rename, validation, rollback), with no reload because the
/// module re-reads the directory at every session. The name is built from the
/// validated [`AccountName`] and the one number is a validated
/// [`OpenFilesLimit`], so no newline, `*`, `@` or other caller text reaches a
/// line `pam_limits` reads as root (rules/security.md item 4).
///
/// **The validator.** `pam_limits` ships no checker for its fragments, so there
/// is no tool that parses the file the way the module will. What the file's
/// correctness does depend on outside its own text is that the user it names
/// resolves, and `id -u -- <account>` asks the host exactly that after the
/// rename. It does not verify the line's syntax and is not claimed to: the
/// syntax is pinned by the template's golden and the figure by its type.
///
/// **The lock.** The account's never-waiting lock is taken at entry, so this
/// write cannot interleave with the account's deletion, which removes the file
/// before `userdel` under the same lock. Without it a write landing after the
/// deletion's removal would leave a file naming a user that no longer exists,
/// handed to whoever is given the name next. Idempotent: the same request
/// renders the same bytes, and removing an absent file succeeds.
///
/// # Errors
///
/// - [`AccountError::Busy`] when another operation holds the account's lock.
/// - [`AccountError::NotFound`] when the host has no such account.
/// - [`AccountError::NotManaged`] when the user exists but is not a hosting
///   account. Nothing was written, so a drop-in can never be made for `root`.
/// - [`AccountError::HomeInspection`] when the password database cannot be
///   read; nothing is acted on.
/// - [`AccountError::LimitsFileFailed`] when the file could not be rendered,
///   written, validated or removed; a refused write restores the previous file.
/// - [`AccountError::Bound`] when the validator outran its deadline.
pub fn set_account_open_files_limit<H: SystemHost>(
    operations: &AccountOperations<H>,
    name: &AccountName,
    limit: ResourceCeiling<OpenFilesLimit>,
) -> Result<(), AccountError> {
    let _guard = take_account_lock(name).ok_or_else(|| AccountError::Busy {
        username: name.as_str().to_owned(),
    })?;

    set_account_open_files_limit_under_lock(operations, name, limit)
}

/// The write itself, with the account's lock ALREADY held.
///
/// Split from [`set_account_open_files_limit`] for the reason
/// `set_sftp_authorized_keys` is split: the lock is a process-wide static that
/// never waits, so a suite driving the public entry for one account name on the
/// harness's threads would refuse itself.
///
/// # Errors
///
/// Every variant [`set_account_open_files_limit`] documents except
/// [`AccountError::Busy`].
pub(crate) fn set_account_open_files_limit_under_lock<H: SystemHost>(
    operations: &AccountOperations<H>,
    name: &AccountName,
    limit: ResourceCeiling<OpenFilesLimit>,
) -> Result<(), AccountError> {
    let account = operations.resolve_managed(name)?;
    let target = AgentPaths::account_limits_file(account.name());

    let ResourceCeiling::Limited(limit) = limit else {
        return operations.host().remove_limits_file(&target);
    };

    let contents = AccountLimits {
        account: account.name().as_str(),
        open_files: limit.value(),
    }
    .render_config()
    .map_err(|_| AccountError::LimitsFileFailed)?;

    let id = operations.distro().id_binary();
    let validator = Validator {
        program: &id,
        arguments: &["-u", END_OF_OPTIONS, account.name().as_str()],
        // Reads the name services and writes nothing: a pure validation.
        class: CallClass::Validate,
    };

    operations.host().write_limits_file(
        &ConfigFile {
            target: &target,
            contents: &contents,
            mode: LIMITS_FILE_MODE,
        },
        &validator,
    )
}

#[cfg(test)]
#[path = "../tests/accounts/set_account_open_files_limit_tests.rs"]
mod tests;
