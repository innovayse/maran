//! The host names a rendered vhost answers to, read back out of its text.

/// The directive nginx spells its names with, as the agent's templates write it.
pub(crate) const NGINX_DIRECTIVE: &str = "    server_name ";

/// The directive Apache spells a virtual host's primary name with.
pub(crate) const APACHE_NAME_DIRECTIVE: &str = "    ServerName ";

/// The directive Apache spells each additional name with.
pub(crate) const APACHE_ALIAS_DIRECTIVE: &str = "    ServerAlias ";

/// Every host name the vhost text `contents` names, in file order.
///
/// Reads the two spellings the agent's own templates produce and nothing else: an nginx
/// `server_name a b;` line and Apache `ServerName` / `ServerAlias` lines, each matched with
/// its four-space indentation. It is a reader for files this agent rendered, not a parser of
/// either server's grammar, so a hand-written vhost may name hosts it does not see; callers
/// that need a verdict about arbitrary files must not rely on it.
#[must_use]
pub(crate) fn vhost_server_names(contents: &str) -> Vec<String> {
    let mut names = Vec::new();

    for line in contents.lines() {
        if let Some(rest) = line.strip_prefix(NGINX_DIRECTIVE) {
            names.extend(
                rest.trim_end_matches(';')
                    .split_whitespace()
                    .map(str::to_owned),
            );
        } else if let Some(rest) = line
            .strip_prefix(APACHE_NAME_DIRECTIVE)
            .or_else(|| line.strip_prefix(APACHE_ALIAS_DIRECTIVE))
        {
            names.extend(rest.split_whitespace().map(str::to_owned));
        }
    }

    names
}

#[cfg(test)]
#[path = "../tests/sites/vhost_server_names_tests.rs"]
mod tests;
