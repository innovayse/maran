//! Finding the `server` blocks of an expanded nginx configuration.

use super::model::inventory_limits::InventoryLimits;
use super::model::nginx_server::NginxServer;
use super::model::nginx_token::NginxToken;
use super::nginx_syntax_error::NginxSyntaxError;

/// What kind of block an open brace opened.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Scope {
    /// A `server` block that is being collected.
    Server,
    /// Any other block (`http`, `location`, `if`, ...).
    Other,
}

/// Collects the facts of every top-level `server` block in `tokens`: its
/// `server_name` words, its `root` (or, failing that, the first `root` of a block
/// inside it), the target of its first `fastcgi_pass` and whether it has a
/// `proxy_pass`.
///
/// A `server` block inside another `server` block is an ordinary block, not a
/// second site. Statements outside a `server` block are ignored, and so is every
/// directive this reader does not name: the reader asks only for what an
/// inventory shows and executes nothing.
///
/// # Errors
///
/// - [`NginxSyntaxError::UnbalancedBraces`] for a `}` with no `{` or a `{` never
///   closed.
/// - [`NginxSyntaxError::TooDeep`] when blocks nest past
///   [`InventoryLimits::MAX_BLOCK_DEPTH`].
/// - [`NginxSyntaxError::StatementTooLong`] when one statement holds more than
///   [`InventoryLimits::MAX_STATEMENT_WORDS`] words.
pub fn read_nginx_servers(tokens: &[NginxToken]) -> Result<Vec<NginxServer>, NginxSyntaxError> {
    let mut servers = Vec::new();
    let mut scopes: Vec<Scope> = Vec::new();
    let mut current: Option<NginxServer> = None;
    let mut statement: Vec<&str> = Vec::new();

    for token in tokens {
        match token {
            NginxToken::Word(word) => {
                if statement.len() >= InventoryLimits::MAX_STATEMENT_WORDS {
                    return Err(NginxSyntaxError::StatementTooLong);
                }
                statement.push(word.as_str());
            }
            NginxToken::Semicolon => {
                if let Some(server) = current.as_mut() {
                    let in_server_block = scopes.last() == Some(&Scope::Server);
                    apply_directive(server, &statement, in_server_block);
                }
                statement.clear();
            }
            NginxToken::OpenBrace => {
                if scopes.len() >= InventoryLimits::MAX_BLOCK_DEPTH {
                    return Err(NginxSyntaxError::TooDeep);
                }
                if statement.as_slice() == ["server"] && current.is_none() {
                    current = Some(NginxServer::default());
                    scopes.push(Scope::Server);
                } else {
                    scopes.push(Scope::Other);
                }
                statement.clear();
            }
            NginxToken::CloseBrace => {
                match scopes.pop() {
                    None => return Err(NginxSyntaxError::UnbalancedBraces),
                    Some(Scope::Server) => servers.extend(current.take()),
                    Some(Scope::Other) => {}
                }
                statement.clear();
            }
        }
    }
    if !scopes.is_empty() {
        return Err(NginxSyntaxError::UnbalancedBraces);
    }
    Ok(servers)
}

/// Records one directive of the current `server` block. `server_name` counts only
/// directly in the block, since nginx accepts it nowhere else; the other
/// directives count at any depth, because a PHP handler or a proxy lives in a
/// `location`.
fn apply_directive(server: &mut NginxServer, statement: &[&str], in_server_block: bool) {
    let Some((&name, arguments)) = statement.split_first() else {
        return;
    };
    match name {
        "server_name" if in_server_block => {
            for argument in arguments {
                if server.server_names.len() >= InventoryLimits::MAX_SITE_NAMES {
                    break;
                }
                server.server_names.push((*argument).to_owned());
            }
        }
        "root" => {
            let slot = if in_server_block {
                &mut server.root
            } else {
                &mut server.location_root
            };
            if slot.is_none() {
                *slot = arguments.first().map(|value| (*value).to_owned());
            }
        }
        "fastcgi_pass" => {
            if server.fastcgi_pass.is_none() {
                server.fastcgi_pass = arguments.first().map(|value| (*value).to_owned());
            }
        }
        "proxy_pass" => server.has_proxy_pass = true,
        _ => {}
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_nginx_servers_tests.rs"]
mod tests;
