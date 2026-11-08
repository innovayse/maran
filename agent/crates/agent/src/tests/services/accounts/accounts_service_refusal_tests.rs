//! Every account rpc refuses a reserved or unmanaged name with the typed
//! refusal and runs nothing; a hosting account is still suspended over the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;

use tonic::Request;

use super::fixture::blocking_host::FAST_ACCOUNT;
use super::fixture::{ServiceUnderTest, service_over, standard_passwd};
use crate::proto::accounts_service_server::AccountsService;
use crate::proto::{
    AgentError, CreateAccountRequest, DeleteAccountRequest, ErrorCode,
    GetAccountSuspensionStateRequest, GetAccountUsageRequest, ResourceCeiling,
    ResourceCeilingUnlimited, SetAccountOpenFilesLimitRequest, SetAccountQuotaRequest,
    SetAccountResourceLimitsRequest, SuspendAccountRequest, UnsuspendAccountRequest,
    create_account_response, delete_account_response, get_account_suspension_state_response,
    get_account_usage_response, resource_ceiling, set_account_open_files_limit_response,
    set_account_quota_response, set_account_resource_limits_response, suspend_account_response,
    unsuspend_account_response,
};

/// The refusal inside an rpc's `oneof`, when it answered with one.
fn refusal_of<T>(
    result: Option<T>,
    pick: impl FnOnce(T) -> Option<AgentError>,
) -> Option<AgentError> {
    result.and_then(pick)
}

/// A wire ceiling stating "no ceiling".
fn unlimited_ceiling() -> ResourceCeiling {
    ResourceCeiling {
        ceiling: Some(resource_ceiling::Ceiling::Unlimited(
            ResourceCeilingUnlimited {},
        )),
    }
}

