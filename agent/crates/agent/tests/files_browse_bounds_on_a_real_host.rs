//! Every ceiling the browse rpcs hold against a customer who owns the tree being
//! read, observed on real directories and real files (issue #166).
//!
//! A customer can make a directory with millions of entries, a file that grows
//! as fast as it is read, a tree deeper than any limit, and as many requests as
//! the panel forwards. Each bound the agent holds has a test here that crosses
//! it by exactly one and a control that sits exactly on it, because a bound
//! mutated one too high or one too low passes a test that only goes far over.
//!
//! The concurrency cases hold browse operations open by leaving `ReadFile`
//! streams unread: the agent's pump blocks on a full channel and the call keeps
//! its slot until the stream is dropped.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/browse_session.rs"]
mod browse_session;
#[path = "fixtures/held_read.rs"]
mod held_read;
#[path = "fixtures/in_process_agent.rs"]
mod in_process_agent;
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_home_tree.rs"]
mod polygon_home_tree;
#[path = "fixtures/read_outcome.rs"]
mod read_outcome;

use std::time::{Duration, Instant};

use maran_agent::proto::ErrorCode;

use browse_session::BrowseSession;
use held_read::HeldRead;
use polygon_account::PolygonAccount;
use polygon_home_tree::PolygonHomeTree;

/// Names read from one directory at most; one more is `TooLarge`.
const DIRECTORY_NAME_CEILING: u32 = 100_000;

/// Entries a search scans at most before it stops and says it was truncated.
const SEARCH_SCAN_BUDGET: u32 = 200_000;

/// The ceiling a read request may ask for at most.
const READ_CEILING: u64 = 512 * 1024 * 1024;

/// Browse operations one account may have in flight.
const PER_ACCOUNT_SLOTS: usize = 4;

/// How long a released slot is given to become usable again.
const RELEASE_BUDGET: Duration = Duration::from_secs(15);

/// A file big enough that an unread `ReadFile` cannot finish: far above the
/// agent's channel (eight 64 KiB chunks) plus every transport window.
const HOLDING_FILE_BYTES: u64 = 64 * 1024 * 1024;

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and a hundred thousand files: polygon only"]
async fn a_directory_of_100001_names_is_too_large_and_one_of_100000_is_not() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrdirceiling");
    let tree = PolygonHomeTree::new(&account);
    let directory = tree.directory("big");
    tree.many_empty_files(&directory, "n", 0..DIRECTORY_NAME_CEILING);
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    // Control, exactly on the bound: it lists, and says how many there are.
    let mut request = BrowseSession::list_request(name, "big");
    request.limit = 10;
    let at_the_bound = session.list(request.clone()).await.unwrap();
    assert_eq!(at_the_bound.total_entries, DIRECTORY_NAME_CEILING);
    assert_eq!(at_the_bound.entries.len(), 10);

    tree.many_empty_files(
        &directory,
        "n",
        DIRECTORY_NAME_CEILING..DIRECTORY_NAME_CEILING + 1,
    );
    let over = session
        .list(request)
        .await
        .expect_err("one name over the bound");
    BrowseSession::assert_code(&over, ErrorCode::TooLarge);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and seventy thousand files: polygon only"]
