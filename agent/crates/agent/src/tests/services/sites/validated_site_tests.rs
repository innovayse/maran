//! What the strict site check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::panel_host_names::PanelHostNames;

use super::{CERTIFICATE_NAME_LIMIT, MAXIMUM_ALIASES, validated_site};
use crate::proto::{ErrorCode, SiteBackendType, SiteSpec};

/// A static spec carrying `aliases`.
fn a_static_spec(aliases: &[&str]) -> SiteSpec {
    SiteSpec {
        aliases: aliases.iter().map(|alias| (*alias).to_owned()).collect(),
        backend_type: SiteBackendType::Static as i32,
        php_version: String::new(),
        proxy_upstream: String::new(),
        has_certificate: false,
        options: None,
        ..Default::default()
    }
}

#[test]
fn an_ordinary_domain_and_alias_are_accepted() {
    let spec = a_static_spec(&["www.example.com"]);

    let input = validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none())
        .expect("accepted");

    assert_eq!(input.domain.as_str(), "example.com");
    assert_eq!(input.aliases.len(), 1);
    assert_eq!(input.aliases[0].as_str(), "www.example.com");
}

#[test]
fn a_reserved_domain_is_refused_as_invalid_input() {
    let spec = a_static_spec(&[]);

    for reserved in ["203.0.113.7", "localhost", "x.local"] {
        let error = validated_site("acme", reserved, Some(&spec), &PanelHostNames::none())
            .map(|_| ())
            .expect_err("a reserved domain must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{reserved}");
    }
}

#[test]
fn a_reserved_alias_is_refused_even_when_the_domain_is_ordinary() {
    for reserved in ["203.0.113.7", "x.local", "localhost"] {
        let spec = a_static_spec(&["www.example.com", reserved]);

        let error = validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none())
            .map(|_| ())
            .expect_err("a reserved alias must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{reserved}");
    }
}

#[test]
fn a_malformed_alias_is_still_refused() {
    let spec = a_static_spec(&["bad_name.example.com"]);

    assert!(validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none()).is_err());
}

#[test]
fn an_absent_spec_is_refused() {
    assert!(validated_site("acme", "example.com", None, &PanelHostNames::none()).is_err());
}

/// A static spec carrying `count` distinct, well-formed aliases of `example.com`.
fn a_static_spec_with_aliases(count: usize) -> SiteSpec {
    let aliases: Vec<String> = (0..count)
        .map(|index| format!("alias{index}.example.com"))
        .collect();
    let borrowed: Vec<&str> = aliases.iter().map(String::as_str).collect();
    a_static_spec(&borrowed)
}

#[test]
fn a_site_carrying_exactly_the_maximum_number_of_aliases_is_accepted() {
    let spec = a_static_spec_with_aliases(MAXIMUM_ALIASES);

    let input = validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none())
        .expect("accepted");

    assert_eq!(input.aliases.len(), MAXIMUM_ALIASES);
}

#[test]
fn one_alias_over_the_maximum_is_refused_as_invalid_input() {
    let spec = a_static_spec_with_aliases(MAXIMUM_ALIASES + 1);

    let error = validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none())
        .map(|_| ())
        .expect_err("one alias over the maximum must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_alias_maximum_leaves_the_primary_domain_one_name_of_a_certificate() {
    assert_eq!(MAXIMUM_ALIASES + 1, CERTIFICATE_NAME_LIMIT);
}

#[test]
fn a_specs_options_reach_the_validated_site() {
    use crate::proto::{CanonicalHost, SiteOptions};

    let spec = SiteSpec {
        options: Some(SiteOptions {
            canonical_host: CanonicalHost::Www as i32,
            client_max_body_size_mib: 0,
            ..SiteOptions::default()
        }),
        ..a_static_spec(&["www.example.com"])
    };

    let input = validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none())
        .expect("accepted");

    assert!(input.options.canonical_host().is_some());
}

