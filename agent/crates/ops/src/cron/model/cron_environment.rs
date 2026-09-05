//! One environment assignment the customer set for their own cron entries.

use maran_agent_core::validation::system::env_var_name::EnvVarName;
use maran_agent_core::validation::system::env_var_value::EnvVarValue;

/// A `NAME=VALUE` line the panel writes into the account's crontab preamble.
///
/// Both halves are validated types and neither is a `String`, which is what
/// makes writing this into a line-oriented file safe by construction rather
/// than by an escape at the render site (rules/rust.md "Validation first").
/// [`EnvVarName`] refuses `MAILTO` and `SHELL` — the two the agent writes
/// itself, one an outbound mail relay and the other the interpreter under every
/// entry — and [`EnvVarValue`] refuses the `%` that cron rewrites into a
/// newline, along with the surrounding whitespace and wrapping quotes cron
/// would silently strip.
///
/// A named pair rather than a `(EnvVarName, EnvVarValue)` tuple, because a
/// tuple's halves are told apart by position at every call site and this one is
/// read back out of a file, edited, and written again.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CronEnvironment {
    /// The variable's name, as it is written left of the `=`.
    pub name: EnvVarName,
    /// The variable's value, as it is written right of the `=`.
    pub value: EnvVarValue,
}
