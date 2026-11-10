//! Tests for [`converge_php_site`].
//!
//! What they pin is which files the convergence decides to write: only the
//! ones that differ from today's render, the pool before the vhost, and never
//! a suspended site's stub.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::SiteWebServer;
use std::path::PathBuf;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::php::fake_php_host::FakePhpHost;
use crate::php::model::pool_input::PoolInput;
use crate::php::write_pool;
use crate::sites::converge_php_site::converge_php_site;
use crate::sites::fake_site_host::{
    FakeSiteHost, create_test_site, distro, php_input, php_input_with_options,
};
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::php_switch::PhpSwitch;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::{SitesOpError, disable_site};

/// The worker budget the pool is written and converged with.
const WORKERS: u32 = 10;

/// A directory the socket path of an earlier release stands in for.
const OLD_SOCKET_DIRECTORY: &str = "/run/php";

/// A site that is current on disk: its vhost created, its pool written.
struct Fixture {
    /// The in-memory nginx side.
    site_host: FakeSiteHost,
    /// The in-memory php-fpm side.
    php_host: FakePhpHost,
    /// The PHP site under test.
    site: CreateSiteInput,
    /// Where the vhost lives.
    vhost_path: PathBuf,
    /// Where the 8.3 pool lives.
    pool_path: PathBuf,
}

impl Fixture {
    /// Creates the site and writes its pool, so both files equal today's render.
    fn current() -> Self {
        let site_host = FakeSiteHost::passing();
        let php_host = FakePhpHost::with_installed(&["8.3"]);
        let site = php_input();
        create_test_site(&site_host, &site).unwrap();
        write_pool(
            &php_host,
            distro(),
            &PoolInput {
                account: site.account.clone(),
                version: PhpVersion::parse("8.3").unwrap(),
                max_children: WORKERS,
                overrides: Vec::new(),
            },
        )
        .unwrap();
        let vhost_path =
            SitePaths::for_site(&site.account, &site.domain, SiteWebServer::Nginx).config_path;
        let pool_path = PathBuf::from(format!(
            "/etc/php/8.3/fpm/pool.d/{}.conf",
            site.account.as_str()
        ));

        Self {
            site_host,
            php_host,
            site,
            vhost_path,
            pool_path,
        }
    }

    /// Rewrites the pool on disk as an earlier release would have left it, and
    /// returns the new text.
    fn age_pool(&self) -> String {
        let current = self.php_host.config(&self.pool_path).unwrap();
        let aged = current.replace(AgentPaths::PHP_FPM_SOCKET_DIRECTORY, OLD_SOCKET_DIRECTORY);
        assert_ne!(aged, current, "the pool never named the socket directory");
        self.php_host.place_config(&self.pool_path, &aged);
        aged
    }

    /// Rewrites the vhost on disk as an earlier release would have left it, and
    /// returns the new text.
    fn age_vhost(&self) -> String {
        let current = self.site_host.config(&self.vhost_path).unwrap();
        let aged = current.replace(AgentPaths::PHP_FPM_SOCKET_DIRECTORY, OLD_SOCKET_DIRECTORY);
        assert_ne!(aged, current, "the vhost never named the socket directory");
        self.site_host.place_config(&self.vhost_path, &aged);
        aged
    }

    /// Converges the site on 8.3 with the fixture's own worker budget.
    fn converge(&self) -> Result<(), SitesOpError> {
        converge_php_site(
            &self.site_host,
            &self.php_host,
            distro(),
            &self.site,
            &PhpSwitch {
                version: &PhpVersion::parse("8.3").unwrap(),
                max_children: WORKERS,
                overrides: &[],
                remove_previous_pool: false,
            },
        )
    }
}

#[test]
fn a_site_that_is_already_current_is_left_alone_with_no_validator_and_no_reload() {
    let fixture = Fixture::current();
    let pool_writes = fixture.php_host.writes();
    let vhost_writes = fixture.site_host.writes();
    let pool_commands = fixture.php_host.protocol_commands().len();
    let vhost_commands = fixture.site_host.protocol().len();

    fixture.converge().unwrap();

    assert_eq!(fixture.php_host.writes(), pool_writes);
    assert_eq!(fixture.site_host.writes(), vhost_writes);
    assert_eq!(fixture.php_host.protocol_commands().len(), pool_commands);
    assert_eq!(fixture.site_host.protocol().len(), vhost_commands);
}

#[test]
fn a_pool_and_a_vhost_on_the_old_socket_path_are_both_rewritten() {
    let fixture = Fixture::current();
    let current_pool = fixture.php_host.config(&fixture.pool_path).unwrap();
    let current_vhost = fixture.site_host.config(&fixture.vhost_path).unwrap();
    fixture.age_pool();
    fixture.age_vhost();
    let pool_writes = fixture.php_host.writes();
    let vhost_writes = fixture.site_host.writes();

    fixture.converge().unwrap();

    assert_eq!(fixture.php_host.writes(), pool_writes + 1);
    assert_eq!(fixture.site_host.writes(), vhost_writes + 1);
    assert_eq!(
        fixture.php_host.config(&fixture.pool_path).unwrap(),
        current_pool
    );
    assert_eq!(
        fixture.site_host.config(&fixture.vhost_path).unwrap(),
        current_vhost
    );
}

