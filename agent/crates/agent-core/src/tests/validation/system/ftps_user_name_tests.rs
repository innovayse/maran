//! Tests for the `ftps_user_name` module.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md); the
//! source file declares this module with `#[path]`, keeping it a child able to
//! reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::system::ftps_user_name_error::FtpsUserNameError;
use crate::validation::system::name::AccountName;

use super::FtpsUserName;

/// The account every test here builds a login for.
fn account() -> AccountName {
    AccountName::parse("acme").expect("a valid account name")
}

#[test]
fn an_ftps_login_is_named_after_the_account_that_owns_it() {
    let name = FtpsUserName::for_account(&account(), "web").expect("a valid request");

    assert_eq!(name.as_str(), "acme_web");
}

#[test]
fn a_newline_in_the_request_is_refused_by_the_alphabet_and_not_by_a_sweep() {
    // vsftpd's per-user configuration is a line-oriented `key=value` file whose
    // NAME is this value, and `/etc/passwd` is line-oriented too. A newline
    // here is the panel's SQL injection (rules/security.md §4). The assertion
    // names the character it refuses, so a widened alphabet cannot pass this
    // test by refusing something else.
    let result = FtpsUserName::for_account(&account(), "web\nchroot_local_user=NO");

    assert!(matches!(
        result,
        Err(FtpsUserNameError::UnexpectedCharacter { character: '\n' })
    ));
}

#[test]
fn a_carriage_return_is_refused_as_its_own_named_character() {
    let result = FtpsUserName::for_account(&account(), "web\rmore");

    assert!(matches!(
        result,
        Err(FtpsUserNameError::UnexpectedCharacter { character: '\r' })
    ));
}

#[test]
fn a_traversal_is_refused_at_the_first_dot_it_contains() {
    // The name becomes a path segment under the jail root. `..` never reaches a
    // path join, because the `.` is outside the alphabet.
    let result = FtpsUserName::for_account(&account(), "../../etc/passwd");

    assert!(matches!(
        result,
        Err(FtpsUserNameError::UnexpectedCharacter { character: '.' })
    ));
}

#[test]
fn a_leading_dash_is_refused_so_a_name_cannot_be_read_as_an_option() {
    // `useradd -- <name>` is what this agent writes, but the name is also
    // passed to other tools by later tasks, and a leading `-` is the argument
    // that turns a name into a flag. It is refused here rather than depended on
    // downstream, and the refusal names the character.
    let result = FtpsUserName::for_account(&account(), "-rf");

    assert!(matches!(
        result,
        Err(FtpsUserNameError::UnexpectedCharacter { character: '-' })
    ));
}

#[test]
fn an_underscore_in_the_request_is_rejected_so_ownership_cannot_be_forged() {
    // `AccountName` permits underscores, so `bob_deploy` requested by `acme`
    // would produce `acme_bob_deploy` — a login that reads as `acme_bob`'s in
    // `/etc/passwd`, and that `decode` would hand to that account.
    let result = FtpsUserName::for_account(&account(), "bob_deploy");

    assert!(matches!(
        result,
        Err(FtpsUserNameError::UnexpectedCharacter { character: '_' })
    ));
}

#[test]
fn an_empty_request_is_rejected() {
    assert!(matches!(
        FtpsUserName::for_account(&account(), ""),
        Err(FtpsUserNameError::Empty)
    ));
}

#[test]
fn a_request_that_overflows_the_useradd_limit_is_rejected_with_its_length() {
    // "acme" + "_" + 28 = 33 bytes, one past the 32-byte useradd limit. The
    // length is asserted, not merely the variant: a ceiling that moved would
    // otherwise still produce a `TooLong` and pass.
    let result = FtpsUserName::for_account(&account(), &"a".repeat(28));

    assert!(matches!(
        result,
        Err(FtpsUserNameError::TooLong { length: 33 })
    ));
}

#[test]
fn a_request_that_exactly_fills_the_useradd_limit_is_accepted() {
    // The inverse control on the ceiling: a type that refused everything would
    // pass every refusal test above. "acme" + "_" + 27 = 32 bytes, which
    // `useradd` accepts on both families.
    let name = FtpsUserName::for_account(&account(), &"a".repeat(27)).expect("a valid request");

    assert_eq!(name.as_str().len(), 32);
    assert_eq!(name.as_str(), format!("acme_{}", "a".repeat(27)));
}

