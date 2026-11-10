//! Reading the list of web-server configuration files of a source.

use maran_agent_core::validation::fs::remote_path::RemotePath;

use super::model::inventory_limits::InventoryLimits;
use super::model::line_window::LineWindow;
use super::model::parsed::Parsed;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;

/// Reads the output of `ListNginxVhostFiles` or `ListApacheVhostFiles` (one path
/// per line) into remote paths.
///
/// A line that is not a path the remote-path grammar accepts (a space, a quote,
/// a `..`, a relative path) is NOT read: the file behind it is never asked for,
/// because the path is the one hole of the command that reads it. Such lines are
/// reported once as `SiteUnreadable`. A path repeated keeps its first place.
///
/// Bounded: at most [`InventoryLimits::MAX_VHOST_FILES`] paths, and a bound
/// reached raises `Truncated`. Never fails.
#[must_use]
pub fn parse_vhost_file_list(listing: &str) -> Parsed<RemotePath> {
    let window = LineWindow::of(listing);
    let mut parsed = Parsed::new();
    let mut refused = window.rejected;
    for line in &window.lines {
        let text = line.trim();
        if text.is_empty() {
            continue;
        }
        let Ok(path) = RemotePath::parse(text) else {
            refused += 1;
            continue;
        };
        if parsed.items.contains(&path) {
            continue;
        }
        if parsed.items.len() >= InventoryLimits::MAX_VHOST_FILES {
            parsed.warnings.push(SourceWarningRecord::about_nothing(
                SourceWarningKind::Truncated,
            ));
            return parsed;
        }
        parsed.items.push(path);
    }
    if refused > 0 {
        parsed.warnings.push(SourceWarningRecord::about_nothing(
            SourceWarningKind::SiteUnreadable,
        ));
    }
    if window.truncated {
        parsed.warnings.push(SourceWarningRecord::about_nothing(
            SourceWarningKind::Truncated,
        ));
    }
    parsed
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_vhost_file_list_tests.rs"]
mod tests;
