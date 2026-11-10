//! The one place a database name is written as a `GRANT` pattern.

/// The one character a database-level `GRANT` reads as a wildcard that a
/// [`DatabaseName`](crate::validation::db::database_name::DatabaseName)
/// can actually hold.
///
/// `_` — matching any single character — and it is in every name this agent
/// grants on, because it is the separator between an account and the suffix its
/// customer asked for, and because an account name may contain it too.
pub const GRANT_SINGLE_CHARACTER_WILDCARD: char = '_';

/// The other character a database-level `GRANT` reads as a wildcard: `%`,
/// matching any sequence of characters.
///
/// No [`DatabaseName`](crate::validation::db::database_name::DatabaseName) can
/// hold it today, and it is escaped anyway: the helper is the one place a name
/// becomes a pattern, and a pattern writer that is correct only for the alphabet
/// of today's callers is the defect this file exists to remove. The escape costs
/// nothing for a name without the character.
pub const GRANT_ANY_SEQUENCE_WILDCARD: char = '%';

/// The character that makes either wildcard mean itself.
pub(crate) const GRANT_ESCAPE: char = '\\';

/// Writes `database` as a `GRANT` pattern that matches that database and nothing
/// else.
///
/// **The database name of a database-level `GRANT` is a pattern, not an
/// identifier**, and this is the only place in this agent where that is true. In
/// MySQL and MariaDB `_` matches any single character there and `%` any
/// sequence; backtick-quoting does not turn it off, which is why the server's own
/// manual tells you to escape the character instead - the escaped form is what
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
/// **Every statement that names a database in the position of a grant uses this
/// function**: `GRANT` and `REVOKE` in `ops::db` (creation, privilege changes,
/// drop-database revocation, grant repair) and the temporary user of the
/// restricted dump loader in `ops::backup`. It lives in `agent-core` because two
/// areas need it and areas never import each other.
///
/// The escape belongs here and NOT in `DatabaseName`: `CREATE DATABASE` and
/// `DROP DATABASE` take the name as an identifier, where a backslash is part of
/// the name, so escaping upstream would create a database that is actually called
/// `h\_stco\_main`.
///
/// It is also the reason `repair_grants` can decide whether a stored row needs
/// repair by comparing it against what this function produces: the two cannot
/// disagree without the repair either skipping a broken row or rewriting a
/// correct one for ever.
#[must_use]
pub fn grant_pattern_for(database: &str) -> String {
    let mut pattern = String::with_capacity(database.len() + 4);
    for character in database.chars() {
        if character == GRANT_SINGLE_CHARACTER_WILDCARD || character == GRANT_ANY_SEQUENCE_WILDCARD
        {
            pattern.push(GRANT_ESCAPE);
        }
        pattern.push(character);
    }

    pattern
}

#[cfg(test)]
#[path = "../../tests/validation/db/grant_pattern_for_tests.rs"]
mod tests;
