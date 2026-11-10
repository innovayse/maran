//! Turning the raw facts of one virtual host into a validated one.

use maran_agent_core::validation::fs::remote_path::RemotePath;

use super::claim_names::claim_names;
use super::model::parsed_vhost::ParsedVhost;
use super::model::source_site_backend::SourceSiteBackend;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;

/// Validates the names and the document root of one virtual host, shared by the
/// nginx and the Apache readers so that both hold the same rules.
///
/// `names` are the host names in the order the configuration lists them. The
/// virtual host is dropped, with a warning, when no name validates and some were
/// refused (`NameRefused`, no subject) or when its document root is not a
/// remote path (`SiteUnreadable`, the primary name as subject). A virtual host
/// whose names are all conventions (`_`) is dropped silently. Names refused
/// beside a valid primary raise one `NameRefused` about that primary; the valid
/// ones are kept. Nothing is repaired.
pub fn build_parsed_vhost(
    names: &[String],
    document_root: Option<&str>,
    backend: SourceSiteBackend,
    php_version: Option<String>,
    warnings: &mut Vec<SourceWarningRecord>,
) -> Option<ParsedVhost> {
    let claimed = claim_names(names);
    let Some(primary) = claimed.primary else {
        if claimed.refused > 0 {
            warnings.push(SourceWarningRecord::about_nothing(
                SourceWarningKind::NameRefused,
            ));
        }
        return None;
    };
    if claimed.refused > 0 {
        warnings.push(SourceWarningRecord::about_domain(
            SourceWarningKind::NameRefused,
            &primary,
        ));
    }
    let document_root = match document_root {
        None => None,
        Some(root) => match RemotePath::parse(root) {
            Ok(path) => Some(path),
            Err(_) => {
                warnings.push(SourceWarningRecord::about_domain(
                    SourceWarningKind::SiteUnreadable,
                    &primary,
                ));
                return None;
            }
        },
    };
    Some(ParsedVhost {
        primary,
        aliases: claimed.aliases,
        document_root,
        backend,
        php_version,
    })
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/build_parsed_vhost_tests.rs"]
mod tests;