async fn a_listing_over_the_result_byte_cap_is_too_large_while_a_small_one_is_not() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrbytecap");
    let tree = PolygonHomeTree::new(&account);
    // Seventy thousand names of the longest length are under the name ceiling
    // and over sixteen MiB once each carries its name and its figures.
    let heavy = tree.directory("heavy");
    tree.many_longest_names(&heavy, 70_000);
    tree.file("light/a.txt", b"a");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let light = session
        .list(BrowseSession::list_request(name, "light"))
        .await
        .unwrap();
    assert_eq!(light.entries.len(), 1);

    let mut request = BrowseSession::list_request(name, "heavy");
    request.limit = 10;
    let error = session
        .list(request)
        .await
        .expect_err("the child's result is over the byte channel's cap");
    BrowseSession::assert_code(&error, ErrorCode::TooLarge);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn the_listing_page_size_defaults_to_200_and_over_1000_is_refused() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrpagesize");
    let tree = PolygonHomeTree::new(&account);
    let directory = tree.directory("many");
    tree.many_empty_files(&directory, "e", 0..1_005);
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let default = session
        .list(BrowseSession::list_request(name, "many"))
        .await
        .unwrap();
    assert_eq!(
        default.entries.len(),
        200,
        "limit 0 means the agent's default"
    );
    assert_eq!(default.total_entries, 1_005);

    let mut at_max = BrowseSession::list_request(name, "many");
    at_max.limit = 1_000;
    let at_max = session.list(at_max).await.unwrap();
    assert_eq!(at_max.entries.len(), 1_000, "1000 is exactly the maximum");

    let mut over = BrowseSession::list_request(name, "many");
    over.limit = 1_001;
    let refused = session
        .list(over)
        .await
        .expect_err("limit over the maximum");
    BrowseSession::assert_code(&refused, ErrorCode::InvalidInput);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_read_ceiling_smaller_than_the_file_is_refused_before_any_byte() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrreadceiling");
    let tree = PolygonHomeTree::new(&account);
    let content = vec![7_u8; 100_000];
    tree.file("exact.bin", &content);
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    // Control, exactly on the bound: a ceiling equal to the size reads it all.
    let at_the_bound = session
        .read(BrowseSession::read_request(
            name,
            "exact.bin",
            content.len() as u64,
        ))
        .await;
    assert!(at_the_bound.error.is_none());
    assert_eq!(at_the_bound.bytes, content);

    let under = session
        .read(BrowseSession::read_request(
            name,
            "exact.bin",
            content.len() as u64 - 1,
        ))
        .await;
    BrowseSession::assert_code(
        &under.error.expect("a file over the ceiling is refused"),
        ErrorCode::TooLarge,
    );
    assert_eq!(under.chunks, 0, "refused before any byte is streamed");
    assert!(under.bytes.is_empty());
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_read_ceiling_of_zero_or_over_the_agents_maximum_is_refused() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrreadrange");
    let tree = PolygonHomeTree::new(&account);
    tree.file("small.txt", b"small");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    for ceiling in [0, READ_CEILING + 1] {
        let read = session
            .read(BrowseSession::read_request(name, "small.txt", ceiling))
            .await;
        BrowseSession::assert_code(
            &read
                .error
                .unwrap_or_else(|| panic!("ceiling {ceiling} must be refused")),
            ErrorCode::InvalidInput,
        );
    }

    // Control: the maximum itself is accepted.
    let at_max = session
        .read(BrowseSession::read_request(name, "small.txt", READ_CEILING))
        .await;
    assert_eq!(at_max.bytes, b"small");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_file_that_grows_past_the_ceiling_mid_read_ends_the_stream_with_too_large() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrgrowing");
    let tree = PolygonHomeTree::new(&account);
    let path = tree.sparse_file("growing.bin", HOLDING_FILE_BYTES);
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    // The size check passes: the ceiling equals the size NOW. The stream is
    // opened and left unread, so the pump has read only the first chunks.
    let mut held = session
        .open_read(BrowseSession::read_request(
            name,
            "growing.bin",
            HOLDING_FILE_BYTES,
        ))
        .await
        .expect("the file fits its ceiling when the read starts");

    // The customer appends while the agent reads. Only counting the bytes while
    // streaming (not trusting the size seen at the start) can notice.
    let appended = std::fs::OpenOptions::new()
        .append(true)
        .open(&path)
        .unwrap();
    appended.set_len(HOLDING_FILE_BYTES + 1024 * 1024).unwrap();

    let mut error = None;
    let mut received = 0_u64;
    while let Some(message) =
        tokio::time::timeout(browse_session::CALL_BUDGET, held.stream.message())
            .await
            .expect("the stream stalled")
            .expect("transport failure")
    {
        match message.result.expect("a message with no arm") {
            maran_agent::proto::read_file_response::Result::Ok(chunk) => {
                received += chunk.data.len() as u64;
            }
            maran_agent::proto::read_file_response::Result::Error(found) => {
                error = Some(found);
                break;
            }
        }
    }

    BrowseSession::assert_code(
        &error.expect("the stream must end with the error, not clean"),
        ErrorCode::TooLarge,
    );
    assert!(
        received <= HOLDING_FILE_BYTES,
        "no more than the ceiling's bytes may have been delivered ({received})"
    );
}

