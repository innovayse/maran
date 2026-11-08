//! Render types for the nginx configs the agent writes.

pub mod auth_line;
pub mod canonical_redirect_body;
pub mod ip_rules;
pub mod php_site;
pub mod proxy_site;
pub mod redirect_line;
pub mod redirect_site;
pub mod request_rate_line;
pub mod site_access_rules;
pub mod site_auth_file;
pub mod site_body;
pub mod site_option_locations;
pub mod site_options_block;
pub mod site_request_limit;
pub mod ssl_block;
pub mod static_site;
pub mod suspended_site;
