//! Tests for [`tokenize_nginx`]: splitting an nginx configuration into tokens.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::nginx_token::NginxToken;
use crate::transfer::inventory::nginx_syntax_error::NginxSyntaxError;
use crate::transfer::inventory::tokenize_nginx::tokenize_nginx;

/// A word token.
fn word(text: &str) -> NginxToken {
    NginxToken::Word(text.to_owned())
}

/// Words, braces and semicolons are split; comments are dropped.
#[test]
fn words_braces_and_semicolons_are_split_and_comments_dropped() {
    let tokens = tokenize_nginx("server { # note\n  listen 80;\n}\n").unwrap();

    assert_eq!(
        tokens,
        [
            word("server"),
            NginxToken::OpenBrace,
            word("listen"),
            word("80"),
            NginxToken::Semicolon,
            NginxToken::CloseBrace,
        ]
    );
}

/// Quoted words lose their quotes and keep spaces; an escaped quote does not end
/// the word.
#[test]
fn quoted_words_keep_their_content() {
    let tokens = tokenize_nginx("root \"/home/a b\"; set 'x\\'y';").unwrap();

    assert_eq!(tokens[1], word("/home/a b"));
    assert_eq!(tokens[4], word("x\\'y"));
}

/// A hash inside a word is not a comment.
#[test]
fn a_hash_inside_a_word_is_not_a_comment() {
    let tokens = tokenize_nginx("a#b;").unwrap();

    assert_eq!(tokens, [word("a#b"), NginxToken::Semicolon]);
}

/// Non-ASCII text stays inside its word without splitting a character.
#[test]
fn non_ascii_text_stays_in_its_word() {
    let tokens = tokenize_nginx("server_name caf\u{e9}.example \u{fffd};").unwrap();

    assert_eq!(tokens[1], word("caf\u{e9}.example"));
    assert_eq!(tokens[2], word("\u{fffd}"));
}

/// A NUL byte is refused.
#[test]
fn a_nul_byte_is_refused() {
    assert_eq!(
        tokenize_nginx("a\0b;"),
        Err(NginxSyntaxError::ControlCharacter)
    );
}

/// An unterminated quote is refused, including one ended by an escape at the end.
#[test]
fn an_unterminated_quote_is_refused() {
    assert_eq!(
        tokenize_nginx("name \"open;"),
        Err(NginxSyntaxError::UnterminatedQuote)
    );
    assert_eq!(
        tokenize_nginx("name \"open\\"),
        Err(NginxSyntaxError::UnterminatedQuote)
    );
}

/// A word over the bound is refused; one at the bound is the inverse control.
#[test]
fn a_huge_word_is_refused_and_one_at_the_bound_is_accepted() {
    let huge = "a".repeat(InventoryLimits::MAX_WORD_BYTES + 1);
    let edge = "a".repeat(InventoryLimits::MAX_WORD_BYTES);

    assert_eq!(tokenize_nginx(&huge), Err(NginxSyntaxError::WordTooLong));
    assert_eq!(tokenize_nginx(&edge).unwrap().len(), 1);
}

/// A text over the byte bound is refused before it is read.
#[test]
fn a_text_over_the_byte_bound_is_refused() {
    let text = " ".repeat(InventoryLimits::MAX_OUTPUT_BYTES + 1);

    assert_eq!(tokenize_nginx(&text), Err(NginxSyntaxError::TooLarge));
}

/// More tokens than the bound are refused.
#[test]
fn too_many_tokens_are_refused() {
    let text = ";".repeat(InventoryLimits::MAX_CONFIG_TOKENS + 1);

    assert_eq!(tokenize_nginx(&text), Err(NginxSyntaxError::TooManyTokens));
}

/// An empty text has no tokens.
#[test]
fn an_empty_text_has_no_tokens() {
    assert_eq!(tokenize_nginx("").unwrap(), []);
}
