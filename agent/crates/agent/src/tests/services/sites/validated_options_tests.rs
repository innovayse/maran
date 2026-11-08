//! Tests for the `validated_options` module: the agent's own revalidation of a
//! site's options, which decides whatever the panel sent.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::sites::model::canonical_host::CanonicalHost;
use maran_ops::sites::model::ip_access_mode::IpAccessMode;
use maran_ops::sites::model::redirect_match::RedirectMatch;
use maran_ops::sites::model::redirect_status::RedirectStatus;

use super::validated_options;
use crate::proto::{
    BasicAuth, BasicAuthUser, CanonicalHost as WireCanonicalHost, ErrorCode, IpAccess,
    IpAccessMode as WireIpAccessMode, RedirectMatch as WireMatch, RedirectRule, SiteOptions,
};

/// A syntactically valid bcrypt hash that belongs to no password.
const HASH: &str = "$2y$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXY./";

/// An exact redirect rule.
fn rule(path: &str, target: &str, status: u32) -> RedirectRule {
    RedirectRule {
        path: path.to_owned(),
        r#match: WireMatch::Exact as i32,
        target: target.to_owned(),
        status,
    }
}

/// A user with a valid hash.
fn user(name: &str) -> BasicAuthUser {
    BasicAuthUser {
        name: name.to_owned(),
        password_hash: HASH.to_owned(),
    }
}

/// The code an options message is refused with.
fn refusal(options: &SiteOptions) -> i32 {
    let error = validated_options(Some(options)).unwrap_err();
    assert_eq!(
        error.code,
        ErrorCode::InvalidInput as i32,
        "{}",
        error.message
    );
    error.code
}

#[test]
fn an_absent_message_and_an_empty_one_are_a_site_with_no_options() {
    assert_eq!(validated_options(None).unwrap().redirects().len(), 0);
    let empty = validated_options(Some(&SiteOptions::default())).unwrap();

    assert_eq!(empty, maran_ops::sites::SiteOptions::default());
}

#[test]
fn every_option_is_carried_into_the_validated_type() {
    let options = SiteOptions {
        canonical_host: WireCanonicalHost::Www as i32,
        client_max_body_size_mib: 64,
        static_cache_days: 30,
        redirects: vec![
            rule("/old", "/new", 301),
            RedirectRule {
                path: "/blog/".to_owned(),
                r#match: WireMatch::Prefix as i32,
                target: "https://blog.example.org/".to_owned(),
                status: 302,
            },
        ],
        basic_auth: Some(BasicAuth {
            users: vec![user("alice"), user("bob")],
        }),
        ip_access: Some(IpAccess {
            mode: WireIpAccessMode::DenyList as i32,
            cidrs: vec!["203.0.113.0/24".to_owned(), "2001:db8::/32".to_owned()],
        }),
        request_rate_per_second: 25,
        request_burst: 10,
    };

    let built = validated_options(Some(&options)).unwrap();

    assert_eq!(built.canonical_host(), Some(CanonicalHost::Www));
    assert_eq!(built.client_max_body_size().unwrap().mebibytes(), 64);
    assert_eq!(built.static_cache_days().unwrap().days(), 30);
    assert_eq!(built.redirects().len(), 2);
    assert_eq!(built.redirects()[0].status(), RedirectStatus::Permanent);
    assert_eq!(built.redirects()[1].matching(), RedirectMatch::Prefix);
    assert_eq!(built.redirects()[1].status(), RedirectStatus::Temporary);
    assert_eq!(built.basic_auth().len(), 2);
    assert_eq!(built.ip_access().unwrap().mode(), IpAccessMode::DenyList);
    assert_eq!(built.ip_access().unwrap().networks().len(), 2);
    assert_eq!(built.request_rate().unwrap().per_second(), 25);
    assert_eq!(built.request_rate().unwrap().burst(), 10);
}

#[test]
fn no_rate_and_no_burst_is_a_site_with_no_request_limit() {
    let built = validated_options(Some(&SiteOptions::default())).unwrap();

    assert_eq!(built.request_rate(), None);
}

#[test]
fn a_rate_with_no_burst_is_a_limit_that_allows_no_burst() {
    let built = validated_options(Some(&SiteOptions {
        request_rate_per_second: 5,
        ..SiteOptions::default()
    }))
    .unwrap();

    let rate = built.request_rate().unwrap();
    assert_eq!((rate.per_second(), rate.burst()), (5, 0));
}

#[test]
fn a_burst_with_no_rate_is_refused_and_not_read_as_no_limit() {
    refusal(&SiteOptions {
        request_burst: 10,
        ..SiteOptions::default()
    });
}

#[test]
fn a_rate_or_a_burst_above_its_maximum_is_refused() {
    refusal(&SiteOptions {
        request_rate_per_second: 1001,
        ..SiteOptions::default()
    });
    refusal(&SiteOptions {
        request_rate_per_second: 10,
        request_burst: 1001,
        ..SiteOptions::default()
    });
}

#[test]
fn a_redirect_that_could_end_the_directive_is_refused() {
    for target in [
        "/a;b",
        "/a{b",
        "https://example.org/$uri",
        "javascript:alert",
        "//evil.example",
    ] {
        let options = SiteOptions {
            redirects: vec![rule("/old", target, 301)],
            ..SiteOptions::default()
        };

        refusal(&options);
    }
    let options = SiteOptions {
        redirects: vec![rule("/old;\ndeny all", "/new", 301)],
        ..SiteOptions::default()
    };
    refusal(&options);
}

