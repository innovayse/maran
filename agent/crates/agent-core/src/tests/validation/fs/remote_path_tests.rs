//! Tests for the `remote_path` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{RemotePath, RemotePathError};

#[test]
fn ordinary_absolute_paths_are_accepted_unchanged() {
    for candidate in [
        "/",
        "/home/shop",
        "/home/shop/public_html",
        "/var/www/site.example.com/html/",
        "/srv/a@b/c:d/e+f-g/h_i",
        "/home/shop/./sub",
        "/home/.hidden/file.tar.gz",
    ] {
        assert_eq!(RemotePath::parse(candidate).unwrap().as_str(), candidate);
    }
}

#[test]
fn an_empty_candidate_is_refused() {
    assert_eq!(RemotePath::parse(""), Err(RemotePathError::Empty));
}

#[test]
fn a_relative_path_is_refused() {
    for candidate in ["home/shop", "./home", "../etc", "-rf", "~/x"] {
        assert_eq!(
            RemotePath::parse(candidate),
            Err(RemotePathError::NotAbsolute),
            "{candidate}"
        );
    }
}

#[test]
fn a_path_of_exactly_4096_bytes_is_accepted_and_one_more_is_refused() {
    let at_limit = format!("/{}", "a".repeat(4095));
    assert!(RemotePath::parse(&at_limit).is_ok());

    assert_eq!(
        RemotePath::parse(&format!("{at_limit}a")),
        Err(RemotePathError::TooLong { limit: 4096 })
    );
}

#[test]
fn shell_metacharacters_whitespace_and_control_characters_are_refused() {
    for candidate in [
        "/home/a b",
        "/home/a;id",
        "/home/a$(id)",
        "/home/a`id`",
        "/home/a|b",
        "/home/a&b",
        "/home/a>b",
        "/home/a<b",
        "/home/a'b",
        "/home/a\"b",
        "/home/a\\b",
        "/home/a*",
        "/home/a?",
        "/home/~root",
        "/home/a\nb",
        "/home/a\0b",
        "/home/a\tb",
        "/home/a%b",
        "/home/a#b",
        "/home/a=b",
        "/home/a,b",
        "/home/caf\u{e9}",
    ] {
        assert_eq!(
            RemotePath::parse(candidate),
            Err(RemotePathError::IllegalCharacter),
            "{candidate:?}"
        );
    }
}

#[test]
fn a_double_slash_is_refused_but_a_single_trailing_slash_is_not() {
    for candidate in ["//home", "/home//shop", "/home/shop//"] {
        assert_eq!(
            RemotePath::parse(candidate),
            Err(RemotePathError::EmptyComponent),
            "{candidate}"
        );
    }
    assert!(RemotePath::parse("/home/shop/").is_ok());
}

#[test]
fn a_parent_component_is_refused_anywhere_but_a_name_containing_dots_is_not() {
    for candidate in ["/..", "/home/../etc", "/home/shop/..", "/../.."] {
        assert_eq!(
            RemotePath::parse(candidate),
            Err(RemotePathError::ParentComponent),
            "{candidate}"
        );
    }
    for candidate in ["/home/..shop", "/home/shop..", "/home/a..b", "/home/..."] {
        assert!(RemotePath::parse(candidate).is_ok(), "{candidate}");
    }
}

#[test]
fn a_component_starting_with_a_dash_is_refused_but_a_dash_inside_is_not() {
    for candidate in ["/-rf", "/home/-x", "/home/shop/--help", "/home/-"] {
        assert_eq!(
            RemotePath::parse(candidate),
            Err(RemotePathError::LeadingDash),
            "{candidate}"
        );
    }
    assert!(RemotePath::parse("/home/a-b/c-").is_ok());
}

#[test]
fn a_path_is_inside_its_ancestors_by_component_and_not_by_text_prefix() {
    let home = RemotePath::parse("/home/a").unwrap();

    assert!(RemotePath::parse("/home/a").unwrap().is_inside(&home));
    assert!(RemotePath::parse("/home/a/").unwrap().is_inside(&home));
    assert!(
        RemotePath::parse("/home/a/public_html")
            .unwrap()
            .is_inside(&home)
    );
    // The inverse controls: a text prefix, a sibling and a parent are not inside.
    assert!(!RemotePath::parse("/home/ab").unwrap().is_inside(&home));
    assert!(!RemotePath::parse("/home/b/a").unwrap().is_inside(&home));
    assert!(!RemotePath::parse("/home").unwrap().is_inside(&home));
    assert!(!RemotePath::parse("/").unwrap().is_inside(&home));
    // Everything is inside the root.
    assert!(home.is_inside(&RemotePath::parse("/").unwrap()));
}
