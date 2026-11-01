//! `SetAccountResourceLimits`: the CPU, memory and process ceilings of one
//! account's systemd slice.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::{AccountError, AccountOperations, AccountResourceLimits, SystemHost};

/// Holds the account's `user-<uid>.slice` to `limits`, replacing whatever
/// ceilings it held before.
///
/// **Which processes this reaches.** systemd places every login session of a
/// user — an SSH or SFTP session, anything `pam_systemd` registers — under
/// `user-<uid>.slice`, and the kernel enforces the slice's cgroup v2 controllers
/// (`cpu.max`, `memory.max`, `pids.max`) on every process inside it. A process
/// that never enters a session is NOT inside it: the php-fpm workers of the
/// account's pools run under the php-fpm master's own service cgroup, and on
/// the Debian family cron's PAM stack registers no session. Both stay outside
/// these ceilings until they are moved into the slice, which this rpc does not
/// do and does not claim to (see the issue #133 threat note).
///
/// **How it is written.** One `systemctl set-property` with all three
/// assignments, without `--runtime`, so systemd both applies them to the live
/// cgroup and persists them as drop-ins that survive a reboot. The slice does
/// not have to be active: systemd records the properties and applies them when
/// the account's first session creates it. Every assignment is rendered from a
/// validated integer or a fixed "no ceiling" spelling, the unit name from the
/// uid the password database gave, and the program from the distribution
/// adapter — no caller text reaches the argv.
///
/// **Idempotent**: the call replaces all three properties every time, so a
/// repeat converges, and a uid recycled from a deleted account is given a full
/// set rather than inheriting a property its previous owner left behind.
///
/// Holds no lock: it is one call whose effect systemd applies atomically, and
/// it reads nothing it then writes back.
///
/// # Errors
///
/// - [`AccountError::NotFound`] when the host has no such account.
/// - [`AccountError::NotManaged`] when the user exists but is not a hosting
///   account (a reserved name, a system identity, a home elsewhere). Nothing
///   was run, so `root`'s slice can never be capped through here.
/// - [`AccountError::HomeInspection`] when the password database cannot be
///   read; nothing is acted on.
/// - [`AccountError::CommandUnavailable`] when the service manager cannot be
///   started.
/// - [`AccountError::CommandFailed`] when `systemctl` refuses the assignments.
/// - [`AccountError::Bound`] when `systemctl` outlived its deadline or its output
///   ceiling and was killed. Not a verdict: the properties may have been
///   applied, and repeating the call converges either way.
pub fn set_account_resource_limits<H: SystemHost>(
    operations: &AccountOperations<H>,
    name: &AccountName,
    limits: &AccountResourceLimits,
) -> Result<(), AccountError> {
    let account = operations.resolve_managed(name)?;

    let unit = slice_unit(account.uid());
    let [cpu, memory, tasks] = limits.property_assignments();
    let program = operations.distro().service_manager();
    let outcome = operations.host().run(
        &program,
        &["set-property", END_OF_OPTIONS, &unit, &cpu, &memory, &tasks],
        CallClass::Mutation,
    )?;

    if outcome.status == 0 {
        return Ok(());
    }

    Err(AccountError::command_failed(program.as_str(), &outcome))
}

/// The name of the slice systemd places a user's sessions under.
///
/// `user-<uid>.slice` is systemd-logind's own fixed naming on every family this
/// agent supports; it is built from the numeric uid the password database
/// answered, never from the account name, so nothing a caller spelled reaches
/// the unit name.
fn slice_unit(uid: u32) -> String {
    format!("user-{uid}.slice")
}

#[cfg(test)]
#[path = "../tests/accounts/set_account_resource_limits_tests.rs"]
mod tests;
