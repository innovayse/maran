//! The two read-only firewall rpcs, driven the way the server drives them.
//!
//! These tests exist because of a defect a live browser run found and no test
//! saw: `GET /firewall` answered 500 in every debug build, because the area
//! carried a `debug_assert!` on `tokio::runtime::Handle::try_current()
//! .is_err()` that fires inside `tokio::task::spawn_blocking` — a blocking
//! pool thread still belongs to the runtime. The whole suite missed it for
//! one reason: nothing called an rpc of this service from a tokio runtime.
//! Every firewall test called `ops::firewall::*` synchronously, with no
//! runtime entered at all, so the assertion had nothing to fire on.
//!
//! So these are `#[tokio::test]`s that go through `FirewallServiceImpl`
//! itself — the surface the server registers and the panel calls — rather
//! than through the operation underneath it. That is what makes them able to
//! observe the thing they report on (rules/testing.md): they run the real
//! `spawn_blocking` wrapper on a real multi-threaded runtime, which is the
//! exact arrangement the live 500 came out of.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::path::{Path, PathBuf};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_distro::{DistroFamily, adapter_for};
use maran_ops::firewall::{FirewallError, FirewallHost};
use tonic::Request;

use crate::proto::firewall_service_server::FirewallService;
use crate::proto::{ListBansRequest, ListRulesRequest, list_bans_response, list_rules_response};
use crate::services::firewall::firewall_service::FirewallServiceImpl;

/// The exit status `nft` uses for a set that is not there, which the bans
/// listing reads as "the table has never been loaded, so there are no bans".
const NFT_ABSENT_SET: i32 = 1;

/// A host with no ruleset file and no bans table: the state a machine is in
/// before the installer seeds it.
///
/// It answers what the real tool answers for that state and nothing more —
/// these tests are about the CALL SHAPE the rpc uses, not about what the
/// operations make of a populated host, which the `ops` suite already pins
/// against its own richer fake. Every write method is unreachable from the
/// two read-only rpcs driven here and says so rather than pretending to work.
struct UnseededHost;

