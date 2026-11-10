//! Finding the `VirtualHost` sections of an Apache configuration file.

use super::apache_syntax_error::ApacheSyntaxError;
use super::model::apache_virtual_host::ApacheVirtualHost;
use super::model::inventory_limits::InventoryLimits;
use super::model::line_window::LineWindow;
use super::model::source_site_backend::SourceSiteBackend;
use super::php_version_in::php_version_in;

/// Reads the text of one Apache file and returns its `VirtualHost` sections.
///
/// Per section it keeps the first `ServerName` (quotes and `:port` removed),
/// every `ServerAlias` word, the first `DocumentRoot` and the strongest backend
/// the section shows: PHP for a `SetHandler` or `AddHandler`/`AddType` that names
/// PHP, for a `proxy:...fcgi` handler and for a `ProxyPass`/`ProxyPassMatch` to
/// `fcgi://`; a proxy for any other `ProxyPass` (not `!`); static otherwise. The
/// PHP version is taken from the same line when it names one. Directive names
/// are case-insensitive, `\` joins lines, and sections inside a virtual host
/// (`Directory`, `Location`, `IfModule`, ...) are tracked only so their end tags
/// balance. `Include` is NOT followed: the files read are the ones the listing
/// named, and nothing the content says adds to them.
///
/// A file that cannot be read whole is refused whole, never read in part. The
/// number of virtual hosts needs no bound of its own: every one takes two lines
/// at least, and the line bound is lower than twice the virtual-host bound.
///
/// # Errors
///
/// - [`ApacheSyntaxError::TooLarge`] when the text is over the byte bound.
/// - [`ApacheSyntaxError::LineRefused`] for a line over the length bound, one
///   holding a NUL byte, or a file cut by the byte or line bound.
/// - [`ApacheSyntaxError::LineTooLong`] when joined continuations pass the bound.
/// - [`ApacheSyntaxError::UnbalancedSection`] for end tags that do not match.
/// - [`ApacheSyntaxError::TooDeep`] beyond the nesting bound.
pub fn read_apache_virtual_hosts(
    content: &str,
) -> Result<Vec<ApacheVirtualHost>, ApacheSyntaxError> {
    if content.len() > InventoryLimits::MAX_OUTPUT_BYTES {
        return Err(ApacheSyntaxError::TooLarge);
    }
    let window = LineWindow::of(content);
    if window.rejected > 0 || window.truncated {
        return Err(ApacheSyntaxError::LineRefused);
    }

    let mut hosts = Vec::new();
    let mut sections: Vec<String> = Vec::new();
    let mut current: Option<ApacheVirtualHost> = None;
    let mut pending = String::new();

    for raw in &window.lines {
        let line = raw.trim();
        let (piece, continued) = match line.strip_suffix('\\') {
            Some(head) => (head.trim_end(), true),
            None => (line, false),
        };
        if pending.len() + piece.len() + 1 > InventoryLimits::MAX_JOINED_LINE_BYTES {
            return Err(ApacheSyntaxError::LineTooLong);
        }
        if !pending.is_empty() {
            pending.push(' ');
        }
        pending.push_str(piece);
        if continued {
            continue;
        }
        let logical = std::mem::take(&mut pending);
        read_line(logical.trim(), &mut sections, &mut current, &mut hosts)?;
    }
    if !pending.is_empty() {
        read_line(pending.trim(), &mut sections, &mut current, &mut hosts)?;
    }
    if !sections.is_empty() {
        return Err(ApacheSyntaxError::UnbalancedSection);
    }
    Ok(hosts)
}

