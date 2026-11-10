//! Following `include` directives, but only into files the listing named.

use super::model::inventory_limits::InventoryLimits;
use super::model::nginx_token::NginxToken;
use super::model::vhost_file::VhostFile;
use super::nginx_syntax_error::NginxSyntaxError;
use super::tokenize_nginx::tokenize_nginx;
use super::wildcard_matches::wildcard_matches;

/// The longest file name Linux allows; a longer name matches no pattern.
const MAX_FILE_NAME_BYTES: usize = 255;

/// The tokens of `root` with every `include` of a LISTED file replaced by that
/// file's tokens, as nginx itself reads them.
///
/// An `include` is followed only when its argument is the exact path of a file in
/// `files`, or a pattern whose directory is a listed file's directory and whose
/// last component matches that file's name (`*` and `?`); the matches are taken
/// in path order. Any other `include` (`mime.types`, a path the listing did not
/// name, a pattern matching nothing) is dropped and nothing is read from the
/// source for it, which is what keeps the set of files the agent asks for fixed
/// by the listing and not by the content.
///
/// Bounded: includes nest at most [`InventoryLimits::MAX_INCLUDE_DEPTH`] deep,
/// the expansion enters at most [`InventoryLimits::MAX_INCLUDE_WORK`] files and
/// resolves that many `include` statements in all, and the result holds at most
/// [`InventoryLimits::MAX_CONFIG_TOKENS`] tokens. A visited chain refuses a loop.
///
/// # Errors
///
/// Every error of [`tokenize_nginx`], and in addition:
///
/// - [`NginxSyntaxError::IncludeLoop`] when a chain enters a file already on it.
/// - [`NginxSyntaxError::IncludeTooDeep`] beyond the depth bound.
/// - [`NginxSyntaxError::TooMuchWork`] beyond the work bound.
/// - [`NginxSyntaxError::TooManyTokens`] beyond the token bound.
pub fn expand_nginx_includes(
    root: &VhostFile,
    files: &[VhostFile],
) -> Result<Vec<NginxToken>, NginxSyntaxError> {
    let mut expansion = Expansion {
        files,
        chain: vec![root.path.as_str().to_owned()],
        out: Vec::new(),
        work: 0,
    };
    expansion.inline(&root.content)?;
    Ok(expansion.out)
}

/// The state of one expansion.
struct Expansion<'a> {
    /// Every file the listing named.
    files: &'a [VhostFile],
    /// The paths of the files being entered, outermost first.
    chain: Vec<String>,
    /// The tokens produced so far.
    out: Vec<NginxToken>,
    /// Files entered plus `include` statements resolved.
    work: usize,
}

impl<'a> Expansion<'a> {
    /// Appends the tokens of `content`, entering the files its includes name.
    fn inline(&mut self, content: &str) -> Result<(), NginxSyntaxError> {
        self.spend_work()?;
        let tokens = tokenize_nginx(content)?;
        let files = self.files;
        let mut at_statement_start = true;
        let mut index = 0;
        while let Some(token) = tokens.get(index) {
            let include = if at_statement_start {
                include_pattern(&tokens, index)
            } else {
                None
            };
            if let Some(pattern) = include {
                self.spend_work()?;
                for file in matching_files(files, pattern) {
                    let path = file.path.as_str();
                    if self.chain.iter().any(|entered| entered == path) {
                        return Err(NginxSyntaxError::IncludeLoop);
                    }
                    if self.chain.len() > InventoryLimits::MAX_INCLUDE_DEPTH {
                        return Err(NginxSyntaxError::IncludeTooDeep);
                    }
                    self.chain.push(path.to_owned());
                    self.inline(&file.content)?;
                    self.chain.pop();
                }
                index += 3;
                continue;
            }
            at_statement_start = !matches!(token, NginxToken::Word(_));
            self.out.push(token.clone());
            if self.out.len() > InventoryLimits::MAX_CONFIG_TOKENS {
                return Err(NginxSyntaxError::TooManyTokens);
            }
            index += 1;
        }
        Ok(())
    }

    /// Counts one unit of work against the bound.
    fn spend_work(&mut self) -> Result<(), NginxSyntaxError> {
        self.work += 1;
        if self.work > InventoryLimits::MAX_INCLUDE_WORK {
            return Err(NginxSyntaxError::TooMuchWork);
        }
        Ok(())
    }
}

/// The argument of an `include ARG;` statement that starts at `index`.
fn include_pattern(tokens: &[NginxToken], index: usize) -> Option<&str> {
    match (
        tokens.get(index)?,
        tokens.get(index + 1)?,
        tokens.get(index + 2)?,
    ) {
        (NginxToken::Word(directive), NginxToken::Word(argument), NginxToken::Semicolon)
            if directive == "include" =>
        {
            Some(argument.as_str())
        }
        _ => None,
    }
}

/// The listed files an include argument names, in path order.
fn matching_files<'f>(files: &'f [VhostFile], pattern: &str) -> Vec<&'f VhostFile> {
    let Some((directory, name_pattern)) = pattern.rsplit_once('/') else {
        return Vec::new();
    };
    let mut matched: Vec<&VhostFile> = files
        .iter()
        .filter(|file| {
            file.path
                .as_str()
                .rsplit_once('/')
                .is_some_and(|(file_directory, name)| {
                    file_directory == directory
                        && name.len() <= MAX_FILE_NAME_BYTES
                        && wildcard_matches(name_pattern, name)
                })
        })
        .collect();
    matched.sort_by(|a, b| a.path.as_str().cmp(b.path.as_str()));
    matched
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/expand_nginx_includes_tests.rs"]
mod tests;
