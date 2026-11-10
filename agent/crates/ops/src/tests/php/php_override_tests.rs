//! Tests for [`PhpOverride`].
//!
//! The most security-relevant tests in the area. Every one of them defends a
//! specific way a customer setting could become something other than a
//! customer setting: an unlisted name silently taking effect, an unbounded
//! value taking the machine down for its neighbours, and a value with a
//! newline in it becoming a second directive in a file root wrote.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::php::PhpOpError;
use crate::php::model::php_override::PhpOverride;

#[test]
fn a_whitelisted_setting_within_its_bound_is_accepted() {
    let accepted = PhpOverride::parse("memory_limit", "256M").unwrap();

    assert_eq!(accepted.name(), "memory_limit");
    assert_eq!(accepted.value(), "256M");
}

#[test]
fn a_name_outside_the_whitelist_is_refused_and_not_dropped() {
    // `disable_functions` is the name that matters here: dropping it silently
    // would let a customer believe they had unset the pool's protection, and
    // refusing it tells them the truth. It is also the name an attacker tries
    // first.
    match PhpOverride::parse("disable_functions", "") {
        Err(PhpOpError::OverrideNotAllowed { name }) => assert_eq!(name, "disable_functions"),
        other => panic!("expected OverrideNotAllowed, got {other:?}"),
    }
}

#[test]
fn open_basedir_is_not_a_setting_a_customer_may_change() {
    assert!(matches!(
        PhpOverride::parse("open_basedir", "/"),
        Err(PhpOpError::OverrideNotAllowed { .. })
    ));
}

#[test]
fn a_memory_limit_above_the_maximum_is_refused() {
    // 512M is the ceiling; 1G is over it. Unbounded memory on a shared host is
    // one account making the machine unusable for every other account on it.
    match PhpOverride::parse("memory_limit", "1G") {
        Err(PhpOpError::OverrideOutOfRange { name, maximum, .. }) => {
            assert_eq!(name, "memory_limit");
            assert_eq!(maximum, 512 * 1024 * 1024);
        }
        other => panic!("expected OverrideOutOfRange, got {other:?}"),
    }
}