/// Applies one logical line to the state of the file.
fn read_line(
    text: &str,
    sections: &mut Vec<String>,
    current: &mut Option<ApacheVirtualHost>,
    hosts: &mut Vec<ApacheVirtualHost>,
) -> Result<(), ApacheSyntaxError> {
    if text.is_empty() || text.starts_with('#') {
        return Ok(());
    }
    if let Some(rest) = text.strip_prefix("</") {
        let name = section_name(rest);
        if sections.pop().as_deref() != Some(name.as_str()) {
            return Err(ApacheSyntaxError::UnbalancedSection);
        }
        if name == "virtualhost" {
            hosts.extend(current.take());
        }
        return Ok(());
    }
    if let Some(rest) = text.strip_prefix('<') {
        let name = section_name(rest);
        if sections.len() >= InventoryLimits::MAX_BLOCK_DEPTH {
            return Err(ApacheSyntaxError::TooDeep);
        }
        if name == "virtualhost" {
            if current.is_some() {
                return Err(ApacheSyntaxError::UnbalancedSection);
            }
            *current = Some(ApacheVirtualHost::default());
        }
        sections.push(name);
        return Ok(());
    }
    if let Some(host) = current.as_mut() {
        apply_directive(host, text);
    }
    Ok(())
}

/// The lowercase name of a section from the text after `<` or `</`.
fn section_name(rest: &str) -> String {
    rest.split(|c: char| c.is_whitespace() || c == '>')
        .next()
        .unwrap_or("")
        .to_ascii_lowercase()
}

/// Records one directive of the current virtual host.
fn apply_directive(host: &mut ApacheVirtualHost, text: &str) {
    let words: Vec<&str> = text.split_whitespace().collect();
    let Some(&first) = words.first() else {
        return;
    };
    let lowered = text.to_ascii_lowercase();
    match first.to_ascii_lowercase().as_str() {
        "servername" => {
            if host.server_name.is_none() {
                host.server_name = words.get(1).copied().and_then(unquote).map(without_port);
            }
        }
        "serveralias" => {
            for word in words.iter().skip(1) {
                if host.aliases.len() >= InventoryLimits::MAX_SITE_NAMES {
                    break;
                }
                if let Some(alias) = unquote(word) {
                    host.aliases.push(alias.to_owned());
                }
            }
        }
        "documentroot" => {
            if host.document_root.is_none() {
                host.document_root = words.get(1).copied().and_then(unquote).map(str::to_owned);
            }
        }
        "proxypass" | "proxypassmatch" => {
            if words.last() == Some(&"!") {
                return;
            }
            if lowered.contains("fcgi://") {
                show_php(host, text);
            } else {
                raise(host, SourceSiteBackend::Proxy);
            }
        }
        "sethandler"
            if lowered.contains("php")
                || (lowered.contains("proxy:") && lowered.contains("fcgi")) =>
        {
            show_php(host, text);
        }
        "addhandler" | "addtype" if lowered.contains("php") => show_php(host, text),
        _ => {}
    }
}

/// Marks the host as PHP and takes the version the line names, if any.
fn show_php(host: &mut ApacheVirtualHost, text: &str) {
    raise(host, SourceSiteBackend::Php);
    if host.php_version.is_none() {
        host.php_version = php_version_in(text);
    }
}

/// Raises the host's backend to `to` when that is stronger than what it has.
fn raise(host: &mut ApacheVirtualHost, to: SourceSiteBackend) {
    host.backend = host.backend.max(to);
}

/// The word with one pair of surrounding quotes removed; `None` for a quote that
/// is opened and not closed within the word, because the rest of a quoted value
/// with a space in it is not this word.
fn unquote(word: &str) -> Option<&str> {
    let first = word.chars().next()?;
    if first != '"' && first != '\'' {
        return Some(word);
    }
    if word.len() < 2 || !word.ends_with(first) {
        return None;
    }
    word.get(1..word.len() - 1)
}

/// A host name with a trailing `:port` removed.
fn without_port(name: &str) -> String {
    match name.rsplit_once(':') {
        Some((host, port)) if !port.is_empty() && port.bytes().all(|b| b.is_ascii_digit()) => {
            host.to_owned()
        }
        _ => name.to_owned(),
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_apache_virtual_hosts_tests.rs"]
mod tests;
