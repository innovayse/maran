//! Tests for the list `GetCronEnvironment` answers with.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::env_var_name::EnvVarName;
use maran_agent_core::validation::system::env_var_value::EnvVarValue;
use maran_ops::cron::CronEnvironment;

use super::environment_variables;

/// An assignment built from plain text.
fn assignment(name: &str, value: &str) -> CronEnvironment {
    CronEnvironment {
        name: EnvVarName::parse(name).unwrap(),
        value: EnvVarValue::parse(value).unwrap(),
    }
}

#[test]
fn an_account_with_no_assignments_reports_an_empty_list() {
    assert!(environment_variables(Vec::new()).is_empty());
}

#[test]
fn assignments_travel_in_the_order_the_crontab_holds_them() {
    let wire = environment_variables(vec![
        assignment("REPORT_TO", "ops"),
        assignment("LANG", "C"),
    ]);

    assert_eq!(wire.len(), 2);
    assert_eq!(wire[0].name, "REPORT_TO");
    assert_eq!(wire[0].value, "ops");
    assert_eq!(wire[1].name, "LANG");
    assert_eq!(wire[1].value, "C");
}