#[test]
fn memory_limit_minus_one_is_refused_rather_than_read_as_unlimited() {
    // PHP reads `-1` as "no limit", so a check that only compared numbers
    // would pass the single most dangerous value there is.
    assert!(matches!(
        PhpOverride::parse("memory_limit", "-1"),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

#[test]
fn a_size_whose_scaling_would_overflow_is_refused_rather_than_wrapped() {
    // Wrapping would turn an enormous request into a small number that passes
    // the bound — the bound check would report success on the way to writing
    // an unbounded pool.
    assert!(matches!(
        PhpOverride::parse("memory_limit", "18446744073709551G"),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

#[test]
fn a_value_containing_a_newline_is_refused() {
    // The config-injection rule (rules/security.md item 4) in a second file
    // format. Without this, the rendered pool would read:
    //   php_value[memory_limit] = 128M
    //   php_admin_value[disable_functions] =
    // and the customer would have unset the pool's hardening from inside a
    // setting they are allowed to change.
    match PhpOverride::parse("memory_limit", "128M\nphp_admin_value[disable_functions] =") {
        Err(PhpOpError::OverrideControlCharacter { name }) => assert_eq!(name, "memory_limit"),
        other => panic!("expected OverrideControlCharacter, got {other:?}"),
    }
}

#[test]
fn a_value_containing_a_carriage_return_is_refused() {
    // A lone `\r` is not a line ending for a parser reading `\n`, but it is
    // one for plenty of tooling — and it is a control character, which is the
    // class the check is written against rather than the two characters that
    // happen to be famous.
    assert!(matches!(
        PhpOverride::parse("max_input_vars", "100\rmore"),
        Err(PhpOpError::OverrideControlCharacter { .. })
    ));
}

#[test]
fn a_value_containing_a_nul_is_refused() {
    assert!(matches!(
        PhpOverride::parse("max_execution_time", "30\0"),
        Err(PhpOpError::OverrideControlCharacter { .. })
    ));
}

#[test]
fn a_timezone_is_accepted_by_name() {
    assert_eq!(
        PhpOverride::parse("date.timezone", "Europe/Yerevan")
            .unwrap()
            .value(),
        "Europe/Yerevan"
    );
}

#[test]
fn a_timezone_that_is_a_path_traversal_is_refused() {
    // `date.timezone` is resolved by PHP against the zoneinfo tree, so it is a
    // path-like value in a root-owned config and gets the treatment every
    // path-like value gets (rules/security.md item 2).
    assert!(matches!(
        PhpOverride::parse("date.timezone", "../../etc/passwd"),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

#[test]
fn an_absolute_timezone_path_is_refused() {
    assert!(matches!(
        PhpOverride::parse("date.timezone", "/etc/passwd"),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

#[test]
fn max_execution_time_above_the_maximum_is_refused() {
    // Workers are a fixed budget; a request stuck for an hour holds one of
    // them for an hour and the account's sites stop answering.
    assert!(matches!(
        PhpOverride::parse("max_execution_time", "3600"),
        Err(PhpOpError::OverrideOutOfRange { .. })
    ));
}

#[test]
fn max_execution_time_zero_is_refused_rather_than_read_as_unlimited() {
    // PHP reads zero here as "no limit" — the same meaning `-1` has for
    // memory_limit, at the setting where it costs most: a request with no
    // execution limit holds one of the pool's fixed workers indefinitely. A
    // ceiling-only bound would let it in through the front door.
    assert!(matches!(
        PhpOverride::parse("max_execution_time", "0"),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

#[test]
fn a_size_of_zero_is_refused_for_every_size_setting_rather_than_read_as_unlimited() {
    // PHP documents `post_max_size = 0` as "no limit", so a ceiling that only
    // compared the upper bound let zero walk under it. Every spelling of zero
    // is named: the bare number and each suffix.
    for setting in ["memory_limit", "upload_max_filesize", "post_max_size"] {
        for zero in ["0", "0K", "0M", "0G", "00"] {
            assert!(
                matches!(
                    PhpOverride::parse(setting, zero),
                    Err(PhpOpError::OverrideMalformed { .. })
                ),
                "{setting}={zero} must be refused"
            );
        }
    }
}

#[test]
fn a_size_of_one_byte_is_accepted_for_every_size_setting() {
    // The inverse control: the floor sits at 1, so the smallest real size
    // still passes and the refusal above is not a refusal of everything.
    for setting in ["memory_limit", "upload_max_filesize", "post_max_size"] {
        assert_eq!(PhpOverride::parse(setting, "1").unwrap().value(), "1");
        assert_eq!(PhpOverride::parse(setting, "1K").unwrap().value(), "1K");
    }
}

#[test]
fn max_input_vars_zero_is_refused() {
    // Accepting no input variables at all breaks every form on the site, and
    // does so at the next request rather than at the settings page.
    assert!(matches!(
        PhpOverride::parse("max_input_vars", "0"),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

#[test]
fn a_value_exactly_at_the_maximum_is_accepted() {
    // The edge, pinned in the direction that matters: an off-by-one the other
    // way silently rejects the documented ceiling and reads to a customer as
    // the panel being broken.
    assert_eq!(
        PhpOverride::parse("memory_limit", "512M").unwrap().value(),
        "512M"
    );
    assert_eq!(
        PhpOverride::parse("max_execution_time", "300")
            .unwrap()
            .value(),
        "300"
    );
    assert_eq!(
        PhpOverride::parse("max_input_vars", "10000")
            .unwrap()
            .value(),
        "10000"
    );
}

#[test]
fn a_kibibyte_suffix_scales_the_same_way_a_mebibyte_one_does() {
    // 1024K is 1 MiB and well under the ceiling. A suffix table that scaled K
    // wrongly would either reject ordinary values or let an enormous one past.
    assert_eq!(
        PhpOverride::parse("memory_limit", "1024K").unwrap().value(),
        "1024K"
    );
    assert!(matches!(
        PhpOverride::parse("memory_limit", "524289K"),
        Err(PhpOpError::OverrideOutOfRange { .. })
    ));
}

#[test]
fn an_empty_value_is_refused_for_every_kind() {
    // Empty renders as `php_value[memory_limit] = ` — accepted by php-fpm and
    // read by PHP as an empty setting, which is not what the customer asked
    // for and is not reported to them either.
    for name in ["memory_limit", "max_execution_time", "max_input_vars"] {
        assert!(
            matches!(
                PhpOverride::parse(name, ""),
                Err(PhpOpError::OverrideMalformed { .. })
            ),
            "an empty {name} was accepted"
        );
    }
    assert!(matches!(
        PhpOverride::parse("date.timezone", ""),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

#[test]
fn a_non_numeric_count_is_refused() {
    assert!(matches!(
        PhpOverride::parse("max_input_vars", "many"),
        Err(PhpOpError::OverrideMalformed { .. })
    ));
}

/// Parses the named settings into one override set.
fn overrides(settings: &[(&str, &str)]) -> Vec<PhpOverride> {
    settings
        .iter()
        .map(|(name, value)| PhpOverride::parse(name, value).unwrap())
        .collect()
}

#[test]
fn a_set_naming_neither_size_is_consistent() {
    assert!(PhpOverride::ensure_consistent(&overrides(&[("memory_limit", "256M")])).is_ok());
    assert!(PhpOverride::ensure_consistent(&[]).is_ok());
}

#[test]
fn a_post_size_equal_to_the_upload_size_is_consistent() {
    let set = overrides(&[("upload_max_filesize", "64M"), ("post_max_size", "64M")]);

    assert!(PhpOverride::ensure_consistent(&set).is_ok());
}

#[test]
fn a_post_size_below_the_upload_size_is_refused_with_both_figures() {
    let set = overrides(&[("upload_max_filesize", "64M"), ("post_max_size", "16M")]);

    match PhpOverride::ensure_consistent(&set) {
        Err(PhpOpError::OverridePostSizeBelowUpload {
            post_max_size,
            upload_max_filesize,
        }) => {
            assert_eq!(post_max_size, 16 * 1024 * 1024);
            assert_eq!(upload_max_filesize, 64 * 1024 * 1024);
        }
        other => panic!("expected OverridePostSizeBelowUpload, got {other:?}"),
    }
}

#[test]
fn an_upload_size_above_the_default_post_size_is_refused_when_post_is_unset() {
    // The pool's post_max_size is the 32M default, so a 64M file could be
    // accepted by the upload limit and discarded by the body limit.
    let set = overrides(&[("upload_max_filesize", "64M")]);

    assert!(matches!(
        PhpOverride::ensure_consistent(&set),
        Err(PhpOpError::OverridePostSizeBelowUpload { .. })
    ));
}

#[test]
fn a_post_size_below_the_default_upload_size_is_refused_when_upload_is_unset() {
    let set = overrides(&[("post_max_size", "8M")]);

    assert!(matches!(
        PhpOverride::ensure_consistent(&set),
        Err(PhpOpError::OverridePostSizeBelowUpload { .. })
    ));
}

#[test]
fn a_larger_post_size_alone_is_consistent_with_the_default_upload_size() {
    let set = overrides(&[("post_max_size", "128M")]);

    assert!(PhpOverride::ensure_consistent(&set).is_ok());
}

#[test]
fn sizes_are_compared_in_bytes_not_as_text() {
    // "100M" > "20M" as bytes although "100M" < "20M" as text.
    let set = overrides(&[("upload_max_filesize", "100M"), ("post_max_size", "20M")]);

    assert!(PhpOverride::ensure_consistent(&set).is_err());
    let set = overrides(&[("upload_max_filesize", "20M"), ("post_max_size", "100M")]);
    assert!(PhpOverride::ensure_consistent(&set).is_ok());
}

#[test]
fn the_default_size_constant_and_its_byte_figure_agree() {
    use crate::php::model::php_override::{DEFAULT_SIZE, DEFAULT_SIZE_BYTES, parse_bytes};

    assert_eq!(
        parse_bytes("post_max_size", DEFAULT_SIZE).unwrap(),
        DEFAULT_SIZE_BYTES
    );
}
