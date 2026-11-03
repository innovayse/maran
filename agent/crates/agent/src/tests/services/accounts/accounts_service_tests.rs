//! Whether an account rpc holds the runtime's workers while it waits.
//!
//! The account operations spawn `useradd`, `chgrp`, `chmod`, `setquota`, `id`
//! and `quota` and wait on each. This service awaited them on the async
//! worker itself — no `spawn_blocking` anywhere in the file — so for as long
//! as one account creation was inside `useradd`, every other in-flight
//! command on that worker was stopped, with no symptom but an unrelated
//! timeout under load.
//!
//! Nothing in the existing suite could see that: every account test drives
//! `ops::accounts` synchronously with a fake that returns at once, and a fake
//! that never blocks stalls nothing. The test below therefore supplies a fake
//! that DOES block, on a runtime with exactly one worker, and asks the only
//! question that distinguishes the two arrangements: while one rpc is inside
//! its spawn, does a second rpc still answer? On the blocking pool it answers
//! in milliseconds; on the worker it cannot answer until the first spawn
//! returns.
//!
//! One worker rather than the default count deliberately. With several
//! workers a stalled one is hidden by its neighbours, which is exactly why
//! the defect survived so long in production shape.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};
use std::time::Instant;

use tonic::Request;

use super::fixture::blocking_host::{
    BLOCKING_SPAWN, FAST_ACCOUNT, SECOND_RPC_BUDGET, SLOW_ACCOUNT,
};
use super::fixture::service;
use crate::proto::accounts_service_server::AccountsService;
use crate::proto::{
    CreateAccountRequest, ErrorCode, GetAccountSuspensionStateRequest, GetAccountUsageRequest,
    LoginPasswordState, create_account_response, get_account_suspension_state_response,
    get_account_usage_response,
};

/// The moment the blocking host entered its spawn, waited for by yielding
/// rather than by sleeping.
///
/// Yielding is what makes this honest on the broken arrangement too: the
/// spawned rpc and this loop share the one worker, so the loop gives the
/// worker up and the rpc gets it. Whether the loop then resumes in
/// milliseconds or only after the block has finished is the whole question,
/// and it is answered by the recorded instant rather than by this loop.
async fn blocking_start(cell: &Arc<Mutex<Option<Instant>>>) -> Instant {
    loop {
        if let Some(began) = *cell.lock().expect("the recorded instant is readable") {
            return began;
        }
        tokio::task::yield_now().await;
    }
}

/// A second rpc must answer while the first is still inside its spawn.
///
/// This is the regression test for the accounts service awaiting its process
/// spawns on the runtime worker. With one worker and no `spawn_blocking`, the
/// usage rpc cannot be polled until the creation's `useradd` has returned, so
/// it answers [`BLOCKING_SPAWN`] late; with the spawn on the blocking pool it
/// answers at once. The assertion is on the elapsed time measured FROM the
/// blocking host's own start instant, which is the only reading a stalled
/// worker cannot distort.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn a_second_rpc_answers_while_an_account_creation_is_inside_its_spawn() {
    let (service, blocking_began) = service();

    let creating = tokio::spawn({
        let service = Arc::clone(&service);
        async move {
            service
                .create_account(Request::new(CreateAccountRequest {
                    username: SLOW_ACCOUNT.to_owned(),
                    quota_bytes: 0,
                }))
                .await
        }
    });

    let began = blocking_start(&blocking_began).await;

    // Spawned rather than awaited here. A `#[tokio::test(flavor =
    // "multi_thread")]` body is driven by `block_on` on the harness's own
    // thread, which is NOT one of the worker threads: awaiting the second rpc
    // inline would run it on a thread the first rpc could never have stalled,
    // and the test would pass on the defect it exists to catch. Both rpcs are
    // therefore tasks, and the single worker they share is the thing under
    // test.
    let usage = tokio::spawn({
        let service = Arc::clone(&service);
        async move {
            service
                .get_account_usage(Request::new(GetAccountUsageRequest {
                    username: FAST_ACCOUNT.to_owned(),
                }))
                .await
        }
    })
    .await
    .unwrap()
    .unwrap();
    let answered_after = began.elapsed();

    assert!(
        matches!(
            usage.into_inner().result,
            Some(get_account_usage_response::Result::Ok(_))
        ),
        "the second rpc must answer, and answer successfully"
    );
    assert!(
        answered_after < SECOND_RPC_BUDGET,
        "the second rpc answered {answered_after:?} after the first entered a {BLOCKING_SPAWN:?} \
         spawn, so it was queued behind it: the operations are being awaited on the runtime \
         worker instead of the blocking pool"
    );

    let created = creating.await.unwrap().unwrap();
    assert!(
        matches!(
            created.into_inner().result,
            Some(create_account_response::Result::Ok(_))
        ),
        "the first rpc must still complete normally"
    );
}

/// The suspension state reaches the wire as an answer, not as a transport
/// status, and each of its three parts lands in its own field.
///
/// The field mapping is what this pins. Two booleans side by side in one
/// message is exactly the shape that survives being swapped: a suspension
/// reported over an unreadable directory, or an unreadable directory reported
/// as a locked login, would both compile and both read as success.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn the_suspension_state_reaches_the_wire_as_an_answer_field_by_field() {
    let (service, _) = service();

    let response = service
        .get_account_suspension_state(Request::new(GetAccountSuspensionStateRequest {
            username: FAST_ACCOUNT.to_owned(),
        }))
        .await
        .expect("the rpc answers")
        .into_inner();

    let Some(get_account_suspension_state_response::Result::Ok(state)) = response.result else {
        panic!("a readable host answers with an ok, not with an error: {response:?}");
    };

    assert!(state.login_locked, "`passwd -S` reported the login as `L`");
    assert_eq!(
        state.login_password_state,
        LoginPasswordState::Absent as i32,
        "the shadow field held `!`: locked to `passwd -S`, and no password to unlock",
    );
    assert!(
        state.sites_directory_readable,
        "the directory was listed; an empty list must not be reported as a blind one",
    );
    assert!(state.sites.is_empty());

    // The cron and SFTP halves land in their own fields too. Both are the
    // "nothing there" answer, and both are reported as an OBSERVED nothing:
    // the host answered, it was not that nobody asked.
    assert_eq!(state.cron_entries_total, 0);
    assert_eq!(state.cron_entries_suspended, 0);
    assert_eq!(state.cron_foreign_lines, 0);
    assert!(state.sftp_logins.is_empty());
}

/// An invalid username is refused by the agent's own revalidation, in the
/// payload rather than as a transport error.
#[tokio::test(flavor = "multi_thread", worker_threads = 1)]
async fn a_suspension_state_asked_for_an_impossible_username_is_refused_in_the_payload() {
    let (service, _) = service();

    let response = service
        .get_account_suspension_state(Request::new(GetAccountSuspensionStateRequest {
            username: "../../etc".to_owned(),
        }))
        .await
        .expect("the rpc answers")
        .into_inner();

    let Some(get_account_suspension_state_response::Result::Error(error)) = response.result else {
        panic!("an impossible name is refused: {response:?}");
    };

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}
