//! Tests for the `site_auth_file` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SiteAuthFile;
use crate::nginx::auth_line::AuthLine;

#[test]
fn each_user_is_one_line_ending_in_a_newline() {
    let users = [
        AuthLine {
            name: "alice",
            hash: "$2y$12$abc",
        },
        AuthLine {
            name: "bob",
            hash: "$2b$10$def",
        },
    ];

    assert_eq!(
        SiteAuthFile { users: &users }.render_config().unwrap(),
        "alice:$2y$12$abc\nbob:$2b$10$def\n"
    );
}

#[test]
fn no_users_render_an_empty_file() {
    assert_eq!(SiteAuthFile { users: &[] }.render_config().unwrap(), "");
}
