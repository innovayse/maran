//! Tests for the Debian family's app facts: the policy reading of the one `nodejs` package.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::nodejs_candidate_major;

#[test]
fn the_major_of_the_candidate_is_the_digits_before_the_first_dot() {
    let policy =
        "nodejs:\n  Installed: (none)\n  Candidate: 18.19.1+dfsg-6ubuntu5\n  Version table:\n";

    assert_eq!(nodejs_candidate_major(policy).as_deref(), Some("18"));
}

#[test]
fn an_epoch_is_dropped_before_the_major_is_read() {
    let policy = "nodejs:\n  Installed: (none)\n  Candidate: 2:20.11.1-1\n";

    assert_eq!(nodejs_candidate_major(policy).as_deref(), Some("20"));
}

#[test]
fn a_package_without_a_candidate_has_no_major() {
    let policy = "nodejs:\n  Installed: (none)\n  Candidate: (none)\n";

    assert_eq!(nodejs_candidate_major(policy), None);
}

#[test]
fn only_the_nodejs_stanza_is_read() {
    // Inverse control: another package's candidate must not be taken for nodejs's.
    let policy = "nginx:\n  Candidate: 1.24.0-2\nnodejs:\n  Candidate: 22.1.0-1\n";
    let other_only = "nginx:\n  Candidate: 1.24.0-2\nnodejs-doc:\n  Candidate: 22.1.0-1\n";

    assert_eq!(nodejs_candidate_major(policy).as_deref(), Some("22"));
    assert_eq!(nodejs_candidate_major(other_only), None);
}

#[test]
fn an_answer_with_no_stanza_has_no_major() {
    assert_eq!(nodejs_candidate_major(""), None);
}
