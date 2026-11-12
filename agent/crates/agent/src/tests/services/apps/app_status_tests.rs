//! Tests for the mapping of app failures onto wire codes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::apps::AppsOpError;
use maran_ops::packages::PackageError;
use maran_ops::php::PhpOpError;

use super::to_agent_error;
use crate::proto::ErrorCode;

fn code(error: AppsOpError) -> i32 {
    to_agent_error(&error).code
}

#[test]
fn a_removal_that_would_take_other_packages_is_in_use_and_one_not_checked_is_a_system_failure() {
    assert_eq!(
        code(AppsOpError::RemovalWouldRemoveOthers {
            app: "apache".into(),
            packages: "python3-certbot-apache".into()
        }),
        ErrorCode::AppInUse as i32
    );
    // Inverse control: an unchecked removal is the host's fault, not a refusal about use.
    assert_eq!(
        code(AppsOpError::RemovalNotSimulated {
            app: "apache".into(),
            reason: "no plan".into()
        }),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn the_app_store_refusals_have_their_own_codes() {
    assert_eq!(
        code(AppsOpError::NotInstalled {
            app: "apache".into()
        }),
        ErrorCode::AppNotInstalled as i32
    );
    assert_eq!(
        code(AppsOpError::InUse {
            app: "apache".into(),
            users: "1 site".into()
        }),
        ErrorCode::AppInUse as i32
    );
    assert_eq!(
        code(AppsOpError::WebServerAlreadyInstalled {
            requested: "nginx".into(),
            installed: "apache".into()
        }),
        ErrorCode::WebServerAlreadyInstalled as i32
    );
    assert_eq!(
        code(AppsOpError::NodeMajorAlreadyInstalled {
            requested: "nodejs-22".into(),
            installed: "16".into()
        }),
        ErrorCode::NodeMajorAlreadyInstalled as i32
    );
    // Inverse control: the Node.js refusal is its own code, not the web server's.
    assert_ne!(
        ErrorCode::NodeMajorAlreadyInstalled as i32,
        ErrorCode::WebServerAlreadyInstalled as i32
    );
}

#[test]
fn a_request_the_agent_will_not_carry_out_is_invalid_input() {
    assert_eq!(
        code(AppsOpError::UnsupportedApp {
            app: "php-9.9".into()
        }),
        ErrorCode::InvalidInput as i32
    );
    assert_eq!(
        code(AppsOpError::ActionRefused {
            app: "nginx".into(),
            action: "stopped",
            reason: "front"
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_service_manager_failure_is_a_system_failure_carrying_its_words() {
    let error = to_agent_error(&AppsOpError::ServiceManager {
        stderr: "Job failed".into(),
    });

    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    assert_eq!(error.tool_output, "Job failed");
}

#[test]
fn package_failures_travel_as_the_codes_they_travel_as_for_php() {
    assert_eq!(
        code(AppsOpError::Package(PackageError::SourceRefused {
            stderr: String::new()
        })),
        ErrorCode::PackageSourceRefused as i32
    );
    assert_eq!(
        code(AppsOpError::Package(PackageError::SourceUnreachable {
            stderr: String::new()
        })),
        ErrorCode::PackageSourceUnreachable as i32
    );
    assert_eq!(
        code(AppsOpError::Package(PackageError::NotAvailable {
            package: "nodejs20".into(),
            stderr: String::new()
        })),
        ErrorCode::PackageNotAvailable as i32
    );
    assert_eq!(
        code(AppsOpError::Php(PhpOpError::PackageManager {
            stderr: String::new()
        })),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn a_bound_is_never_a_refusal_of_the_request() {
    let error = to_agent_error(&AppsOpError::StillAbsent {
        app: "apache".into(),
        detail: String::new(),
    });

    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
}

#[test]
fn each_node_js_refusal_an_operator_must_act_on_has_its_own_code_and_the_command_in_its_words() {
    let unmanaged = to_agent_error(&AppsOpError::UnmanagedNodeInstalled {
        requested: "nodejs-20".into(),
        installed: "16".into(),
        command: "dnf remove nodejs".into(),
    });
    let enabled = to_agent_error(&AppsOpError::NodeStreamEnabledByOperator {
        requested: "nodejs-20".into(),
        enabled: "22".into(),
        command: "dnf module reset nodejs".into(),
    });

    assert_eq!(unmanaged.code, ErrorCode::NodeUnmanagedInstall as i32);
    assert!(
        unmanaged.message.contains("dnf remove nodejs"),
        "{}",
        unmanaged.message
    );
    assert_eq!(enabled.code, ErrorCode::NodeStreamEnabledByOperator as i32);
    assert!(
        enabled.message.contains("dnf module reset nodejs"),
        "{}",
        enabled.message
    );
    // Inverse control: another major put there by a module stream keeps the code it had, and
    // none of the four shares a number.
    assert_eq!(
        code(AppsOpError::NodeMajorAlreadyInstalled {
            requested: "nodejs-22".into(),
            installed: "20".into()
        }),
        ErrorCode::NodeMajorAlreadyInstalled as i32
    );
    let mut codes = vec![
        ErrorCode::NodeMajorAlreadyInstalled as i32,
        unmanaged.code,
        enabled.code,
        code(AppsOpError::NodeInterpreterUnreadable {
            path: "/usr/bin/node".into(),
        }),
        code(AppsOpError::NodejsOperationInProgress),
    ];
    codes.sort_unstable();
    codes.dedup();
    assert_eq!(codes.len(), 5);
}

#[test]
fn an_unreadable_interpreter_and_a_busy_module_are_not_system_failures() {
    assert_eq!(
        code(AppsOpError::NodeInterpreterUnreadable {
            path: "/usr/bin/node".into()
        }),
        ErrorCode::NodeInterpreterUnreadable as i32
    );
    assert_eq!(
        code(AppsOpError::NodejsOperationInProgress),
        ErrorCode::NodeOperationInProgress as i32
    );
    // Inverse control: a bound that ran out and a host that could not be read stay the host's fault.
    assert_eq!(
        code(AppsOpError::HostUnreadable {
            path: "/etc".into(),
            reason: String::new()
        }),
        ErrorCode::SystemFailure as i32
    );
}