#[test]
fn an_accepted_name_is_a_name_systemds_escaping_rule_leaves_unchanged() {
    // A login name reaches unit-adjacent places, and systemd escapes anything
    // outside `[A-Za-z0-9_.]` into `\xNN` — so a name containing `-` or a space
    // would be one spelling in `/etc/passwd` and another wherever systemd
    // derives a name from it. The alphabet here is a strict subset of the set
    // systemd keeps, which is what makes the two spellings the same string. The
    // escaping rule itself lives in `maran-ops` and cannot be called from this
    // crate, so the property is asserted on the characters it would act on.
    let name = FtpsUserName::for_account(&account(), "web2").expect("a valid request");

    assert!(
        name.as_str()
            .chars()
            .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '_'),
        "an accepted name must hold nothing systemd would escape, got {:?}",
        name.as_str()
    );
}

#[test]
fn an_accepted_name_satisfies_useradds_own_name_regex() {
    // useradd's NAME_REGEX is `[a-z_][a-z0-9_-]*`. The prefix is an
    // AccountName, which already begins with a lowercase letter, and every
    // character after it is [a-z0-9_].
    let name = FtpsUserName::for_account(&account(), "web2").expect("a valid request");
    let mut characters = name.as_str().chars();
    let first = characters.next().expect("a non-empty name");

    assert!(first.is_ascii_lowercase() || first == '_');
    assert!(characters.all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '_'));
}

#[test]
fn every_character_that_would_forge_a_directive_or_a_path_is_rejected() {
    // Named one by one, because "rejects bad input" is satisfied by a type that
    // rejects everything. Each of these is a real vector: a directive appended
    // to vsftpd.conf, a path segment leaving the jail, a shell metacharacter in
    // a log line, an uppercase letter `useradd` refuses on some hosts.
    for requested in [
        "web\nchroot_local_user=NO",
        "web\r",
        "web\0",
        "web ",
        " web",
        "web:",
        "web/../root",
        "web'",
        "Web",
        "web-1",
        "web.conf",
        "wéb",
    ] {
        assert!(
            FtpsUserName::for_account(&account(), requested).is_err(),
            "{requested:?} must be rejected"
        );
    }
}

#[test]
fn every_name_the_constructor_accepts_decodes_back_to_itself() {
    // Construction and its inverse must agree for EVERY accepted input, not
    // only the tidy one. The accounts are the cases that could break an
    // `rsplit`: a plain name, one whose own name holds the separator, and one
    // that is mostly separators.
    for account_name in ["acme", "alice_bob", "a_b_c_d"] {
        let owner = AccountName::parse(account_name).expect("a valid account name");

        for requested in ["web", "a", "shop2024", "x1"] {
            let built = FtpsUserName::for_account(&owner, requested).expect("a valid request");

            let decoded = FtpsUserName::decode(&owner, built.as_str());

            assert_eq!(
                decoded.map(|name| name.as_str().to_owned()),
                Some(built.as_str().to_owned()),
                "{account_name} / {requested} must round-trip"
            );
        }
    }
}

#[test]
fn a_name_that_exactly_fills_the_limit_still_round_trips() {
    // The boundary the constructor accepts is the boundary the decoder must
    // too: `decode` rebuilds through `for_account`, so a ceiling that disagreed
    // by one byte would drop the longest names an account owns and nothing else.
    let built = FtpsUserName::for_account(&account(), &"a".repeat(27)).expect("a valid request");

    let decoded =
        FtpsUserName::decode(&account(), built.as_str()).expect("the longest name round-trips");

    assert_eq!(decoded.as_str().len(), 32);
}

#[test]
fn another_accounts_name_does_not_decode() {
    // `alice_` is a prefix of `alice_bob_web`, so a prefix scan would hand this
    // account another tenant's login — and an account deletion enumerating
    // logins would then remove it.
    let other = AccountName::parse("alice_bob").expect("a valid account name");
    let theirs = FtpsUserName::for_account(&other, "web").expect("a valid request");
    let alice = AccountName::parse("alice").expect("a valid account name");

    assert!(FtpsUserName::decode(&alice, theirs.as_str()).is_none());
}

#[test]
fn a_name_that_was_never_built_by_this_agent_decodes_to_nothing() {
    // `decode` refuses rather than guessing: these reach it from the host's own
    // password database, and a plausible-looking answer for a name outside the
    // convention is what would put another tenant's login — or root — into a
    // listing or a deletion.
    for candidate in [
        "",              // nothing at all
        "root",          // an account nobody here created
        "acme",          // no separator: the account's own name is not a login
        "_",             // the separator alone: both halves empty
        "___",           // all separators, which the requested half may not hold
        "acme_",         // the account, then nothing requested
        "_web",          // a requested half with no account in front
        "other_web",     // a different account entirely
        "acme_Web",      // outside the alphabet, so this agent never created it
        "acme_web_more", // decodes to account `acme_web`, which is not `acme`
    ] {
        assert!(
            FtpsUserName::decode(&account(), candidate).is_none(),
            "{candidate:?} must not decode"
        );
    }
}
