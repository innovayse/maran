//! Reading the virtual hosts of a source's nginx.

use std::collections::HashSet;

use super::build_parsed_vhost::build_parsed_vhost;
use super::expand_nginx_includes::expand_nginx_includes;
use super::model::inventory_limits::InventoryLimits;
use super::model::parsed::Parsed;
use super::model::parsed_vhost::ParsedVhost;
use super::model::source_site_backend::SourceSiteBackend;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::model::vhost_file::VhostFile;
use super::php_version_in::php_version_in;
use super::read_nginx_servers::read_nginx_servers;

/// Reads every `server` block of every listed nginx file into a validated
/// virtual host.
///
/// Each listed file is read together with the listed files it includes
/// (`expand_nginx_includes`); a file that cannot be read (an unterminated
/// quote, unbalanced braces, an include loop or a chain past the bounds, a NUL
/// byte) is skipped whole with one `SiteUnreadable` warning and the other files
/// are still read. The backend is PHP when a `fastcgi_pass` is present (the
/// version taken from its target when the target names one), proxy when only a
/// `proxy_pass` is, static otherwise. Names and document root pass
/// `build_parsed_vhost`.
///
/// A primary name seen again — the same site reached once as a listed file and
/// once through an include — keeps its first block, as nginx itself does with a
/// repeated `server_name`. Bounded: at most [`InventoryLimits::MAX_VHOSTS`]
/// virtual hosts, then `Truncated`. Never fails.
#[must_use]
pub fn parse_nginx_vhosts(files: &[VhostFile]) -> Parsed<ParsedVhost> {
    let mut parsed = Parsed::new();
    let mut seen: HashSet<String> = HashSet::new();
    for file in files {
        let servers =
            expand_nginx_includes(file, files).and_then(|tokens| read_nginx_servers(&tokens));
        let Ok(servers) = servers else {
            parsed.warnings.push(SourceWarningRecord::about_nothing(
                SourceWarningKind::SiteUnreadable,
            ));
            continue;
        };
        for server in servers {
            let (backend, php_version) = match server.fastcgi_pass.as_deref() {
                Some(target) => (SourceSiteBackend::Php, php_version_in(target)),
                None if server.has_proxy_pass => (SourceSiteBackend::Proxy, None),
                None => (SourceSiteBackend::Static, None),
            };
            let root = server.root.as_deref().or(server.location_root.as_deref());
            let Some(vhost) = build_parsed_vhost(
                &server.server_names,
                root,
                backend,
                php_version,
                &mut parsed.warnings,
            ) else {
                continue;
            };
            if !seen.insert(vhost.primary.as_str().to_owned()) {
                continue;
            }
            if parsed.items.len() >= InventoryLimits::MAX_VHOSTS {
                parsed.warnings.push(SourceWarningRecord::about_nothing(
                    SourceWarningKind::Truncated,
                ));
                return parsed;
            }
            parsed.items.push(vhost);
        }
    }
    parsed
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_nginx_vhosts_tests.rs"]
mod tests;
