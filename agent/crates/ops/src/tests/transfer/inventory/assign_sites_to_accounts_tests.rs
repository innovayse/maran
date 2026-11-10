//! Tests for [`assign_sites_to_accounts`]: attributing virtual hosts to accounts.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::web::domain::Domain;

use crate::transfer::inventory::assign_sites_to_accounts::assign_sites_to_accounts;
use crate::transfer::inventory::inventory_fixtures::passwd_entry;
use crate::transfer::inventory::model::parsed_vhost::ParsedVhost;
use crate::transfer::inventory::model::source_site_backend::SourceSiteBackend;

/// A virtual host of `domain` whose root is `root`.
fn vhost(domain: &str, root: Option<&str>, backend: SourceSiteBackend) -> ParsedVhost {
    ParsedVhost {
        primary: Domain::parse(domain).unwrap(),
        aliases: Vec::new(),
        document_root: root.map(|path| RemotePath::parse(path).unwrap()),
        backend,
        php_version: None,
    }
}

/// A site whose root lies in a home goes to that account, with its root as text.
#[test]
fn a_site_goes_to_the_account_whose_home_holds_its_root() {
    let acme = passwd_entry("acme", "/home/acme");
    let shop = passwd_entry("shopsite", "/home/shopsite");

    let assignment = assign_sites_to_accounts(
        vec![
            vhost(
                "a.example.com",
                Some("/home/acme/public_html"),
                SourceSiteBackend::Static,
            ),
            vhost(
                "b.example.com",
                Some("/home/shopsite/www/public"),
                SourceSiteBackend::Static,
            ),
        ],
        &[&acme, &shop],
        &[],
    );

    assert_eq!(assignment.per_account[0].len(), 1);
    assert_eq!(
        assignment.per_account[0][0].document_root,
        "/home/acme/public_html"
    );
    assert_eq!(
        assignment.per_account[1][0].domain.as_str(),
        "b.example.com"
    );
    assert!(assignment.unowned.is_empty());
}

/// Containment is by whole components: /home/acme2 is not inside /home/acme.
#[test]
fn containment_is_by_whole_components() {
    let acme = passwd_entry("acme", "/home/acme");

    let assignment = assign_sites_to_accounts(
        vec![vhost(
            "a.example.com",
            Some("/home/acme2/www"),
            SourceSiteBackend::Static,
        )],
        &[&acme],
        &[],
    );

    assert!(assignment.per_account[0].is_empty());
    assert_eq!(assignment.unowned.len(), 1);
}

/// The root being the home itself, with or without a trailing slash, is inside.
#[test]
fn the_home_itself_and_a_trailing_slash_are_inside() {
    let acme = passwd_entry("acme", "/home/acme/");

    let assignment = assign_sites_to_accounts(
        vec![
            vhost(
                "a.example.com",
                Some("/home/acme"),
                SourceSiteBackend::Static,
            ),
            vhost(
                "b.example.com",
                Some("/home/acme/"),
                SourceSiteBackend::Static,
            ),
        ],
        &[&acme],
        &[],
    );

    assert_eq!(assignment.per_account[0].len(), 2);
}

/// Nested homes: the deepest one that contains the root wins.
#[test]
fn the_deepest_home_wins() {
    let outer = passwd_entry("outeracct", "/home/shared");
    let inner = passwd_entry("inneracct", "/home/shared/inner");

    let assignment = assign_sites_to_accounts(
        vec![vhost(
            "a.example.com",
            Some("/home/shared/inner/www"),
            SourceSiteBackend::Static,
        )],
        &[&outer, &inner],
        &[],
    );

    assert!(assignment.per_account[0].is_empty());
    assert_eq!(assignment.per_account[1].len(), 1);
}

/// A root outside every home, or none at all, is unowned and attached to nobody.
#[test]
fn a_root_outside_every_home_or_none_is_unowned() {
    let acme = passwd_entry("acme", "/home/acme");

    let assignment = assign_sites_to_accounts(
        vec![
            vhost(
                "a.example.com",
                Some("/var/www/html"),
                SourceSiteBackend::Static,
            ),
            vhost("b.example.com", None, SourceSiteBackend::Proxy),
        ],
        &[&acme],
        &[],
    );

    assert!(assignment.per_account[0].is_empty());
    let unowned: Vec<&str> = assignment.unowned.iter().map(Domain::as_str).collect();
    assert_eq!(unowned, ["a.example.com", "b.example.com"]);
}

/// A home of / owns nothing: it would own everything.
#[test]
fn a_root_home_owns_nothing() {
    let everything = passwd_entry("everyone", "/");

    let assignment = assign_sites_to_accounts(
        vec![vhost(
            "a.example.com",
            Some("/srv/www"),
            SourceSiteBackend::Static,
        )],
        &[&everything],
        &[],
    );

    assert!(assignment.per_account[0].is_empty());
    assert_eq!(assignment.unowned.len(), 1);
}

/// A repeated primary keeps its first virtual host.
#[test]
fn a_repeated_primary_keeps_the_first() {
    let acme = passwd_entry("acme", "/home/acme");

    let assignment = assign_sites_to_accounts(
        vec![
            vhost(
                "a.example.com",
                Some("/home/acme/one"),
                SourceSiteBackend::Static,
            ),
            vhost(
                "a.example.com",
                Some("/home/acme/two"),
                SourceSiteBackend::Static,
            ),
        ],
        &[&acme],
        &[],
    );

    assert_eq!(assignment.per_account[0].len(), 1);
    assert_eq!(assignment.per_account[0][0].document_root, "/home/acme/one");
}

/// A PHP site with no version takes the only installed version.
#[test]
fn a_php_site_without_a_version_takes_the_only_installed_one() {
    let acme = passwd_entry("acme", "/home/acme");

    let assignment = assign_sites_to_accounts(
        vec![vhost(
            "a.example.com",
            Some("/home/acme/w"),
            SourceSiteBackend::Php,
        )],
        &[&acme],
        &["8.2".to_owned()],
    );

    assert_eq!(
        assignment.per_account[0][0].php_version.as_deref(),
        Some("8.2")
    );
}

/// Inverse controls: two installed versions leave it unknown, and a site that is
/// not PHP never takes one.
#[test]
fn the_version_is_not_guessed() {
    let acme = passwd_entry("acme", "/home/acme");

    let two = assign_sites_to_accounts(
        vec![vhost(
            "a.example.com",
            Some("/home/acme/w"),
            SourceSiteBackend::Php,
        )],
        &[&acme],
        &["8.1".to_owned(), "8.2".to_owned()],
    );
    let static_site = assign_sites_to_accounts(
        vec![vhost(
            "b.example.com",
            Some("/home/acme/w"),
            SourceSiteBackend::Static,
        )],
        &[&acme],
        &["8.2".to_owned()],
    );

    assert_eq!(two.per_account[0][0].php_version, None);
    assert_eq!(static_site.per_account[0][0].php_version, None);
}

/// No accounts and no hosts give an empty result.
#[test]
fn nothing_in_nothing_out() {
    let assignment = assign_sites_to_accounts(Vec::new(), &[], &[]);

    assert!(assignment.per_account.is_empty());
    assert!(assignment.unowned.is_empty());
}