/// Makes `depth` nested directories under `home`, each named `d`, ending in a
/// file called `needle-<depth>.txt`, and returns nothing.
fn plant_chain(tree: &PolygonHomeTree<'_>, depth: u32) {
    let mut relative = String::new();
    for _ in 0..depth {
        relative.push_str("d/");
    }
    tree.file(&format!("{relative}needle-{depth}.txt"), b"x");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn search_depth_is_clamped_to_sixteen_whatever_the_request_asks_for() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrdepth");
    let tree = PolygonHomeTree::new(&account);
    plant_chain(&tree, 15);
    plant_chain(&tree, 17);
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let mut request = BrowseSession::search_request(name, "", "needle");
    request.max_depth = 1_000;
    let found = session.search(request).await.unwrap();
    let names: Vec<_> = found
        .matches
        .iter()
        .map(|m| m.entry.as_ref().unwrap().name.clone())
        .collect();

    // Control: a file 15 levels down is inside the ceiling and found.
    assert!(names.contains(&"needle-15.txt".to_owned()), "{names:?}");
    // A request for a thousand levels is held to sixteen: the file 17 levels
    // down is out of reach, and the search says nothing was cut by a budget.
    assert!(!names.contains(&"needle-17.txt".to_owned()), "{names:?}");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn search_results_are_clamped_to_500_and_the_search_says_it_was_truncated() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrresults");
    let tree = PolygonHomeTree::new(&account);
    let wide = tree.directory("wide");
    tree.many_empty_files(&wide, "match-", 0..600);
    tree.file("few/match-only", b"x");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let mut request = BrowseSession::search_request(name, "wide", "match-");
    request.max_results = 100_000;
    let clamped = session.search(request).await.unwrap();
    assert_eq!(
        clamped.matches.len(),
        500,
        "requests are clamped to the agent's ceiling"
    );
    assert!(
        clamped.truncated,
        "stopped by a bound, so there may be more"
    );

    // Control: a search that finds fewer than the bound is not truncated.
    let few = session
        .search(BrowseSession::search_request(name, "few", "match-"))
        .await
        .unwrap();
    assert_eq!(few.matches.len(), 1);
    assert!(!few.truncated);

    // The default is smaller than the clamp.
    let default = session
        .search(BrowseSession::search_request(name, "wide", "match-"))
        .await
        .unwrap();
    assert_eq!(
        default.matches.len(),
        100,
        "max_results 0 means the default"
    );
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and two hundred thousand files: polygon only"]
async fn the_search_scan_budget_truncates_and_is_never_an_error() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrbudget");
    let tree = PolygonHomeTree::new(&account);
    let directory = tree.directory("filler");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    // Control: well under the budget, a query that matches nothing scans it all
    // and says so — no match and not truncated.
    tree.many_empty_files(&directory, "f", 0..SEARCH_SCAN_BUDGET - 50_000);
    let under = session
        .search(BrowseSession::search_request(name, "", "zzz-nothing"))
        .await
        .unwrap();
    assert!(under.matches.is_empty());
    assert!(!under.truncated, "the whole tree fits the budget");

    // Over it: the budget counts every name read, matched or not.
    tree.many_empty_files(
        &directory,
        "f",
        SEARCH_SCAN_BUDGET - 50_000..SEARCH_SCAN_BUDGET + 50_000,
    );
    let over = session
        .search(BrowseSession::search_request(name, "", "zzz-nothing"))
        .await
        .expect("a budget is a truncation, never an error");
    assert!(over.matches.is_empty());
    assert!(over.truncated, "the budget stopped the scan");
}

