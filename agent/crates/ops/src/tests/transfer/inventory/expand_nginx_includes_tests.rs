//! Tests for [`expand_nginx_includes`]: following includes only into listed files.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::expand_nginx_includes::expand_nginx_includes;
use crate::transfer::inventory::inventory_fixtures::vhost_file;
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::nginx_token::NginxToken;
use crate::transfer::inventory::nginx_syntax_error::NginxSyntaxError;

/// Hand-written file A of an include loop (synthetic).
const LOOP_A: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_include_loop_a.conf"
);

/// Hand-written file B of an include loop (synthetic).
const LOOP_B: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_include_loop_b.conf"
);

/// The words of a token list, for comparison.
fn words(tokens: &[NginxToken]) -> Vec<&str> {
    tokens
        .iter()
        .filter_map(|token| match token {
            NginxToken::Word(word) => Some(word.as_str()),
            _ => None,
        })
        .collect()
}

/// An include of a listed file is replaced by that file's tokens.
#[test]
fn an_include_of_a_listed_file_is_inlined() {
    let root = vhost_file(
        "/etc/nginx/conf.d/root.conf",
        "a; include /etc/nginx/conf.d/part.conf; d;",
    );
    let part = vhost_file("/etc/nginx/conf.d/part.conf", "b; c;");

    let tokens = expand_nginx_includes(&root, &[root.clone(), part]).unwrap();

    assert_eq!(words(&tokens), ["a", "b", "c", "d"]);
}

/// Inverse control: an include of a file the listing did not name is dropped and
/// nothing is read for it.
#[test]
fn an_include_of_an_unlisted_file_is_dropped() {
    let root = vhost_file(
        "/etc/nginx/conf.d/root.conf",
        "a; include /etc/nginx/snippets/x.conf; d;",
    );

    let tokens = expand_nginx_includes(&root, std::slice::from_ref(&root)).unwrap();

    assert_eq!(words(&tokens), ["a", "d"]);
}

/// A pattern include takes every listed file it matches, in path order, and
/// leaves the unmatched ones.
#[test]
fn a_pattern_include_takes_matches_in_path_order() {
    let root = vhost_file(
        "/etc/nginx/sites-enabled/00root",
        "include /etc/nginx/extra/*.conf;",
    );
    let two = vhost_file("/etc/nginx/extra/b.conf", "two;");
    let one = vhost_file("/etc/nginx/extra/a.conf", "one;");
    let other = vhost_file("/etc/nginx/extra/c.txt", "no;");

    let tokens = expand_nginx_includes(&root, &[root.clone(), two, one, other]).unwrap();

    assert_eq!(words(&tokens), ["one", "two"]);
}

/// An include word in the middle of a statement is an argument, not a directive.
#[test]
fn include_in_the_middle_of_a_statement_is_not_followed() {
    let root = vhost_file(
        "/etc/nginx/conf.d/root.conf",
        "add_header include /etc/nginx/conf.d/part.conf;",
    );
    let part = vhost_file("/etc/nginx/conf.d/part.conf", "inlined;");

    let tokens = expand_nginx_includes(&root, &[root.clone(), part]).unwrap();

    assert_eq!(
        words(&tokens),
        ["add_header", "include", "/etc/nginx/conf.d/part.conf"]
    );
}

/// Two files that include each other are refused, from either end.
#[test]
fn an_include_loop_is_refused() {
    let a = vhost_file("/etc/nginx/sites-enabled/loop_a.conf", LOOP_A);
    let b = vhost_file("/etc/nginx/sites-enabled/loop_b.conf", LOOP_B);
    let files = [a.clone(), b.clone()];

    assert_eq!(
        expand_nginx_includes(&a, &files),
        Err(NginxSyntaxError::IncludeLoop)
    );
    assert_eq!(
        expand_nginx_includes(&b, &files),
        Err(NginxSyntaxError::IncludeLoop)
    );
}

/// A file that includes itself is a loop.
#[test]
fn a_self_include_is_refused() {
    let own = vhost_file(
        "/etc/nginx/conf.d/own.conf",
        "include /etc/nginx/conf.d/own.conf;",
    );

    assert_eq!(
        expand_nginx_includes(&own, std::slice::from_ref(&own)),
        Err(NginxSyntaxError::IncludeLoop)
    );
}

/// A chain no deeper than the bound is followed; one more link is refused.
#[test]
fn the_depth_bound_is_held_and_its_edge_is_accepted() {
    let chain = |links: usize| -> Vec<_> {
        (0..=links)
            .map(|index| {
                let content = if index == links {
                    "end;".to_owned()
                } else {
                    format!("include /etc/nginx/chain/f{}.conf;", index + 1)
                };
                vhost_file(&format!("/etc/nginx/chain/f{index}.conf"), &content)
            })
            .collect()
    };

    let at_bound = chain(InventoryLimits::MAX_INCLUDE_DEPTH);
    let past_bound = chain(InventoryLimits::MAX_INCLUDE_DEPTH + 1);

    assert_eq!(
        words(&expand_nginx_includes(&at_bound[0], &at_bound).unwrap()),
        ["end"]
    );
    assert_eq!(
        expand_nginx_includes(&past_bound[0], &past_bound),
        Err(NginxSyntaxError::IncludeTooDeep)
    );
}

/// A fan-out that would multiply into more work than the bound is refused
/// instead of run.
#[test]
fn a_fan_out_past_the_work_bound_is_refused() {
    let mut files = vec![vhost_file(
        "/etc/nginx/fan/root.conf",
        "include /etc/nginx/fan/*.inc;",
    )];
    for index in 0..40 {
        files.push(vhost_file(
            &format!("/etc/nginx/fan/{index:02}.inc"),
            "include /etc/nginx/fan2/*.inc;",
        ));
        files.push(vhost_file(
            &format!("/etc/nginx/fan2/{index:02}.inc"),
            "include /etc/nginx/fan3/*.inc;",
        ));
        files.push(vhost_file(&format!("/etc/nginx/fan3/{index:02}.inc"), "x;"));
    }

    assert_eq!(
        expand_nginx_includes(&files[0], &files),
        Err(NginxSyntaxError::TooMuchWork)
    );
}

/// Unreadable content of an included file surfaces as its error.
#[test]
fn a_broken_included_file_is_refused() {
    let root = vhost_file(
        "/etc/nginx/conf.d/root.conf",
        "include /etc/nginx/conf.d/bad.conf;",
    );
    let bad = vhost_file("/etc/nginx/conf.d/bad.conf", "name \"open");

    assert_eq!(
        expand_nginx_includes(&root, &[root.clone(), bad]),
        Err(NginxSyntaxError::UnterminatedQuote)
    );
}
