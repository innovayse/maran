//! Tests for the `render_apache_vhost` module: what the Apache dialect decides, as opposed to
//! the byte text of each template, which the goldens in the templates crate pin.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::request_rate::RequestRate;
use maran_distro::SiteWebServer;

use crate::sites::SitesOpError;
use crate::sites::fake_site_host::{apache_php_input, node_input, php_input};
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_options::SiteOptions;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::render_vhost::render_vhost;

/// Renders `input` against the paths its own account, domain and web server name.
fn render(input: &CreateSiteInput) -> Result<String, SitesOpError> {
    render_vhost(
        input,
        &SitePaths::for_site(&input.account, &input.domain, input.web_server),
    )
}

#[test]
fn an_apache_site_is_rendered_in_the_apache_dialect() {
    let rendered = render(&apache_php_input()).unwrap();

    assert!(rendered.contains("<VirtualHost *:80>"));
    assert!(rendered.contains("SetHandler \"proxy:unix:/run/maran-php/"));
    assert!(!rendered.contains("server {"));
}

#[test]
fn the_same_site_on_nginx_is_not_rendered_in_the_apache_dialect() {
    let rendered = render(&php_input()).unwrap();

    assert!(rendered.contains("server {"));
    assert!(!rendered.contains("<VirtualHost"));
}

#[test]
fn a_request_limit_on_an_apache_site_is_refused_and_on_nginx_accepted() {
    let limit = SiteOptions::default().with_request_rate(Some(RequestRate::parse(20, 40).unwrap()));
    let apache = CreateSiteInput {
        options: limit.clone(),
        ..apache_php_input()
    };
    let nginx = CreateSiteInput {
        options: limit,
        ..php_input()
    };

    assert!(matches!(
        render(&apache),
        Err(SitesOpError::InvalidOptions(_))
    ));
    assert!(render(&nginx).is_ok());
}

#[test]
fn a_node_site_is_proxied_to_its_loopback_port_on_both_web_servers() {
    let apache = render(&node_input(SiteWebServer::Apache)).unwrap();
    let nginx = render(&node_input(SiteWebServer::Nginx)).unwrap();

    assert!(apache.contains("ProxyPass / http://127.0.0.1:3000/"));
    assert!(nginx.contains("proxy_pass http://127.0.0.1:3000;"));
}

#[test]
fn an_apache_vhost_lives_in_the_apache_directory_and_an_nginx_one_in_nginxs() {
    let input = php_input();
    let apache = SitePaths::for_site(&input.account, &input.domain, SiteWebServer::Apache);
    let nginx = SitePaths::for_site(&input.account, &input.domain, SiteWebServer::Nginx);

    assert!(apache.config_path.starts_with("/etc/maran/apache/sites"));
    assert!(nginx.config_path.starts_with("/etc/maran/nginx/sites"));
    assert_eq!(
        SitePaths::web_server_of(&apache.config_path),
        Some(SiteWebServer::Apache)
    );
    assert_eq!(
        SitePaths::web_server_of(std::path::Path::new("/tmp/x.conf")),
        None
    );
}
