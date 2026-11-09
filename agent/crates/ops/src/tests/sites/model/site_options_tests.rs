//! Tests for the `site_options` module: the caps, the duplicates and the
//! checks that need the site the options are for.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::basic_auth_user_name::BasicAuthUserName;
use maran_agent_core::validation::web::bcrypt_hash::BcryptHash;
use maran_agent_core::validation::web::client_body_size::ClientBodySize;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_agent_core::validation::web::redirect_path::RedirectPath;
use maran_agent_core::validation::web::redirect_target::RedirectTarget;
use maran_agent_core::validation::web::static_cache_days::StaticCacheDays;
use maran_agent_core::validation::web::upstream::Upstream;

use super::{MAXIMUM_REDIRECTS, MAXIMUM_USERS, SiteOptions};
use crate::sites::model::basic_auth_user::BasicAuthUser;
use crate::sites::model::canonical_host::CanonicalHost;
use crate::sites::model::redirect_match::RedirectMatch;
use crate::sites::model::redirect_rule::RedirectRule;
use crate::sites::model::redirect_status::RedirectStatus;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_options_error::SiteOptionsError;

/// A syntactically valid bcrypt hash that belongs to no password.
const HASH: &str = "$2y$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXY./";

/// A domain for the site under test.
fn domain(name: &str) -> Domain {
    Domain::parse(name).unwrap()
}

/// An exact redirect from `path` to `/target`.
fn exact(path: &str) -> RedirectRule {
    RedirectRule::new(
        RedirectPath::parse(path).unwrap(),
        RedirectMatch::Exact,
        RedirectTarget::parse("/target").unwrap(),
        RedirectStatus::Permanent,
    )
    .unwrap()
}

/// A user called `name`.
fn user(name: &str) -> BasicAuthUser {
    BasicAuthUser::new(
        BasicAuthUserName::parse(name).unwrap(),
        BcryptHash::parse(HASH).unwrap(),
    )
}

/// Options holding only `redirects` and `users`.
fn options(
    redirects: Vec<RedirectRule>,
    users: Vec<BasicAuthUser>,
) -> Result<SiteOptions, SiteOptionsError> {
    SiteOptions::new(None, None, None, redirects, users, None)
}

/// Options that choose `host` as canonical.
fn canonical(host: CanonicalHost) -> SiteOptions {
    SiteOptions::new(Some(host), None, None, vec![], vec![], None).unwrap()
}

#[test]
fn a_site_with_no_options_is_the_default() {
    let none = SiteOptions::default();

    assert_eq!(none.canonical_host(), None);
    assert!(none.redirects().is_empty());
    assert!(none.basic_auth().is_empty());
    assert!(none.ip_access().is_none());
}

#[test]
fn redirects_up_to_the_cap_are_accepted_and_one_more_is_refused() {
    let at_cap: Vec<RedirectRule> = (0..MAXIMUM_REDIRECTS)
        .map(|index| exact(&format!("/p{index}")))
        .collect();
    assert!(options(at_cap, vec![]).is_ok());

    let over: Vec<RedirectRule> = (0..=MAXIMUM_REDIRECTS)
        .map(|index| exact(&format!("/p{index}")))
        .collect();
    assert_eq!(
        options(over, vec![]),
        Err(SiteOptionsError::TooManyRedirects {
            count: MAXIMUM_REDIRECTS + 1,
            maximum: MAXIMUM_REDIRECTS,
        })
    );
}

#[test]
fn two_rules_with_the_same_path_and_match_are_refused_but_different_matches_are_not() {
    assert_eq!(
        options(vec![exact("/old"), exact("/old")], vec![]),
        Err(SiteOptionsError::DuplicateRedirect)
    );

    let prefix = RedirectRule::new(
        RedirectPath::parse("/old/").unwrap(),
        RedirectMatch::Prefix,
        RedirectTarget::parse("/new/").unwrap(),
        RedirectStatus::Temporary,
    )
    .unwrap();
    assert!(options(vec![exact("/old/"), prefix], vec![]).is_ok());
}

#[test]
fn users_up_to_the_cap_are_accepted_and_one_more_is_refused() {
    let at_cap: Vec<BasicAuthUser> = (0..MAXIMUM_USERS)
        .map(|index| user(&format!("user{index}")))
        .collect();
    assert!(options(vec![], at_cap).is_ok());

    let over: Vec<BasicAuthUser> = (0..=MAXIMUM_USERS)
        .map(|index| user(&format!("user{index}")))
        .collect();
    assert_eq!(
        options(vec![], over),
        Err(SiteOptionsError::TooManyUsers {
            count: MAXIMUM_USERS + 1,
            maximum: MAXIMUM_USERS,
        })
    );
}