#[test]
fn options_that_do_not_fit_the_site_refuse_the_whole_spec() {
    use crate::proto::SiteOptions;

    // A static site accepts no request body, so it has no upload size.
    let spec = SiteSpec {
        options: Some(SiteOptions {
            client_max_body_size_mib: 10,
            ..SiteOptions::default()
        }),
        ..a_static_spec(&[])
    };

    assert!(validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none()).is_err());
}

#[test]
fn a_canonical_host_the_site_does_not_serve_refuses_the_whole_spec() {
    use crate::proto::{CanonicalHost, SiteOptions};

    // The site serves only `example.com`; `www.example.com` is not one of its names.
    let spec = SiteSpec {
        options: Some(SiteOptions {
            canonical_host: CanonicalHost::Www as i32,
            ..SiteOptions::default()
        }),
        ..a_static_spec(&[])
    };

    assert!(validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none()).is_err());
}

/// The names of a host whose panel is served under `panel.example.com`.
fn the_panel_names() -> PanelHostNames {
    PanelHostNames::parse_list("panel.example.com").unwrap()
}

#[test]
fn a_panel_name_as_the_domain_is_refused_as_invalid_input() {
    let spec = a_static_spec(&[]);

    let error = validated_site("acme", "panel.example.com", Some(&spec), &the_panel_names())
        .map(|_| ())
        .expect_err("the panel's name must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_panel_name_as_an_alias_is_refused_as_invalid_input() {
    let spec = a_static_spec(&["panel.example.com"]);

    let error = validated_site("acme", "example.com", Some(&spec), &the_panel_names())
        .map(|_| ())
        .expect_err("the panel's name must be refused as an alias");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_unrelated_domain_and_alias_are_accepted_beside_the_panel_names() {
    let spec = a_static_spec(&["www.shop.example.com"]);

    let input = validated_site("acme", "shop.example.com", Some(&spec), &the_panel_names())
        .expect("accepted");

    assert_eq!(input.aliases.len(), 1);
}

#[test]
fn a_node_site_carries_its_major_port_and_start_command() {
    let spec = SiteSpec {
        backend_type: SiteBackendType::Node as i32,
        node_version: "20".to_owned(),
        app_port: 3000,
        start_command: "node server.js".to_owned(),
        ..a_static_spec(&[])
    };

    let input =
        validated_site("acme", "example.com", Some(&spec), &PanelHostNames::none()).unwrap();

    assert!(matches!(
        input.kind,
        maran_ops::sites::SiteKind::Node { .. }
    ));
}

#[test]
fn a_node_site_with_a_major_outside_the_catalogue_or_a_shell_command_is_refused() {
    let valid = SiteSpec {
        backend_type: SiteBackendType::Node as i32,
        node_version: "20".to_owned(),
        app_port: 3000,
        start_command: "node server.js".to_owned(),
        ..a_static_spec(&[])
    };
    // Positive control: the base spec is accepted, so each refusal below is its one change.
    validated_site("acme", "example.com", Some(&valid), &PanelHostNames::none()).unwrap();

    for broken in [
        SiteSpec {
            node_version: "19".to_owned(),
            ..valid.clone()
        },
        SiteSpec {
            start_command: "node server.js; rm -rf /".to_owned(),
            ..valid.clone()
        },
        SiteSpec {
            app_port: 0,
            ..valid.clone()
        },
    ] {
        assert!(
            validated_site(
                "acme",
                "example.com",
                Some(&broken),
                &PanelHostNames::none()
            )
            .is_err()
        );
    }
}

#[test]
fn the_web_server_is_apache_only_when_the_spec_says_so() {
    let apache = SiteSpec {
        web_server: crate::proto::SiteWebServer::Apache as i32,
        ..a_static_spec(&[])
    };

    let chosen = validated_site(
        "acme",
        "example.com",
        Some(&apache),
        &PanelHostNames::none(),
    )
    .unwrap();
    let default = validated_site(
        "acme",
        "example.com",
        Some(&a_static_spec(&[])),
        &PanelHostNames::none(),
    )
    .unwrap();

    assert_eq!(chosen.web_server, maran_distro::SiteWebServer::Apache);
    assert_eq!(default.web_server, maran_distro::SiteWebServer::Nginx);
}
