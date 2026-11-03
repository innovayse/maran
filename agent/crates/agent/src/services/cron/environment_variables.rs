//! Turning the environment assignments an account carries into the wire's payload.

use maran_ops::cron::CronEnvironment;

use crate::proto::CronEnvironmentVariable;

/// Builds the wire list for `GetCronEnvironment`: one variable per assignment,
/// in the order the crontab holds them.
///
/// The name and value are the validated types' own text, copied as they are:
/// nothing is escaped, trimmed or reordered on the way out.
#[must_use]
pub fn environment_variables(environment: Vec<CronEnvironment>) -> Vec<CronEnvironmentVariable> {
    environment
        .into_iter()
        .map(|variable| CronEnvironmentVariable {
            name: variable.name.as_str().to_owned(),
            value: variable.value.as_str().to_owned(),
        })
        .collect()
}

#[cfg(test)]
#[path = "../../tests/services/cron/environment_variables_tests.rs"]
mod tests;
