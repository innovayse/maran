//! Tests for the `redirect_rule` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::redirect_path::RedirectPath;
use maran_agent_core::validation::web::redirect_target::RedirectTarget;

use super::RedirectRule;
use crate::sites::model::redirect_match::RedirectMatch;
use crate::sites::model::redirect_status::RedirectStatus;
use crate::sites::model::site_options_error::SiteOptionsError;

/// Builds a rule from texts and a match.
fn rule(
    path: &str,
    matching: RedirectMatch,
    target: &str,
) -> Result<RedirectRule, SiteOptionsError> {
    RedirectRule::new(
        RedirectPath::parse(path).unwrap(),
        matching,
        RedirectTarget::parse(target).unwrap(),
        RedirectStatus::Permanent,
    )
}

#[test]
fn an_exact_rule_needs_no_trailing_slash() {
    let built = rule("/old", RedirectMatch::Exact, "https://new.example.org/page").unwrap();

    assert_eq!(built.path().as_str(), "/old");
    assert_eq!(built.matching(), RedirectMatch::Exact);
    assert_eq!(built.target().as_str(), "https://new.example.org/page");
    assert_eq!(built.status(), RedirectStatus::Permanent);
}

#[test]
fn a_prefix_rule_needs_a_path_and_a_target_that_end_with_a_slash() {
    assert!(rule("/blog/", RedirectMatch::Prefix, "https://new.example.org/").is_ok());
    assert_eq!(
        rule("/blog", RedirectMatch::Prefix, "/new/"),
        Err(SiteOptionsError::PrefixPathNotDirectory)
    );
    assert_eq!(
        rule("/blog/", RedirectMatch::Prefix, "https://new.example.org"),
        Err(SiteOptionsError::PrefixTargetNotDirectory)
    );
}
