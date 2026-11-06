//! Tests for the rpc classification and its census against the contract.
//!
//! The census is the guard that makes adding an rpc without classifying it a
//! red build. It is satisfied by an empty answer, so every refusal below has a
//! planted control (a fake contract the checker must object to) and an inverse
//! control (a consistent one it must accept).

use super::AuditRpcClass;
use crate::audit::mutating_rpcs::MUTATING_RPCS;
use crate::audit::read_only_rpcs::READ_ONLY_RPCS;

/// The contract baseline `maran proto --accept` records, read as the bytes the
/// reviewer sees in the diff.
const CONTRACT_BASELINE: &str =
    include_str!("../../../../../../proto/agent/v1/contract-baseline.txt");

/// Reads every `rpc <package.Service> <Method> ...` line of a baseline text and
/// returns the full gRPC path of each, in file order.
fn contract_paths(baseline: &str) -> Vec<String> {
    baseline
        .lines()
        .filter_map(|line| {
            let mut words = line.split_whitespace();
            match (words.next(), words.next(), words.next()) {
                (Some("rpc"), Some(service), Some(method)) => Some(format!("/{service}/{method}")),
                _ => None,
            }
        })
        .collect()
}

/// Names what is wrong with one list: an entry out of byte order, or repeated.
fn list_problems(name: &str, list: &[&str]) -> Vec<String> {
    let mut problems = Vec::new();
    for pair in list.windows(2) {
        if pair[0] == pair[1] {
            problems.push(format!("{name}: duplicate {}", pair[0]));
        } else if pair[0] > pair[1] {
            problems.push(format!(
                "{name}: {} is not sorted before {}",
                pair[1], pair[0]
            ));
        }
    }
    problems
}

/// Compares the two lists with a baseline text and names every disagreement:
/// a contract rpc in neither list or in both, a listed path the contract lacks,
/// and a list that is unsorted or holds a duplicate. Empty means consistent.
fn census_problems(baseline: &str, mutating: &[&str], read_only: &[&str]) -> Vec<String> {
    let contract = contract_paths(baseline);
    let mut problems = Vec::new();
    for path in &contract {
        let in_mutating = mutating.contains(&path.as_str());
        let in_read_only = read_only.contains(&path.as_str());
        if !in_mutating && !in_read_only {
            problems.push(format!("unclassified: {path}"));
        }
        if in_mutating && in_read_only {
            problems.push(format!("classified twice: {path}"));
        }
    }
    for (name, list) in [("mutating", mutating), ("read-only", read_only)] {
        for path in list {
            if !contract.iter().any(|known| known.as_str() == *path) {
                problems.push(format!("stale {name} path: {path}"));
            }
        }
        problems.extend(list_problems(name, list));
    }
    problems
}

/// A two-rpc contract the planted cases start from.
const FAKE_BASELINE: &str = "\
message maran.agent.v1.Unrelated
rpc maran.agent.v1.FakeService ChangeThing in=.x out=.y client_streaming=false server_streaming=false
rpc maran.agent.v1.FakeService ReadThing in=.x out=.y client_streaming=false server_streaming=false
";

#[test]
fn every_rpc_of_the_contract_is_classified_in_exactly_one_list() {
    let problems = census_problems(CONTRACT_BASELINE, MUTATING_RPCS, READ_ONLY_RPCS);

    assert!(
        problems.is_empty(),
        "census failed:\n{}",
        problems.join("\n")
    );
}

#[test]
fn the_census_reads_the_whole_contract_and_not_an_empty_part_of_it() {
    // Vacuity guard on the axis that can go blind: the parse. A baseline whose
    // lines stopped matching would yield no rpc, and "no rpc is unclassified"
    // would then be true of any pair of lists.
    let contract = contract_paths(CONTRACT_BASELINE);

    assert!(contract.len() > 50, "only {} rpcs parsed", contract.len());
    assert_eq!(contract.len(), MUTATING_RPCS.len() + READ_ONLY_RPCS.len());
    assert!(contract.contains(&"/maran.agent.v1.SitesService/CreateSite".to_owned()));
}

