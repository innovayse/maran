//! Reading the list of PHP installations of a source.

use super::model::inventory_limits::InventoryLimits;
use super::model::line_window::LineWindow;
use super::model::parsed::Parsed;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::php_version_in::php_version_in;

/// Reads the output of `ListPhpVersions` (one installation directory per line,
/// such as `/etc/php/8.2` or `/etc/opt/remi/php82`) into the distinct
/// `major.minor` versions, in the order they appear.
///
/// Only the last path component of a line is looked at, so a directory above
/// the installation cannot supply a version. A line that names no version is
/// ignored: the listing is a hint for sites whose handler does not name a
/// version, and a missing hint costs nothing but an unknown version.
///
/// Bounded: at most [`InventoryLimits::MAX_PHP_VERSIONS`] versions; a bound
/// reached raises `Truncated`. Never fails.
#[must_use]
pub fn parse_php_versions(listing: &str) -> Parsed<String> {
    let window = LineWindow::of(listing);
    let mut parsed = Parsed::new();
    for line in &window.lines {
        let name = line.trim().trim_end_matches('/');
        let component = name.rsplit('/').next().unwrap_or("");
        let Some(version) = php_version_in(component) else {
            continue;
        };
        if parsed.items.contains(&version) {
            continue;
        }
        if parsed.items.len() >= InventoryLimits::MAX_PHP_VERSIONS {
            parsed.warnings.push(SourceWarningRecord::about_nothing(
                SourceWarningKind::Truncated,
            ));
            return parsed;
        }
        parsed.items.push(version);
    }
    if window.truncated || window.rejected > 0 {
        parsed.warnings.push(SourceWarningRecord::about_nothing(
            SourceWarningKind::Truncated,
        ));
    }
    parsed
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_php_versions_tests.rs"]
mod tests;
