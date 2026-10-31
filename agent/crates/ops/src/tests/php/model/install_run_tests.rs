//! Tests for [`InstallRun::parse`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::InstallRun;

/// A run id the service manager could have printed.
const ID: &str = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";

#[test]
fn a_finished_run_names_its_id_and_the_status_its_main_process_exited_with() {
    let run = InstallRun::parse(&format!("InvocationID={ID}\nExecMainStatus=100\n")).unwrap();

    assert_eq!(run.invocation_id(), ID);
    assert_eq!(run.exit_status(), 100);
}

#[test]
fn the_properties_are_read_in_whatever_order_they_are_printed() {
    let run = InstallRun::parse(&format!("ExecMainStatus=1\nInvocationID={ID}\n")).unwrap();

    assert_eq!(run.exit_status(), 1);
}

#[test]
fn a_unit_that_never_ran_names_no_run() {
    // What the service manager prints for an unknown unit too.
    assert_eq!(InstallRun::parse("InvocationID=\nExecMainStatus=0\n"), None);
}

#[test]
fn an_id_that_is_not_lowercase_hexadecimal_is_refused() {
    for id in [
        "0F1E2D3C4B5A69788796A5B4C3D2E1F0",
        "0f1e2d3c4b5a69788796a5b4c3d2e1f",
        "0f1e2d3c4b5a69788796a5b4c3d2e1f0 --since=x",
        "0f1e2d3c4b5a69788796a5b4c3d2e1fg",
    ] {
        assert_eq!(
            InstallRun::parse(&format!("InvocationID={id}\nExecMainStatus=100\n")),
            None,
            "{id}"
        );
    }
}

#[test]
fn a_missing_or_non_numeric_status_names_no_run() {
    assert_eq!(InstallRun::parse(&format!("InvocationID={ID}\n")), None);
    assert_eq!(
        InstallRun::parse(&format!("InvocationID={ID}\nExecMainStatus=x\n")),
        None
    );
}