impl FirewallHost for UnseededHost {
    /// Answers as `nft` does for a set in a table that is not loaded.
    fn run(&self, _program: &str, _arguments: &[&str]) -> Result<CommandOutcome, FirewallError> {
        Ok(CommandOutcome {
            status: NFT_ABSENT_SET,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    /// Answers that the ruleset file has never been written.
    fn read_file(&self, _path: &Path) -> Result<Option<String>, FirewallError> {
        Ok(None)
    }

    /// Unreachable: no read-only rpc stages a file.
    fn stage_file(&self, _target: &Path, _contents: &str) -> Result<PathBuf, FirewallError> {
        panic!("a read-only firewall rpc must not stage a file")
    }

    /// Unreachable: no read-only rpc flushes a file.
    fn sync_file(&self, _staged: &Path) -> Result<(), FirewallError> {
        panic!("a read-only firewall rpc must not flush a file")
    }

    /// Unreachable: no read-only rpc renames a file into place.
    fn commit_file(&self, _staged: &Path, _target: &Path) -> Result<(), FirewallError> {
        panic!("a read-only firewall rpc must not commit a file")
    }

    /// Unreachable: no read-only rpc flushes a directory.
    fn sync_directory(&self, _target: &Path) -> Result<(), FirewallError> {
        panic!("a read-only firewall rpc must not flush a directory")
    }

    /// Unreachable: no read-only rpc discards a staged file.
    fn discard_file(&self, _staged: &Path) {
        panic!("a read-only firewall rpc must not discard a file")
    }
}

/// The service under test, over a host that has never been seeded.
fn service() -> FirewallServiceImpl<UnseededHost> {
    FirewallServiceImpl::new(UnseededHost, adapter_for(DistroFamily::Debian))
}

/// A `ListRules` request naming one ssh port and the panel's.
fn list_rules_request() -> Request<ListRulesRequest> {
    Request::new(ListRulesRequest {
        ssh_ports: vec![22],
        panel_port: 8443,
    })
}

#[tokio::test]
async fn listing_rules_over_the_rpc_answers_instead_of_failing() {
    let response = service().list_rules(list_rules_request()).await.unwrap();

    match response
        .into_inner()
        .result
        .expect("a result is always set")
    {
        list_rules_response::Result::Ok(listed) => assert!(
            listed.rules.is_empty(),
            "an unseeded host manages no rules yet"
        ),
        list_rules_response::Result::Error(error) => {
            panic!("listing rules on an unseeded host must not fail: {error:?}")
        }
    }
}

#[tokio::test]
async fn listing_bans_over_the_rpc_answers_instead_of_failing() {
    let response = service()
        .list_bans(Request::new(ListBansRequest {}))
        .await
        .unwrap();

    match response
        .into_inner()
        .result
        .expect("a result is always set")
    {
        list_bans_response::Result::Ok(listed) => assert!(
            listed.bans.is_empty(),
            "a host whose bans table was never loaded holds no bans"
        ),
        list_bans_response::Result::Error(error) => {
            panic!("listing bans on an unseeded host must not fail: {error:?}")
        }
    }
}

/// The same rpc on a single-threaded runtime, which is the flavour a
/// `#[tokio::test]` gets by default and the one the multi-threaded tests
/// above would not catch a flavour-sensitive mistake on.
///
/// It is here because the guard this file's defect lived in was replaced
/// after establishing that no probe distinguishes a blocking-pool thread from
/// a runtime worker on either flavour; if a future edit reintroduces one that
/// happens to work on `rt-multi-thread` only, this test is where it shows up.
#[tokio::test(flavor = "current_thread")]
async fn listing_rules_answers_on_a_single_threaded_runtime_too() {
    let response = service().list_rules(list_rules_request()).await.unwrap();

    assert!(
        matches!(
            response.into_inner().result,
            Some(list_rules_response::Result::Ok(_))
        ),
        "the rpc must answer on a current-thread runtime as well"
    );
}

/// The six operations `ops::firewall` exposes, every one of which must be
/// reached through the blocking pool.
///
/// Spelled out rather than derived, because a list derived from the source
/// being checked shrinks silently when the source does, and a check that can
/// be satisfied by an empty answer is the failure mode rules/testing.md is
/// most explicit about.
const FIREWALL_OPERATIONS: [&str; 6] = [
    "list_rules",
    "list_bans",
    "allow_port",
    "deny_port",
    "ban_address",
    "unban_address",
];

/// The noun phrase this service passes to the shared wrapper, which is also
/// what distinguishes its call sites from any other area's.
const FIREWALL_WHAT: &str = "\"firewall operation\"";

/// The shape a correct call site has: the operation named directly as the body
/// of the closure handed to the shared `run_blocking` wrapper, with this
/// service's own noun phrase and error mapping in between.
///
/// The whole prefix is matched rather than the closure alone. `run_blocking`
/// is shared with nine other services now, so `move || firewall::…` on its own
/// would be satisfied by a closure handed to anything at all — including a
/// helper that never reaches the blocking pool, which is precisely the
/// substitution the last assertion below exists to refuse.
fn wrapped_call(operation: &str) -> String {
    format!("run_blocking({FIREWALL_WHAT}, to_agent_error, move || firewall::{operation}(")
}

/// Any call at all into one of the operations.
fn any_call(operation: &str) -> String {
    format!("firewall::{operation}(")
}

/// Every `.rs` file in this crate's `src/` except this tests mirror, as
/// (path, source) pairs.
///
/// The whole crate rather than `firewall_service.rs` alone, because the
/// regression this guards against is a NEW caller elsewhere awaiting a
/// firewall operation directly — a file that does not exist yet cannot be
/// named by an `include_str!`.
fn agent_sources() -> Vec<(PathBuf, String)> {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("src");
    let mirror = root.join("tests");
    let mut sources = Vec::new();
    let mut pending = vec![root];

    while let Some(directory) = pending.pop() {
        for entry in fs::read_dir(&directory).expect("the crate's own source tree is readable") {
            let path = entry.expect("a directory entry is readable").path();

            if path.is_dir() {
                if path != mirror {
                    pending.push(path);
                }
            } else if path.extension().is_some_and(|kind| kind == "rs") {
                let source = fs::read_to_string(&path).expect("a source file is UTF-8");
                sources.push((path, source));
            }
        }
    }

    sources
}

/// `source` with every run of whitespace collapsed to one space, and a
/// closure body's opening brace dropped.
///
/// Both normalizations exist so the matcher reads the CODE and not rustfmt's
/// line breaking. A call to the shared wrapper is long enough that rustfmt
/// splits it across lines and wraps a multi-line body in braces, so the same
/// correct call site is spelled `move || firewall::list_bans(` in one handler
/// and `move ||\n{\n firewall::allow_port(` in the next. A matcher sensitive
/// to that difference would silently stop seeing five of the six operations
/// the first time a line grew.
fn normalized(source: &str) -> String {
    source
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
        .replace("move || { ", "move || ")
}

/// Counts the calls into `operation` in `source`, and how many of those are
/// wrapped in the blocking-pool helper.
fn call_sites(source: &str, operation: &str) -> (usize, usize) {
    let source = normalized(source);

    (
        source.matches(&any_call(operation)).count(),
        source.matches(&wrapped_call(operation)).count(),
    )
}

/// The replacement for the runtime guard this file's defect lived in: the
/// property is a static fact about the call sites, so it is checked as one.
///
/// A firewall operation awaited on a runtime worker spawns `nft` and reads a
/// file there, and the only symptom under load is an unrelated command
/// elsewhere timing out with nothing naming the cause. `ops` used to assert
/// `Handle::try_current().is_err()` against that, which is false inside
/// `spawn_blocking` — the assertion fired on the correct call path and every
/// debug build answered `ListRules` with a panic. No stable tokio API tells a
/// blocking-pool thread from a worker — measured on tokio 1.53, where
/// `try_current`, the runtime flavour, `task::try_id`, `block_in_place` and
/// even the thread name agree on both — so there is nothing to assert at run
/// time. What CAN be observed is what this asserts.
#[test]
fn every_firewall_operation_is_reached_through_the_blocking_pool() {
    let sources = agent_sources();
    let mut total_calls = 0_usize;

    for operation in FIREWALL_OPERATIONS {
        let mut calls_for_operation = 0_usize;

        for (path, source) in &sources {
            let (calls, wrapped) = call_sites(source, operation);
            assert_eq!(
                calls,
                wrapped,
                "{}: {} of the {calls} call(s) to firewall::{operation} do not go through \
                 run_blocking(\"firewall operation\", to_agent_error, move || …), which is the \
                 only thing putting them on the blocking pool",
                path.display(),
                calls - wrapped
            );
            calls_for_operation += calls;
        }

        assert!(
            calls_for_operation > 0,
            "no call to firewall::{operation} was found anywhere in this crate — either the rpc \
             was removed, or this check has gone blind and is now passing on an empty search"
        );
        total_calls += calls_for_operation;
    }

    assert!(
        total_calls >= FIREWALL_OPERATIONS.len(),
        "every operation must be called at least once; found {total_calls}"
    );

    let helper = sources
        .iter()
        .find(|(path, _)| path.ends_with("run_blocking.rs"))
        .map(|(_, source)| source)
        .expect("the shared blocking wrapper is in this crate's source tree");
    assert!(
        helper.contains("tokio::task::spawn_blocking(operation).await"),
        "run_blocking must be spawn_blocking; routing the calls through a wrapper that is not \
         one satisfies every assertion above while changing nothing"
    );
}

/// The inverse control for the check above: it must actually REFUSE the call
/// shape it exists to refuse.
///
/// Without this, a matcher that had gone blind — a renamed helper, a changed
/// spelling — would pass the real check by finding zero unwrapped calls in a
/// search that can no longer match anything, which is the exact shape
/// rules/testing.md names as worse than no check.
#[test]
fn the_call_site_check_refuses_an_operation_awaited_directly() {
    let offending = "let bans = firewall::list_bans(host.as_ref(), distro).await;";
    let (calls, wrapped) = call_sites(offending, "list_bans");

    assert_eq!(calls, 1, "the matcher must see a direct call");
    assert_eq!(
        wrapped, 0,
        "a direct call is not wrapped, and must not count"
    );

    let accepted = "run_blocking(\"firewall operation\", to_agent_error, move || \
         firewall::list_bans(host.as_ref(), distro)).await";
    let (calls, wrapped) = call_sites(accepted, "list_bans");

    assert_eq!(calls, 1, "the matcher must see the wrapped call too");
    assert_eq!(wrapped, 1, "a wrapped call must be accepted");

    // The shared wrapper is nine other services' wrapper too, so a call handed
    // to it under a different area's noun phrase is not this area's call site
    // and must not be counted as one.
    let another_area = "run_blocking(\"site operation\", to_agent_error, move || \
                        firewall::list_bans(host.as_ref(), distro)).await";
    let (calls, wrapped) = call_sites(another_area, "list_bans");

    assert_eq!(calls, 1, "the matcher must see the call");
    assert_eq!(
        wrapped, 0,
        "a call wrapped under another area's phrase must not count as this area's"
    );

    // rustfmt writes a multi-line closure body in braces, and both spellings
    // are the same call site.
    let across_lines = "run_blocking(\"firewall operation\", to_agent_error, move || {\n    \
                        firewall::list_bans(host.as_ref(), distro)\n})\n.await";
    let (calls, wrapped) = call_sites(across_lines, "list_bans");

    assert_eq!(calls, 1, "the matcher must see the call across lines");
    assert_eq!(
        wrapped, 1,
        "a wrapped call broken across lines must be accepted"
    );
}
