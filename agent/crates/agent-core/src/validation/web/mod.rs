//! Values written into web-server configuration — server names, proxy targets,
//! PHP versions bound to a site — and the network endpoints the firewall
//! matches on, which are the same kind of value reaching a different file.

pub mod ban_address;
pub mod ban_address_error;
pub mod domain;
pub mod domain_error;
pub(crate) mod ipv4_disguise;
pub mod php_version;
pub mod php_version_error;
pub mod port;
pub mod port_error;
pub mod source_cidr;
pub mod source_cidr_error;
pub mod upstream;
pub mod upstream_error;
