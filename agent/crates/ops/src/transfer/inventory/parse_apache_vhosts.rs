//! Reading the virtual hosts of a source's Apache.

use std::collections::HashSet;

use super::build_parsed_vhost::build_parsed_vhost;
use super::model::inventory_limits::InventoryLimits;
use super::model::parsed::Parsed;
use super::model::parsed_vhost::ParsedVhost;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::model::vhost_file::VhostFile;
use super::read_apache_virtual_hosts::read_apache_virtual_hosts;

/// Reads every `VirtualHost` of every listed Apache file into a validated
/// virtual host.
///
/// A file `read_apache_virtual_hosts` refuses is skipped whole with one
/// `SiteUnreadable` warning and the other files are still read. The names are
/// the `ServerName` then the `ServerAlias` words; names and document root pass
/// `build_parsed_vhost`, the same gate the nginx reader uses. A primary name seen
/// again keeps its first block. Bounded: at most
/// [`InventoryLimits::MAX_VHOSTS`] virtual hosts, then `Truncated`. Never fails.
#[must_use]
pub fn parse_apache_vhosts(files: &[VhostFile]) -> Parsed<ParsedVhost> {
    let mut parsed = Parsed::new();
    let mut seen: HashSet<String> = HashSet::new();
    for file in files {
        let Ok(hosts) = read_apache_virtual_hosts(&file.content) else {
            parsed.warnings.push(SourceWarningRecord::about_nothing(
                SourceWarningKind::SiteUnreadable,
            ));
            continue;
        };
        for host in hosts {
            let names: Vec<String> = host.server_name.into_iter().chain(host.aliases).collect();
            let Some(vhost) = build_parsed_vhost(
                &names,
                host.document_root.as_deref(),
                host.backend,
                host.php_version,
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
#[path = "../../tests/transfer/inventory/parse_apache_vhosts_tests.rs"]
mod tests;