/// Sends every rpc that acts on an account to `service`, naming `username`,
/// and returns each rpc's refusal (`None` when it answered with an ok).
async fn every_rpc_for(
    service: &Arc<ServiceUnderTest>,
    username: &str,
) -> Vec<(&'static str, Option<AgentError>)> {
    let username = username.to_owned();
    vec![
        (
            "create",
            refusal_of(
                service
                    .create_account(Request::new(CreateAccountRequest {
                        username: username.clone(),
                        quota_bytes: 0,
                        ..Default::default()
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    create_account_response::Result::Error(error) => Some(error),
                    create_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "suspend",
            refusal_of(
                service
                    .suspend_account(Request::new(SuspendAccountRequest {
                        username: username.clone(),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    suspend_account_response::Result::Error(error) => Some(error),
                    suspend_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "unsuspend",
            refusal_of(
                service
                    .unsuspend_account(Request::new(UnsuspendAccountRequest {
                        username: username.clone(),
                        ..Default::default()
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    unsuspend_account_response::Result::Error(error) => Some(error),
                    unsuspend_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "delete",
            refusal_of(
                service
                    .delete_account(Request::new(DeleteAccountRequest {
                        username: username.clone(),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    delete_account_response::Result::Error(error) => Some(error),
                    delete_account_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "set_quota",
            refusal_of(
                service
                    .set_account_quota(Request::new(SetAccountQuotaRequest {
                        username: username.clone(),
                        quota_bytes: 1024,
                        ..Default::default()
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    set_account_quota_response::Result::Error(error) => Some(error),
                    set_account_quota_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "resource_limits",
            refusal_of(
                service
                    .set_account_resource_limits(Request::new(SetAccountResourceLimitsRequest {
                        username: username.clone(),
                        cpu_quota_percent: Some(unlimited_ceiling()),
                        memory_max_mib: Some(unlimited_ceiling()),
                        tasks_max: Some(unlimited_ceiling()),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    set_account_resource_limits_response::Result::Error(error) => Some(error),
                    set_account_resource_limits_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "open_files_limit",
            refusal_of(
                service
                    .set_account_open_files_limit(Request::new(SetAccountOpenFilesLimitRequest {
                        username: username.clone(),
                        open_files: Some(unlimited_ceiling()),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    set_account_open_files_limit_response::Result::Error(error) => Some(error),
                    set_account_open_files_limit_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "usage",
            refusal_of(
                service
                    .get_account_usage(Request::new(GetAccountUsageRequest {
                        username: username.clone(),
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    get_account_usage_response::Result::Error(error) => Some(error),
                    get_account_usage_response::Result::Ok(_) => None,
                },
            ),
        ),
        (
            "suspension_state",
            refusal_of(
                service
                    .get_account_suspension_state(Request::new(GetAccountSuspensionStateRequest {
                        username,
                    }))
                    .await
                    .unwrap()
                    .into_inner()
                    .result,
                |result| match result {
                    get_account_suspension_state_response::Result::Error(error) => Some(error),
                    get_account_suspension_state_response::Result::Ok(_) => None,
                },
            ),
        ),
    ]
}

/// Every rpc that acts on an account answers a reserved name with the typed
/// refusal, and nothing reaches the machine.
///
/// `create` is in the list for the same name: it is refused before the host is
/// asked whether such a user exists.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn every_account_rpc_refuses_root_with_invalid_input_and_runs_nothing() {
    let (service, _, effects) =
        service_over("root:x:0:0:root:/root:/bin/bash\n".to_owned() + &standard_passwd());

    for (rpc, refusal) in every_rpc_for(&service, "root").await {
        let error = refusal.unwrap_or_else(|| panic!("{rpc} must refuse root"));
        assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "{rpc} must answer a refusal of the request, not a fault or an absence"
        );
        assert_eq!(
            error.message, "account 'root' is not a hosting account this panel manages",
            "{rpc}"
        );
        assert!(error.tool_output.is_empty(), "{rpc}");
    }
    assert_eq!(
        *effects.lock().unwrap(),
        0,
        "a refused request must not spawn a program or measure a tree"
    );
}

/// An existing user that is not a hosting account is a refusal, an absent one
/// is not found, and the two are never confused with each other or with an
/// idempotency outcome.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn an_unmanaged_user_is_invalid_input_and_an_absent_user_is_not_found() {
    let passwd = standard_passwd()
        + "ubuntu:x:1000:1000::/home/ubuntu:/bin/bash\n\
           opsadmin:x:1003:1003::/srv/opsadmin:/bin/bash\n";
    let (service, _, effects) = service_over(passwd);

    for user in ["ubuntu", "opsadmin"] {
        for (rpc, refusal) in every_rpc_for(&service, user)
            .await
            .into_iter()
            .filter(|(rpc, _)| *rpc != "create")
        {
            let error = refusal.unwrap_or_else(|| panic!("{rpc} must refuse {user}"));
            assert_eq!(
                error.code,
                ErrorCode::InvalidInput as i32,
                "{rpc} on {user}"
            );
        }
    }
    assert_eq!(*effects.lock().unwrap(), 0);

    for (rpc, refusal) in every_rpc_for(&service, "ghostuser")
        .await
        .into_iter()
        .filter(|(rpc, _)| *rpc != "create")
    {
        let error = refusal.unwrap_or_else(|| panic!("{rpc} must refuse a user that is absent"));
        assert_eq!(error.code, ErrorCode::NotFound as i32, "{rpc}");
    }
}

/// The inverse control: a hosting account is still served over the same rpcs.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn a_hosting_account_is_still_suspended_and_unsuspended_over_the_rpc() {
    let (service, _, effects) = service_over(standard_passwd());

    let suspended = service
        .suspend_account(Request::new(SuspendAccountRequest {
            username: FAST_ACCOUNT.to_owned(),
        }))
        .await
        .unwrap()
        .into_inner();
    let unsuspended = service
        .unsuspend_account(Request::new(UnsuspendAccountRequest {
            username: FAST_ACCOUNT.to_owned(),
            ..Default::default()
        }))
        .await
        .unwrap()
        .into_inner();

    assert!(
        matches!(
            suspended.result,
            Some(suspend_account_response::Result::Ok(_))
        ),
        "{suspended:?}"
    );
    assert!(
        matches!(
            unsuspended.result,
            Some(unsuspend_account_response::Result::Ok(_))
        ),
        "{unsuspended:?}"
    );
    assert!(
        *effects.lock().unwrap() > 0,
        "the accepted request must reach the host, or the refusals above prove nothing"
    );
}

/// The account the open-files cases address. Its own name, because the rpc takes
/// the account's process-wide lock and no other case in this file may hold it.
const OPEN_FILES_ACCOUNT: &str = "openfilesacct";

/// The standard password database plus [`OPEN_FILES_ACCOUNT`].
fn open_files_passwd() -> String {
    standard_passwd()
        + &format!(
            "{OPEN_FILES_ACCOUNT}:x:1005:1005::/home/{OPEN_FILES_ACCOUNT}:/usr/sbin/nologin\n"
        )
}

/// An open-files request that states no ceiling is refused before the host is
/// touched: an omitted field must never remove an account's limit.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn an_open_files_limit_without_a_ceiling_is_refused_and_runs_nothing() {
    let (service, _, effects) = service_over(open_files_passwd());

    let response = service
        .set_account_open_files_limit(Request::new(SetAccountOpenFilesLimitRequest {
            username: OPEN_FILES_ACCOUNT.to_owned(),
            open_files: None,
        }))
        .await
        .unwrap()
        .into_inner();

    let Some(set_account_open_files_limit_response::Result::Error(error)) = response.result else {
        panic!("an absent ceiling must be refused, got {response:?}");
    };
    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(
        error.message.starts_with("open_files:"),
        "{}",
        error.message
    );
    assert_eq!(*effects.lock().unwrap(), 0);
}

/// The inverse control: the same account with a ceiling stated is accepted.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn a_hosting_account_is_given_an_open_files_limit_over_the_rpc() {
    let (service, _, _) = service_over(open_files_passwd());

    let response = service
        .set_account_open_files_limit(Request::new(SetAccountOpenFilesLimitRequest {
            username: OPEN_FILES_ACCOUNT.to_owned(),
            open_files: Some(ResourceCeiling {
                ceiling: Some(resource_ceiling::Ceiling::Maximum(4096)),
            }),
        }))
        .await
        .unwrap()
        .into_inner();

    assert!(
        matches!(
            response.result,
            Some(set_account_open_files_limit_response::Result::Ok(_))
        ),
        "{response:?}"
    );
}