#[test]
fn a_consistent_fake_contract_is_accepted() {
    let problems = census_problems(
        FAKE_BASELINE,
        &["/maran.agent.v1.FakeService/ChangeThing"],
        &["/maran.agent.v1.FakeService/ReadThing"],
    );

    assert!(problems.is_empty(), "inverse control refused: {problems:?}");
}

#[test]
fn a_planted_unclassified_rpc_is_reported() {
    let problems = census_problems(
        FAKE_BASELINE,
        &["/maran.agent.v1.FakeService/ChangeThing"],
        &[],
    );

    assert_eq!(
        problems,
        ["unclassified: /maran.agent.v1.FakeService/ReadThing"]
    );
}

#[test]
fn a_planted_rpc_in_both_lists_is_reported() {
    let problems = census_problems(
        FAKE_BASELINE,
        &["/maran.agent.v1.FakeService/ChangeThing"],
        &[
            "/maran.agent.v1.FakeService/ChangeThing",
            "/maran.agent.v1.FakeService/ReadThing",
        ],
    );

    assert_eq!(
        problems,
        ["classified twice: /maran.agent.v1.FakeService/ChangeThing"]
    );
}

#[test]
fn a_planted_stale_listed_path_is_reported() {
    let problems = census_problems(
        FAKE_BASELINE,
        &[
            "/maran.agent.v1.FakeService/ChangeThing",
            "/maran.agent.v1.FakeService/RemovedThing",
        ],
        &["/maran.agent.v1.FakeService/ReadThing"],
    );

    assert_eq!(
        problems,
        ["stale mutating path: /maran.agent.v1.FakeService/RemovedThing"]
    );
}

#[test]
fn a_planted_unsorted_or_duplicated_list_is_reported() {
    let unsorted = census_problems(
        FAKE_BASELINE,
        &["/maran.agent.v1.FakeService/ChangeThing"],
        &[
            "/maran.agent.v1.FakeService/ReadThing",
            "/maran.agent.v1.FakeService/ChangeThing",
        ],
    );
    let duplicated = list_problems("mutating", &["/maran.agent.v1.A/B", "/maran.agent.v1.A/B"]);

    assert!(
        unsorted
            .iter()
            .any(|problem| problem.contains("is not sorted before"))
    );
    assert_eq!(duplicated, ["mutating: duplicate /maran.agent.v1.A/B"]);
}

#[test]
fn a_mutating_path_is_classified_as_mutating() {
    assert_eq!(
        AuditRpcClass::of("/maran.agent.v1.SitesService/CreateSite"),
        Some(AuditRpcClass::Mutating)
    );
}

#[test]
fn a_read_only_path_is_classified_as_read_only() {
    assert_eq!(
        AuditRpcClass::of("/maran.agent.v1.SystemService/GetAgentInfo"),
        Some(AuditRpcClass::ReadOnly)
    );
}

#[test]
fn every_listed_path_is_classified_as_its_own_list() {
    for path in MUTATING_RPCS {
        assert_eq!(
            AuditRpcClass::of(path),
            Some(AuditRpcClass::Mutating),
            "{path}"
        );
    }
    for path in READ_ONLY_RPCS {
        assert_eq!(
            AuditRpcClass::of(path),
            Some(AuditRpcClass::ReadOnly),
            "{path}"
        );
    }
}

#[test]
fn an_unknown_or_malformed_path_has_no_class() {
    for path in [
        "",
        "/",
        "/maran.agent.v1.SitesService/NoSuchRpc",
        "/other.v1.SitesService/CreateSite",
        "maran.agent.v1.SitesService/CreateSite",
        "/maran.agent.v1.SitesService/CreateSite ",
        "/maran.agent.v1.SitesService/createsite",
        "/maran.agent.v1.SitesService/CreateSite\n",
    ] {
        assert_eq!(AuditRpcClass::of(path), None, "{path:?}");
    }
}