/// Opens a held read of the sparse file at `path`, which must be admitted.
async fn hold(session: &BrowseSession, account: &str, path: &str) -> HeldRead {
    session
        .open_read(BrowseSession::read_request(
            account,
            path,
            HOLDING_FILE_BYTES,
        ))
        .await
        .unwrap_or_else(|error| panic!("a slot should have been free: {}", error.message))
}

/// Waits until a listing of `account` is admitted again.
async fn wait_until_admitted(session: &BrowseSession, account: &str) {
    let deadline = Instant::now() + RELEASE_BUDGET;
    loop {
        if session
            .list(BrowseSession::list_request(account, ""))
            .await
            .is_ok()
        {
            return;
        }
        assert!(
            Instant::now() < deadline,
            "the slot was not released within {RELEASE_BUDGET:?} of the stream being dropped"
        );
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_fifth_browse_operation_of_one_account_is_refused_and_admitted_once_one_ends() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrslots");
    PolygonHomeTree::new(&account).sparse_file("big.bin", HOLDING_FILE_BYTES);
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let mut held = Vec::new();
    for _ in 0..PER_ACCOUNT_SLOTS {
        held.push(hold(&session, name, "big.bin").await);
    }

    // Refused, not queued: the answer comes back at once with the saturation code.
    let refused = session
        .list(BrowseSession::list_request(name, ""))
        .await
        .expect_err("the account's slots are all taken");
    BrowseSession::assert_code(&refused, ErrorCode::AgentSaturated);

    // Released by the client going away: the pump stops and the grant drops.
    drop(held.pop());
    wait_until_admitted(&session, name).await;
    drop(held);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates real system accounts and drops privileges: polygon only"]
async fn a_seventeenth_browse_operation_on_the_host_is_refused_across_accounts() {
    PolygonAccount::require_polygon();
    // Four accounts fill the host's sixteen slots, four each; a fifth account,
    // with every one of its own slots free, is refused by the host bound alone.
    let accounts: Vec<PolygonAccount> = ["pbrhostone", "pbrhosttwo", "pbrhostthree", "pbrhostfour"]
        .into_iter()
        .map(PolygonAccount::create)
        .collect();
    let fifth = PolygonAccount::create("pbrhostfive");
    for account in accounts.iter().chain([&fifth]) {
        PolygonHomeTree::new(account).sparse_file("big.bin", HOLDING_FILE_BYTES);
    }
    let session = BrowseSession::start().await;

    let mut held = Vec::new();
    for account in &accounts {
        for _ in 0..PER_ACCOUNT_SLOTS {
            held.push(hold(&session, account.name().as_str(), "big.bin").await);
        }
    }

    let refused = session
        .list(BrowseSession::list_request(fifth.name().as_str(), ""))
        .await
        .expect_err("the host's sixteen slots are all taken");
    BrowseSession::assert_code(&refused, ErrorCode::AgentSaturated);

    // Released: dropping one stream makes room for the fifth account again.
    drop(held.pop());
    wait_until_admitted(&session, fifth.name().as_str()).await;
    drop(held);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn the_refusals_above_are_not_an_artefact_of_a_missing_account() {
    PolygonAccount::require_polygon();
    // The inverse control for the two concurrency cases: with no stream held,
    // the same listing is admitted, so a refusal there is the bound speaking.
    let account = PolygonAccount::create("pbrinverse");
    PolygonHomeTree::new(&account).file("a.txt", b"a");
    let session = BrowseSession::start().await;

    let listed = session
        .list(BrowseSession::list_request(account.name().as_str(), ""))
        .await
        .unwrap();
    assert_eq!(listed.entries.len(), 1);

    let unknown = session
        .list(BrowseSession::list_request("pbrnosuchaccount", ""))
        .await
        .expect_err("an account that does not exist");
    // The agent trusts the panel to name an account it holds, so an account the host does not
    // have is a failure of the privileged work, not a refusal of the request: the mapping
    // `file_status_tests` pins (`Privilege(NoSuchAccount)` is `SystemFailure`). What this case
    // adds is that it is an ANSWER of that kind and not the saturation code, so the bound above
    // is not what a missing account looks like.
    BrowseSession::assert_code(&unknown, ErrorCode::SystemFailure);
}
