//! The one place a database name is written as a `GRANT` pattern.

/// The one character a database-level `GRANT` reads as a wildcard that a
/// [`DatabaseName`](maran_agent_core::validation::db::database_name::DatabaseName)
/// can actually hold.
///
/// `_` — matching any single character — and it is in every name this agent
/// grants on, because it is the separator between an account and the suffix its
/// customer asked for, and because an account name may contain it too.
pub(crate) const GRANT_SINGLE_CHARACTER_WILDCARD: char = '_';

/// What that character has to be written as to mean itself.
pub(crate) const ESCAPED_SINGLE_CHARACTER_WILDCARD: &str = "\\_";

/// [`GRANT_SINGLE_CHARACTER_WILDCARD`] as a string, for the one replacement that
/// puts it back.
const SINGLE_CHARACTER_WILDCARD_AS_STRING: &str = "_";

/// Writes `database` as a `GRANT` pattern that matches that database and nothing
/// else.
///
/// **The database name of a database-level `GRANT` is a pattern, not an
/// identifier**, and this is the only place in this agent where that is true. In
/// MySQL and MariaDB `_` matches any single character there and `%` any
/// sequence; backtick-quoting does not turn it off, which is why the server's own
/// manual tells you to escape the character instead — the escaped form is what
/// stops the grantee "being able to access additional databases matching the
/// wildcard pattern".
///
/// It is not a theoretical difference here, because every name this agent grants
/// on holds the character. A name is `<account>_<suffix>`, so it carries the
/// separator at least; and an account name may itself contain underscores, which
/// is what turns one grant into a claim on other accounts' databases. Account
/// `h_stco` asking for `main` produces `h_stco_main`, and that name unescaped is
/// the pattern `h?stco?main`, which matches account `hostco`'s `hostco_main`
/// exactly. Patterns are stored in `mysql.db` and matched when a connection asks
/// for a database, so the reach extends to databases created after the grant.
///
/// `%` is deliberately NOT escaped, and that is a statement about the alphabet
/// rather than an oversight: a `DatabaseName` is built only by
/// `DatabaseName::for_account`, whose account half is `[a-z0-9_]` and whose
/// requested half is `[a-z0-9]`, so `%` cannot be in one. An escape for a
/// character that cannot arrive is a defensive call that cannot fail, which
/// rules/testing.md says to delete rather than label — so this comment carries
/// the reason instead, and the day that alphabet widens, this function is where
/// the second character goes.
///
/// The escape belongs here and NOT in `DatabaseName`: `CREATE DATABASE` and
/// `DROP DATABASE` take the name as an identifier, where a backslash is part of
/// the name, so escaping upstream would create a database that is actually called
/// `h\_stco\_main`.
///
/// # Why this is its own file rather than private to `create_database`
///
/// It was private to `create_database` until `repair_grants` arrived, which has
/// to write the SAME escaped form for a grant issued by an older agent — and a
/// second copy of the escape is a second answer to the question of what a
/// correct grant looks like. `repair_grants` decides whether a stored row needs
/// repair by comparing it against what this function produces, so the two cannot
/// disagree without the repair either skipping a broken row or rewriting a
/// correct one for ever.
pub(crate) fn grant_pattern_for(database: &str) -> String {
    database.replace(
        GRANT_SINGLE_CHARACTER_WILDCARD,
        ESCAPED_SINGLE_CHARACTER_WILDCARD,
    )
}

/// Reads a stored `GRANT` pattern back as the database name it was built from.
///
/// The exact inverse of [`grant_pattern_for`], and it lives beside it for the
/// reason every decoder in this workspace lives beside its constructor: a repair
/// that recognises an already-escaped row has to agree, character for character,
/// with the function that wrote it.
///
/// It is a partial inverse and the caller must treat it as one. Applied to a
/// pattern this panel never wrote — one escaped in part, or escaping some other
/// character — it returns a string with a backslash still in it, which is how
/// `repair_grants` tells the two apart: it re-escapes the result and refuses the
/// row unless it gets the stored bytes back.
pub(crate) fn unescaped_grant_pattern(pattern: &str) -> String {
    pattern.replace(
        ESCAPED_SINGLE_CHARACTER_WILDCARD,
        SINGLE_CHARACTER_WILDCARD_AS_STRING,
    )
}

#[cfg(test)]
#[path = "../tests/db/grant_pattern_tests.rs"]
mod tests;
