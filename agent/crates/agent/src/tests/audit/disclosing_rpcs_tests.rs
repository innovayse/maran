//! Tests for the list of disclosing rpcs: the one that hands file contents back
//! is in it, and nothing that only lists names or changes the host has crept in.
//! Coverage against the contract is the census in `audit_rpc_class_tests.rs`.

use super::DISCLOSING_RPCS;
use crate::audit::mutating_rpcs::MUTATING_RPCS;
use crate::audit::read_only_rpcs::READ_ONLY_RPCS;

#[test]
fn reading_a_file_is_disclosing() {
    assert!(DISCLOSING_RPCS.contains(&"/maran.agent.v1.FilesService/ReadFile"));
}

#[test]
fn the_list_is_not_empty_and_is_made_of_full_paths_of_the_agent_package() {
    assert!(!DISCLOSING_RPCS.is_empty(), "positive control");
    for path in DISCLOSING_RPCS {
        assert!(path.starts_with("/maran.agent.v1."), "{path}");
        assert_eq!(path.matches('/').count(), 2, "{path}");
    }
}

#[test]
fn no_disclosing_rpc_is_also_in_another_list() {
    for path in DISCLOSING_RPCS {
        assert!(!MUTATING_RPCS.contains(path), "{path} is also mutating");
        assert!(!READ_ONLY_RPCS.contains(path), "{path} is also read-only");
    }
}

#[test]
fn a_listing_and_a_search_are_not_disclosing() {
    for method in ["ListDirectory", "SearchEntries"] {
        assert!(
            !DISCLOSING_RPCS
                .iter()
                .any(|path| path.ends_with(&format!("/{method}"))),
            "{method} discloses names only and a file manager polls it"
        );
    }
}
