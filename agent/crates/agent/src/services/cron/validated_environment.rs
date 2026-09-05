//! Turning a `SetCronEnvironment` request into the assignments cron writes.

use maran_agent_core::validation::system::env_var_name::EnvVarName;
use maran_agent_core::validation::system::env_var_value::EnvVarValue;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::cron::CronEnvironment;

use crate::proto::AgentError;
use crate::proto::CronEnvironmentVariable;
use crate::services::cron::validated_account::validated_account;
use crate::services::sites::invalid_input::invalid_input;

/// Builds the account and the complete set of assignments `SetCronEnvironment`
/// installs.
///
/// The set is validated WHOLE before any of it is used: one bad name in a list
/// of ten refuses the request rather than installing the other nine, because
/// this rpc replaces the managed assignments entirely and a partially applied
/// replacement is a crontab nobody asked for.
///
/// An empty list is a legitimate request — it is how every managed assignment
/// is cleared — so it is not refused here.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for a
/// name outside the environment-variable grammar or on the reserved list
/// (`MAILTO`, which is an outbound relay through the host's mail transfer
/// agent, and `SHELL`, which changes the interpreter under every managed
/// entry — the agent emits both itself), and for a value carrying a control
/// character, which a line-oriented crontab would read as the start of another
/// line.
pub fn validated_environment(
    account_username: &str,
    variables: &[CronEnvironmentVariable],
) -> Result<(AccountName, Vec<CronEnvironment>), AgentError> {
    let account = validated_account(account_username)?;

    let mut environment = Vec::with_capacity(variables.len());
    for variable in variables {
        let name =
            EnvVarName::parse(&variable.name).map_err(|error| invalid_input(error.to_string()))?;
        let value = EnvVarValue::parse(&variable.value)
            .map_err(|error| invalid_input(error.to_string()))?;

        environment.push(CronEnvironment { name, value });
    }

    Ok((account, environment))
}
