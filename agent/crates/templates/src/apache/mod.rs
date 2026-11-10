//! Render types for the Apache configs the agent writes.
//!
//! The shapes mirror [`crate::nginx`]: one render type per config artifact, the body of a site
//! rendered once and embedded in both of its virtual hosts. The modules an Apache site relies
//! on, which the Apache app enables when it is installed: `alias`, `authn_file`, `auth_basic`,
//! `authz_core`, `authz_host`, `authz_user`, `dir`, `expires`, `headers`, `mime`, `proxy`,
//! `proxy_fcgi`, `proxy_http`, `rewrite` and `ssl`.

pub mod canonical_redirect_body;
pub mod panel_acme_site;
pub mod php_site;
pub mod proxy_site;
pub mod redirect_rule;
pub mod redirect_site;
pub mod site_access_rules;
pub mod site_body;
pub mod site_option_locations;
pub mod site_options_block;
pub mod ssl_block;
pub mod static_site;
pub mod suspended_site;