#[test]
fn two_users_with_the_same_name_are_refused() {
    assert_eq!(
        options(vec![], vec![user("alice"), user("alice")]),
        Err(SiteOptionsError::DuplicateUser)
    );
}

#[test]
fn an_upload_size_on_a_static_site_is_refused_and_on_the_other_kinds_accepted() {
    let with_size = SiteOptions::new(
        None,
        Some(ClientBodySize::parse(10).unwrap()),
        None,
        vec![],
        vec![],
        None,
    )
    .unwrap();
    let site = domain("example.com");
    let php = SiteKind::Php {
        version: PhpVersion::parse("8.3").unwrap(),
    };
    let proxy = SiteKind::ReverseProxy {
        upstream: Upstream::parse("127.0.0.1:3000").unwrap(),
    };

    assert_eq!(
        with_size.check_against(&SiteKind::Static, &site, &[]),
        Err(SiteOptionsError::BodySizeOnStaticSite)
    );
    assert_eq!(with_size.check_against(&php, &site, &[]), Ok(()));
    assert_eq!(with_size.check_against(&proxy, &site, &[]), Ok(()));
}

#[test]
fn a_cache_rule_on_a_proxied_site_is_refused_and_on_the_other_kinds_accepted() {
    let with_cache = SiteOptions::new(
        None,
        None,
        Some(StaticCacheDays::parse(7).unwrap()),
        vec![],
        vec![],
        None,
    )
    .unwrap();
    let site = domain("example.com");
    let php = SiteKind::Php {
        version: PhpVersion::parse("8.3").unwrap(),
    };
    let proxy = SiteKind::ReverseProxy {
        upstream: Upstream::parse("127.0.0.1:3000").unwrap(),
    };

    assert_eq!(
        with_cache.check_against(&proxy, &site, &[]),
        Err(SiteOptionsError::CacheOnProxiedSite)
    );
    assert_eq!(with_cache.check_against(&php, &site, &[]), Ok(()));
    assert_eq!(
        with_cache.check_against(&SiteKind::Static, &site, &[]),
        Ok(())
    );
}

#[test]
fn a_www_preference_redirects_the_apex_and_serves_the_rest_under_the_www_name() {
    let split = canonical(CanonicalHost::Www)
        .canonical_split(
            &domain("example.com"),
            &[domain("www.example.com"), domain("blog.example.com")],
        )
        .unwrap()
        .unwrap();

    assert_eq!(split.canonical, "www.example.com");
    assert_eq!(split.redirected, ["example.com"]);
    assert_eq!(split.served, ["www.example.com", "blog.example.com"]);
}

#[test]
fn an_apex_preference_works_from_a_site_registered_under_its_www_name() {
    let split = canonical(CanonicalHost::Apex)
        .canonical_split(&domain("www.example.com"), &[domain("example.com")])
        .unwrap()
        .unwrap();

    assert_eq!(split.canonical, "example.com");
    assert_eq!(split.redirected, ["www.example.com"]);
    assert_eq!(split.served, ["example.com"]);
}

#[test]
fn a_canonical_host_the_site_does_not_serve_is_refused() {
    assert_eq!(
        canonical(CanonicalHost::Www).canonical_split(&domain("example.com"), &[]),
        Err(SiteOptionsError::CanonicalHostNotServed {
            wanted: "www.example.com".to_owned(),
        })
    );
}

#[test]
fn a_canonical_host_with_nothing_to_redirect_splits_nothing() {
    // The site serves only the canonical name of the pair: no second server
    // block is needed.
    let split = canonical(CanonicalHost::Apex)
        .canonical_split(&domain("example.com"), &[domain("shop.example.com")])
        .unwrap();

    assert_eq!(split, None);
}

#[test]
fn no_canonical_host_splits_nothing() {
    let split = SiteOptions::default()
        .canonical_split(&domain("example.com"), &[domain("www.example.com")])
        .unwrap();

    assert_eq!(split, None);
}

#[test]
fn options_start_with_no_request_limit_and_take_one_without_touching_the_rest() {
    let rate = maran_agent_core::validation::web::request_rate::RequestRate::parse(20, 40).unwrap();
    let base =
        SiteOptions::new(Some(CanonicalHost::Www), None, None, vec![], vec![], None).unwrap();
    assert_eq!(base.request_rate(), None);
    assert_eq!(SiteOptions::default().request_rate(), None);

    let limited = base.clone().with_request_rate(Some(rate));

    assert_eq!(limited.request_rate(), Some(rate));
    assert_eq!(limited.canonical_host(), base.canonical_host());
    assert_eq!(limited.with_request_rate(None), base);
}