#[test]
fn a_redirect_under_the_challenge_directory_is_refused() {
    let options = SiteOptions {
        redirects: vec![rule("/.well-known/acme-challenge/token", "/new", 301)],
        ..SiteOptions::default()
    };

    refusal(&options);
}

#[test]
fn an_unknown_match_or_status_is_refused() {
    refusal(&SiteOptions {
        redirects: vec![rule("/old", "/new", 307)],
        ..SiteOptions::default()
    });
    refusal(&SiteOptions {
        redirects: vec![RedirectRule {
            r#match: WireMatch::Unspecified as i32,
            ..rule("/old", "/new", 301)
        }],
        ..SiteOptions::default()
    });
    refusal(&SiteOptions {
        redirects: vec![RedirectRule {
            r#match: 99,
            ..rule("/old", "/new", 301)
        }],
        ..SiteOptions::default()
    });
}

#[test]
fn more_redirects_than_the_cap_are_refused_before_any_is_parsed() {
    // The paths are not even valid: the cap refuses first, which is the point.
    let redirects = (0..26).map(|_| rule("not a path", "/x", 301)).collect();

    let error = validated_options(Some(&SiteOptions {
        redirects,
        ..SiteOptions::default()
    }))
    .unwrap_err();

    assert!(error.message.contains("at most 25"), "{}", error.message);
}

#[test]
fn an_unknown_canonical_host_is_refused() {
    refusal(&SiteOptions {
        canonical_host: 7,
        ..SiteOptions::default()
    });
}

#[test]
fn an_upload_size_or_a_cache_lifetime_out_of_range_is_refused() {
    refusal(&SiteOptions {
        client_max_body_size_mib: 1025,
        ..SiteOptions::default()
    });
    refusal(&SiteOptions {
        static_cache_days: 366,
        ..SiteOptions::default()
    });
}

#[test]
fn a_basic_auth_message_with_no_users_is_refused_and_not_read_as_off() {
    refusal(&SiteOptions {
        basic_auth: Some(BasicAuth { users: vec![] }),
        ..SiteOptions::default()
    });
}

#[test]
fn a_user_name_that_adds_a_field_or_a_line_is_refused() {
    for name in ["al:ice", "alice\nmallory", ""] {
        refusal(&SiteOptions {
            basic_auth: Some(BasicAuth {
                users: vec![user(name)],
            }),
            ..SiteOptions::default()
        });
    }
}

#[test]
fn a_weak_or_malformed_hash_is_refused_without_echoing_it() {
    let with_second_user = format!("{HASH}\nx:y");
    for hash in [
        "$apr1$abc$def",
        "{SHA}abc=",
        "plain",
        with_second_user.as_str(),
    ] {
        let options = SiteOptions {
            basic_auth: Some(BasicAuth {
                users: vec![BasicAuthUser {
                    name: "alice".to_owned(),
                    password_hash: hash.to_owned(),
                }],
            }),
            ..SiteOptions::default()
        };

        let error = validated_options(Some(&options)).unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        assert!(!error.message.contains(hash), "{}", error.message);
    }
}

#[test]
fn more_users_than_the_cap_and_a_repeated_user_are_refused() {
    let users = (0..11).map(|index| user(&format!("user{index}"))).collect();
    refusal(&SiteOptions {
        basic_auth: Some(BasicAuth { users }),
        ..SiteOptions::default()
    });
    refusal(&SiteOptions {
        basic_auth: Some(BasicAuth {
            users: vec![user("alice"), user("alice")],
        }),
        ..SiteOptions::default()
    });
}

#[test]
fn an_ip_list_needs_a_mode_and_a_network_or_neither() {
    // Neither: no IP rule.
    assert!(
        validated_options(Some(&SiteOptions {
            ip_access: Some(IpAccess::default()),
            ..SiteOptions::default()
        }))
        .unwrap()
        .ip_access()
        .is_none()
    );
    // A mode with no network would lock everyone out or do nothing.
    refusal(&SiteOptions {
        ip_access: Some(IpAccess {
            mode: WireIpAccessMode::AllowList as i32,
            cidrs: vec![],
        }),
        ..SiteOptions::default()
    });
    // Networks with no mode.
    refusal(&SiteOptions {
        ip_access: Some(IpAccess {
            mode: 0,
            cidrs: vec!["203.0.113.0/24".to_owned()],
        }),
        ..SiteOptions::default()
    });
}

#[test]
fn a_network_that_is_not_canonical_and_a_repeated_one_are_refused() {
    for cidrs in [
        vec!["203.0.113.7/24".to_owned()],
        vec!["203.0.113.0/24;".to_owned()],
        vec!["example.com/24".to_owned()],
        vec!["203.0.113.0/24".to_owned(), "203.0.113.0/24".to_owned()],
    ] {
        refusal(&SiteOptions {
            ip_access: Some(IpAccess {
                mode: WireIpAccessMode::AllowList as i32,
                cidrs,
            }),
            ..SiteOptions::default()
        });
    }
}
