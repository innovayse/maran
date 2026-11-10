//! Splitting an nginx configuration into tokens.

use super::model::inventory_limits::InventoryLimits;
use super::model::nginx_token::NginxToken;
use super::nginx_syntax_error::NginxSyntaxError;

/// Splits `text` into words, `;`, `{` and `}`.
///
/// Comments (`#` to the end of the line, where a token could start) are dropped.
/// A word may be single- or double-quoted, with a backslash escaping the next
/// byte; the quotes are removed and nothing is unescaped. The scan is by bytes
/// and every cut falls on an ASCII byte, so a multi-byte character is never
/// split, and non-ASCII text simply stays inside a word.
///
/// Bounded: the text is at most [`InventoryLimits::MAX_OUTPUT_BYTES`], a word at
/// most [`InventoryLimits::MAX_WORD_BYTES`], the token list at most
/// [`InventoryLimits::MAX_CONFIG_TOKENS`].
///
/// # Errors
///
/// - [`NginxSyntaxError::TooLarge`] when the text is over the byte bound.
/// - [`NginxSyntaxError::ControlCharacter`] when it holds a NUL byte.
/// - [`NginxSyntaxError::UnterminatedQuote`] when a quote is never closed.
/// - [`NginxSyntaxError::WordTooLong`] when a word is over the word bound.
/// - [`NginxSyntaxError::TooManyTokens`] when the token bound is reached.
pub fn tokenize_nginx(text: &str) -> Result<Vec<NginxToken>, NginxSyntaxError> {
    if text.len() > InventoryLimits::MAX_OUTPUT_BYTES {
        return Err(NginxSyntaxError::TooLarge);
    }
    let bytes = text.as_bytes();
    if bytes.contains(&0) {
        return Err(NginxSyntaxError::ControlCharacter);
    }

    let mut tokens = Vec::new();
    let mut index = 0;
    while let Some(&byte) = bytes.get(index) {
        match byte {
            b' ' | b'\t' | b'\r' | b'\n' => index += 1,
            b'#' => {
                while bytes.get(index).is_some_and(|b| *b != b'\n') {
                    index += 1;
                }
            }
            b';' => {
                tokens.push(NginxToken::Semicolon);
                index += 1;
            }
            b'{' => {
                tokens.push(NginxToken::OpenBrace);
                index += 1;
            }
            b'}' => {
                tokens.push(NginxToken::CloseBrace);
                index += 1;
            }
            b'"' | b'\'' => {
                let (word, next) = read_quoted(text, index + 1, byte)?;
                tokens.push(NginxToken::Word(word));
                index = next;
            }
            _ => {
                let start = index;
                while bytes.get(index).is_some_and(|b| !is_delimiter(*b)) {
                    index += 1;
                }
                tokens.push(NginxToken::Word(word_between(text, start, index)?));
            }
        }
        if tokens.len() > InventoryLimits::MAX_CONFIG_TOKENS {
            return Err(NginxSyntaxError::TooManyTokens);
        }
    }
    Ok(tokens)
}

/// Whether `byte` ends an unquoted word.
fn is_delimiter(byte: u8) -> bool {
    matches!(byte, b' ' | b'\t' | b'\r' | b'\n' | b';' | b'{' | b'}')
}

/// Reads a quoted word whose first content byte is at `start`; returns the word
/// and the index just past the closing quote.
fn read_quoted(text: &str, start: usize, quote: u8) -> Result<(String, usize), NginxSyntaxError> {
    let bytes = text.as_bytes();
    let mut end = start;
    loop {
        match bytes.get(end).copied() {
            None => return Err(NginxSyntaxError::UnterminatedQuote),
            Some(b'\\') => end += 2,
            Some(byte) if byte == quote => break,
            Some(_) => end += 1,
        }
    }
    Ok((word_between(text, start, end)?, end + 1))
}

/// The text between two indexes as an owned word, within the word bound.
fn word_between(text: &str, start: usize, end: usize) -> Result<String, NginxSyntaxError> {
    let word = text
        .get(start..end)
        .ok_or(NginxSyntaxError::ControlCharacter)?;
    if word.len() > InventoryLimits::MAX_WORD_BYTES {
        return Err(NginxSyntaxError::WordTooLong);
    }
    Ok(word.to_owned())
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/tokenize_nginx_tests.rs"]
mod tests;
