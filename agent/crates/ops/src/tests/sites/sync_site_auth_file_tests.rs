//! Tests for the `sync_site_auth_file` module: the credential file follows the
//! options, is written only when it differs, and goes away with the last user.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::web::basic_auth_user_name::BasicAuthUserName;
use maran_agent_core::validation::web::bcrypt_hash::BcryptHash;

use super::sync_site_auth_file;
use crate::sites::fake_site_host::{
    FakeSiteHost, TEST_BCRYPT_HASH, distro, php_input, php_input_with_options,
};
use crate::sites::model::basic_auth_user::BasicAuthUser;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_options::SiteOptions;

/// The expected credential file of the fixture site, `alice` only.
fn alice_file() -> String {
    format!("alice:{TEST_BCRYPT_HASH}\n")
}

#[test]
fn a_site_with_users_gets_one_line_per_user_in_the_agents_own_directory() {
    let host = FakeSiteHost::passing();
    let input = php_input_with_options();

    sync_site_auth_file(&host, distro(), &input).unwrap();

    let path = AgentPaths::site_auth_file(&input.domain);
    assert_eq!(host.auth_file(&path), Some(alice_file()));
    // The group the directory is handed to is the adapter's, not a literal.
    assert_eq!(host.auth_groups(), [distro().web_server_group()]);
}

#[test]
fn a_file_that_is_already_right_is_not_written_again() {
    let host = FakeSiteHost::passing();
    let input = php_input_with_options();
    sync_site_auth_file(&host, distro(), &input).unwrap();
    let before = host.write_order().len();

    sync_site_auth_file(&host, distro(), &input).unwrap();

    // The panel retries after a timeout; a second write would be a second
    // `nginx -t` for nothing.
    assert_eq!(host.write_order().len(), before);
}

#[test]
fn a_changed_user_list_rewrites_the_file() {
    let host = FakeSiteHost::passing();
    let input = php_input_with_options();
    sync_site_auth_file(&host, distro(), &input).unwrap();
    let second = BasicAuthUser::new(
        BasicAuthUserName::parse("bob").unwrap(),
        BcryptHash::parse(TEST_BCRYPT_HASH).unwrap(),
    );
    let options = SiteOptions::new(
        None,
        None,
        None,
        vec![],
        input
            .options
            .basic_auth()
            .iter()
            .cloned()
            .chain([second])
            .collect(),
        None,
    )
    .unwrap();
    let changed = CreateSiteInput { options, ..input };

    sync_site_auth_file(&host, distro(), &changed).unwrap();

    assert_eq!(
        host.auth_file(&AgentPaths::site_auth_file(&changed.domain)),
        Some(format!(
            "alice:{TEST_BCRYPT_HASH}\nbob:{TEST_BCRYPT_HASH}\n"
        ))
    );
}

#[test]
fn a_site_without_users_has_no_file_and_the_last_user_taking_it_away() {
    let host = FakeSiteHost::passing();
    let with = php_input_with_options();
    let path = AgentPaths::site_auth_file(&with.domain);
    sync_site_auth_file(&host, distro(), &with).unwrap();
    assert!(host.auth_file(&path).is_some());

    let without = php_input();
    sync_site_auth_file(&host, distro(), &without).unwrap();

    assert_eq!(host.auth_file(&path), None);
}

#[test]
fn a_site_that_never_had_users_writes_nothing() {
    let host = FakeSiteHost::passing();

    sync_site_auth_file(&host, distro(), &php_input()).unwrap();

    assert!(host.write_order().is_empty());
}