#[test]
fn a_current_pool_with_an_old_vhost_rewrites_only_the_vhost() {
    let fixture = Fixture::current();
    let current_vhost = fixture.site_host.config(&fixture.vhost_path).unwrap();
    fixture.age_vhost();
    let pool_writes = fixture.php_host.writes();

    fixture.converge().unwrap();

    assert_eq!(
        fixture.php_host.writes(),
        pool_writes,
        "a pool that already equals its render must not be rewritten and reloaded"
    );
    assert_eq!(
        fixture.site_host.config(&fixture.vhost_path).unwrap(),
        current_vhost
    );
}

#[test]
fn an_old_pool_with_a_current_vhost_rewrites_only_the_pool() {
    let fixture = Fixture::current();
    let current_pool = fixture.php_host.config(&fixture.pool_path).unwrap();
    fixture.age_pool();
    let vhost_writes = fixture.site_host.writes();

    fixture.converge().unwrap();

    assert_eq!(fixture.site_host.writes(), vhost_writes);
    assert_eq!(
        fixture.php_host.config(&fixture.pool_path).unwrap(),
        current_pool
    );
}

#[test]
fn a_suspended_site_gets_its_pool_converged_and_its_stub_left_untouched() {
    let fixture = Fixture::current();
    disable_site(&fixture.site_host, distro(), &fixture.site).unwrap();
    let stub = fixture.site_host.config(&fixture.vhost_path).unwrap();
    let current_pool = fixture.php_host.config(&fixture.pool_path).unwrap();
    fixture.age_pool();
    let vhost_writes = fixture.site_host.writes();

    fixture.converge().unwrap();

    assert_eq!(
        fixture.php_host.config(&fixture.pool_path).unwrap(),
        current_pool
    );
    assert_eq!(fixture.site_host.config(&fixture.vhost_path).unwrap(), stub);
    assert_eq!(
        fixture.site_host.writes(),
        vhost_writes,
        "converging must not un-suspend a site"
    );
}

#[test]
fn a_site_without_a_vhost_is_not_found() {
    let site_host = FakeSiteHost::passing();
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let site = php_input();

    match converge_php_site(
        &site_host,
        &php_host,
        distro(),
        &site,
        &PhpSwitch {
            version: &PhpVersion::parse("8.3").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    ) {
        Err(SitesOpError::NotFound { domain }) => assert_eq!(domain, "example.com"),
        other => panic!("expected NotFound, got {other:?}"),
    }
    assert_eq!(php_host.writes(), 0);
}

#[test]
fn a_pool_that_php_fpm_refuses_is_rolled_back_and_the_vhost_is_not_touched() {
    // The pool goes first precisely so that a refusal here leaves the vhost on
    // the socket the old pool still serves.
    let fixture = Fixture::current();
    let old_pool = fixture.age_pool();
    let old_vhost = fixture.age_vhost();
    let vhost_writes = fixture.site_host.writes();
    fixture.php_host.reject_validation("pool refused");

    let refusal = fixture.converge();

    assert!(refusal.is_err(), "the pool refusal must reach the caller");
    assert_eq!(
        fixture.php_host.config(&fixture.pool_path).unwrap(),
        old_pool
    );
    assert_eq!(
        fixture.site_host.config(&fixture.vhost_path).unwrap(),
        old_vhost
    );
    assert_eq!(fixture.site_host.writes(), vhost_writes);
}

#[test]
fn converging_a_site_whose_options_changed_writes_the_credential_file_before_the_vhost() {
    let fixture = Fixture::current();
    let changed = php_input_with_options();
    let credential = AgentPaths::site_auth_file(&changed.domain);
    let before = fixture.site_host.write_order().len();

    converge_php_site(
        &fixture.site_host,
        &fixture.php_host,
        distro(),
        &changed,
        &PhpSwitch {
            version: &PhpVersion::parse("8.3").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    )
    .unwrap();

    let order = fixture.site_host.write_order();
    assert_eq!(order[before..], [credential, fixture.vhost_path.clone()]);
    let vhost = fixture.site_host.config(&fixture.vhost_path).unwrap();
    assert!(vhost.contains("auth_basic_user_file"), "{vhost}");
}

#[test]
fn converging_a_suspended_site_leaves_the_stub_and_writes_no_credential_file() {
    let fixture = Fixture::current();
    disable_site(&fixture.site_host, distro(), &fixture.site).unwrap();
    let stub = fixture.site_host.config(&fixture.vhost_path).unwrap();
    let with_options = CreateSiteInput {
        options: php_input_with_options().options,
        ..fixture.site.clone()
    };

    converge_php_site(
        &fixture.site_host,
        &fixture.php_host,
        distro(),
        &with_options,
        &PhpSwitch {
            version: &PhpVersion::parse("8.3").unwrap(),
            max_children: WORKERS,
            overrides: &[],
            remove_previous_pool: false,
        },
    )
    .unwrap();

    assert_eq!(fixture.site_host.config(&fixture.vhost_path), Some(stub));
    assert!(
        fixture
            .site_host
            .auth_file(&AgentPaths::site_auth_file(&with_options.domain))
            .is_none()
    );
}
