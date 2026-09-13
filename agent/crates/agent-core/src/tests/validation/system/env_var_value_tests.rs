//! Tests for the `env_var_value` module.
//!
//! The one refusal that separates this type from `cron_command` is `%`, and it
//! is the reason the two exist as separate types at all: an environment
//! assignment lives on a crontab line and a command does not.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{EnvVarValue, EnvVarValueError, MAX_CRON_LINE, MAX_LENGTH, env_var_name};
use crate::validation::system::env_var_name::EnvVarName;

#[test]
fn an_ordinary_value_parses_and_is_kept_verbatim() {
    let value = EnvVarValue::parse("/usr/local/bin:/usr/bin:/bin").unwrap();

    assert_eq!(value.as_str(), "/usr/local/bin:/usr/bin:/bin");
}

#[test]
fn an_empty_value_is_accepted() {
    assert_eq!(EnvVarValue::parse("").unwrap().as_str(), "");
}

#[test]
fn a_percent_sign_is_refused_because_env_lines_live_in_the_crontab() {
    for candidate in ["%", "a%b", "100%"] {
        assert_eq!(
            EnvVarValue::parse(candidate),
            Err(EnvVarValueError::PercentSign)
        );
    }
}

#[test]
fn control_characters_are_refused() {
    for character in ['\n', '\r', '\t', '\u{0}'] {
        assert_eq!(
            EnvVarValue::parse(&format!("a{character}b")),
            Err(EnvVarValueError::ControlCharacter { character })
        );
    }
}

#[test]
fn the_length_ceiling_is_enforced() {
    let longest = "v".repeat(MAX_LENGTH);
    assert_eq!(EnvVarValue::parse(&longest).unwrap().as_str(), longest);

    let overlong = "v".repeat(MAX_LENGTH + 1);
    assert_eq!(
        EnvVarValue::parse(&overlong),
        Err(EnvVarValueError::TooLong {
            maximum: MAX_LENGTH
        })
    );
}

#[test]
fn surrounding_whitespace_is_refused_because_cron_trims_it() {
    for candidate in [" x", "x ", "  x  ", "\tx"] {
        assert!(
            matches!(
                EnvVarValue::parse(candidate),
                Err(EnvVarValueError::SurroundingWhitespace
                    | EnvVarValueError::ControlCharacter { .. })
            ),
            "`{candidate}` sets the same variable as `x`, so storing both is storing a lie"
        );
    }

    assert_eq!(
        EnvVarValue::parse(" x "),
        Err(EnvVarValueError::SurroundingWhitespace)
    );
}

#[test]
fn a_wrapped_value_is_refused_because_cron_strips_the_quotes() {
    assert_eq!(
        EnvVarValue::parse("\"x\""),
        Err(EnvVarValueError::Quoted { quote: '"' })
    );
    assert_eq!(
        EnvVarValue::parse("'x'"),
        Err(EnvVarValueError::Quoted { quote: '\'' })
    );

    // A quote at one end only, and a lone quote, are ordinary text — cron
    // strips a matching PAIR and nothing else.
    assert_eq!(EnvVarValue::parse("\"x").unwrap().as_str(), "\"x");
    assert_eq!(EnvVarValue::parse("x'").unwrap().as_str(), "x'");
    assert_eq!(EnvVarValue::parse("\"").unwrap().as_str(), "\"");
    assert_eq!(EnvVarValue::parse("a\"b\"c").unwrap().as_str(), "a\"b\"c");
}

#[test]
fn the_longest_line_this_crate_can_compose_fits_crons_own_buffer() {
    // Cron reads an env line into a fixed buffer and TRUNCATES what does not
    // fit, silently. So the check is made on a real composed line built from
    // the longest name and the longest value both types accept, not on the
    // constants: what matters is what the pair can produce together.
    let name = EnvVarName::parse(&"A".repeat(env_var_name::MAX_LENGTH)).unwrap();
    let value = EnvVarValue::parse(&"v".repeat(MAX_LENGTH)).unwrap();

    let line = format!("{}={}", name.as_str(), value.as_str());

    assert!(
        line.len() <= MAX_CRON_LINE,
        "a {}-byte line would be applied truncated and reported as whole",
        line.len()
    );
}

#[test]
fn nothing_but_percent_control_characters_and_length_is_refused() {
    // The refusal list is short and closed. A `#`, a quote, a semicolon and a
    // space are ordinary characters in a `PATH` or a `TZ`, and refusing them
    // would cost working configuration to buy nothing: the line is `KEY=value`
    // and cron reads the value to the end of it.
    for candidate in ["a#b", "a'b", "a;b", "a b", "a\"b", "a$b"] {
        assert_eq!(EnvVarValue::parse(candidate).unwrap().as_str(), candidate);
    }
}
