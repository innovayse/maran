//! The statement-level half of the restricted loader's pre-scan.

use crate::backup::model::dump_refusal::DumpRefusal;
use crate::backup::model::dump_token::DumpToken;

/// How many tokens of an ordinary statement are kept to classify it.
///
/// The first few words decide what a statement is; the rest of an `INSERT` is
/// gigabytes of values that are never held.
const PLAIN_TOKEN_LIMIT: usize = 8;

/// How many tokens of a `SET` statement are kept. A statement longer than this
/// is refused: no dump sets that many variables at once.
const SET_TOKEN_LIMIT: usize = 64;

/// Words that never belong in a dump, wherever they stand.
///
/// `NO_BACKSLASH_ESCAPES` changes how the server reads a quote the scanner has
/// already read the old way; `CONNECTION` and `DIRECTORY` are the table options
/// that reach another server or a path; the rest read or write a file, or hold a
/// server thread.
const FORBIDDEN_WORDS: [&str; 9] = [
    "OUTFILE",
    "DUMPFILE",
    "INFILE",
    "LOAD_FILE",
    "SLEEP",
    "BENCHMARK",
    "CONNECTION",
    "DIRECTORY",
    "NO_BACKSLASH_ESCAPES",
];

/// First words that are commands of the database client, not statements.
const CLIENT_COMMANDS: [&str; 22] = [
    "DELIMITER",
    "SOURCE",
    "SYSTEM",
    "TEE",
    "NOTEE",
    "PAGER",
    "NOPAGER",
    "EDIT",
    "EGO",
    "GO",
    "CONNECT",
    "CHARSET",
    "PROMPT",
    "REHASH",
    "STATUS",
    "WARNINGS",
    "NOWARNING",
    "PRINT",
    "QUIT",
    "EXIT",
    "HELP",
    "RESETCONNECTION",
];

/// Character sets whose multi-byte characters can end in the byte of a backslash
/// or a quote, so the server reads a literal's end where a byte scan does not.
/// Matched as a prefix of a word and anywhere in a literal, which covers their
/// collations.
const UNSAFE_CHARSETS: [&str; 12] = [
    "sjis", "big5", "gbk", "gb2312", "gb18030", "cp932", "eucjpms", "ujis", "euckr", "ucs2",
    "utf16", "utf32",
];

/// The session variables a dump may assign.
const SESSION_VARIABLES: [&str; 12] = [
    "CHARACTER_SET_CLIENT",
    "CHARACTER_SET_RESULTS",
    "CHARACTER_SET_CONNECTION",
    "COLLATION_CONNECTION",
    "FOREIGN_KEY_CHECKS",
    "UNIQUE_CHECKS",
    "SQL_MODE",
    "SQL_NOTES",
    "NOTE_VERBOSITY",
    "TIME_ZONE",
    "AUTOCOMMIT",
    "SQL_QUOTE_SHOW_CREATE",
];

/// Classifies the statements of a dump one token at a time.
///
/// The decision is an ALLOW-list: a statement is accepted because its kind is
/// one a dump of tables and rows carries, never because it was not recognised as
/// hostile. A denylist is one forgotten statement from a `GRANT`.
///
/// What it is for, and what it is not. The temporary user the dump is loaded as
/// holds privileges on one database, so the server already refuses a `GRANT`, a
/// file write or another database's table. This type is the control for what no
/// privilege reaches: a line the database CLIENT acts on itself (a shell
/// command), and objects that would be created already broken. It is also the
/// reason a refusal names a kind and not a server error.
pub(crate) struct DumpStatementChecker {
    /// The database the dump is for; the only one it may name.
    database: String,
    /// The first tokens of the statement being read.
    tokens: Vec<DumpToken>,
    /// Whether the statement had more tokens than were kept.
    overflowed: bool,
    /// Whether the previous token was a name that a `.` may now qualify, and if
    /// so whether it is the database being loaded. `None` for any other token.
    qualifier: Option<bool>,
    /// Whether the statement so far qualified a name by another database. Judged
    /// when the statement ends, after its kind: a trigger is refused as a trigger.
    foreign_qualifier: bool,
    /// Whether the previous token was a `.`, so the name after it is a table or
    /// a column and not a database.
    after_dot: bool,
}

impl DumpStatementChecker {
    /// Creates a checker for a dump that is for `database`.
    pub(crate) fn new(database: &str) -> Self {
        Self {
            database: database.to_owned(),
            tokens: Vec::new(),
            overflowed: false,
            qualifier: None,
            foreign_qualifier: false,
            after_dot: false,
        }
    }

    /// Whether the lexer should keep the content of string literals now.
    ///
    /// True only inside a `SET`, the one statement in which a literal's content
    /// decides anything.
    pub(crate) fn wants_literal_content(&self) -> bool {
        matches!(self.tokens.first(), Some(DumpToken::Word(word)) if word.eq_ignore_ascii_case("SET"))
    }

    /// Takes the next token of the dump.
    ///
    /// # Errors
    ///
    /// The [`DumpRefusal`] of a word that has no place in a dump, and, on a
    /// terminator, of the statement it ends.
    pub(crate) fn accept(&mut self, token: DumpToken) -> Result<(), DumpRefusal> {
        if token == DumpToken::Terminator {
            return self.conclude();
        }
        if let DumpToken::Word(word) = &token {
            refuse_word(word)?;
        }
        self.check_qualifier(&token);

        let limit = if self.wants_literal_content() {
            SET_TOKEN_LIMIT
        } else {
            PLAIN_TOKEN_LIMIT
        };
        if self.tokens.len() < limit {
            self.tokens.push(token);
        } else {
            self.overflowed = true;
        }

        Ok(())
    }

    /// Notes a name qualified by a database other than the one being loaded,
    /// wherever in a statement it stands (`other`.`t`, `REFERENCES other.t`); the
    /// statement is refused when it ends, once its kind has been judged.
    ///
    /// Read as a stream over every token of the statement, not over the few that
    /// are kept: a foreign key or an `INSERT` target stands past them. A name is a
    /// qualifier when a `.` follows it and no `.` came before it, so the table of
    /// `db`.`t`.`c` is not judged as a database. Numbers (`1.5`) and variables
    /// (`@@session.x`) are not names. A `SET` is judged by its own rules.
    ///
    /// This is the second line, not the first: the temporary user's grant is
    /// escaped to this one database (`grant_pattern_for`), so the server refuses
    /// another database's table whatever the text says.
    fn check_qualifier(&mut self, token: &DumpToken) {
        let previous = self.qualifier.take();
        let after_dot = std::mem::replace(&mut self.after_dot, false);

        match token {
            DumpToken::Symbol(b'.') => {
                self.after_dot = true;
                if previous == Some(false) && !self.wants_literal_content() {
                    self.foreign_qualifier = true;
                }
            }
            DumpToken::Identifier(name) if !after_dot => {
                self.qualifier = Some(*name == self.database);
            }
            DumpToken::Word(name) if !after_dot && is_plain_name(name) => {
                self.qualifier = Some(*name == self.database);
            }
            _ => {}
        }
    }

    /// Ends the input: a statement without a terminator is still executed by the
    /// client, so it is judged like any other.
    ///
    /// # Errors
    ///
    /// The [`DumpRefusal`] of the last statement.
    pub(crate) fn finish(&mut self) -> Result<(), DumpRefusal> {
        self.conclude()
    }

    /// Judges the statement read so far and starts a new one.
    fn conclude(&mut self) -> Result<(), DumpRefusal> {
        self.qualifier = None;
        self.after_dot = false;
        let foreign_qualifier = std::mem::take(&mut self.foreign_qualifier);
        let tokens = std::mem::take(&mut self.tokens);
        let overflowed = std::mem::replace(&mut self.overflowed, false);

        let Some(first) = tokens.first() else {
            return Ok(());
        };
        let DumpToken::Word(first) = first else {
            return Err(DumpRefusal::ForbiddenStatement);
        };
        let keyword = first.to_ascii_uppercase();
        let second = word_at(&tokens, 1).map(str::to_ascii_uppercase);
        let second = second.as_deref();
        let after_second = tokens.get(2..).unwrap_or_default();

        let verdict = match keyword.as_str() {
            "SET" if overflowed => Err(DumpRefusal::UnsafeSet),
            "SET" => check_set(tokens.get(1..).unwrap_or_default()),
            "INSERT" | "REPLACE" | "BEGIN" | "COMMIT" => Ok(()),
            "START" => require(second == Some("TRANSACTION")),
            "LOCK" | "UNLOCK" => require(matches!(second, Some("TABLES" | "TABLE"))),
            "ALTER" => require(second == Some("TABLE")),
            "DROP" => match second {
                Some("TABLE") => Ok(()),
                Some("DATABASE" | "SCHEMA") => self.check_database_clause(after_second),
                _ => Err(DumpRefusal::ForbiddenStatement),
            },
            "CREATE" => match second {
                Some("TABLE") => Ok(()),
                Some("TEMPORARY") => {
                    require(word_at(&tokens, 2).is_some_and(|w| w.eq_ignore_ascii_case("TABLE")))
                }
                Some("DATABASE" | "SCHEMA") => self.check_database_clause(after_second),
                Some(
                    "TRIGGER" | "PROCEDURE" | "FUNCTION" | "EVENT" | "VIEW" | "ALGORITHM" | "SQL",
                ) => Err(DumpRefusal::StoredProgram),
                _ => Err(DumpRefusal::ForbiddenStatement),
            },
            // The client runs `USE` itself and reads its argument as raw text, so
            // nothing may follow the name for it to read differently.
            "USE" if tokens.len() != 2 => Err(DumpRefusal::OtherDatabase),
            "USE" => self.check_database_clause(tokens.get(1..).unwrap_or_default()),
            other if CLIENT_COMMANDS.contains(&other) => Err(DumpRefusal::ClientCommand),
            _ => Err(DumpRefusal::ForbiddenStatement),
        };

        if foreign_qualifier {
            verdict.and(Err(DumpRefusal::OtherDatabase))
        } else {
            verdict
        }
    }

    /// Requires the first name in `rest`, after any `IF [NOT] EXISTS`, to be the
    /// database this dump is for, and not to be followed by a second name.
    ///
    /// The second-name rule is for the doubled backtick: ``USE `a``b` `` is one
    /// identifier to the server and two tokens to the lexer, and the first of
    /// the two must not be allowed to vouch for the whole.
    fn check_database_clause(&self, rest: &[DumpToken]) -> Result<(), DumpRefusal> {
        let is_filler = |token: &DumpToken| {
            matches!(token, DumpToken::Word(word)
                if ["IF", "NOT", "EXISTS"].iter().any(|k| word.eq_ignore_ascii_case(k)))
        };
        let Some(position) = rest.iter().position(|token| !is_filler(token)) else {
            return Err(DumpRefusal::OtherDatabase);
        };
        if matches!(rest.get(position + 1), Some(DumpToken::Identifier(_))) {
            return Err(DumpRefusal::OtherDatabase);
        }

        match rest.get(position) {
            Some(DumpToken::Identifier(name) | DumpToken::Word(name)) if *name == self.database => {
                Ok(())
            }
            _ => Err(DumpRefusal::OtherDatabase),
        }
    }
}

/// Whether a bare word is a name, and not a number or a `@variable`.
fn is_plain_name(word: &str) -> bool {
    word.bytes()
        .next()
        .is_some_and(|byte| byte.is_ascii_alphabetic() || byte == b'_' || byte >= 0x80)
}

/// `Ok` when `allowed`, the statement's kind refusal otherwise.
fn require(allowed: bool) -> Result<(), DumpRefusal> {
    if allowed {
        Ok(())
    } else {
        Err(DumpRefusal::ForbiddenStatement)
    }
}

/// The word at `index`, when that token is one.
fn word_at(tokens: &[DumpToken], index: usize) -> Option<&str> {
    match tokens.get(index) {
        Some(DumpToken::Word(word)) => Some(word.as_str()),
        _ => None,
    }
}

/// Refuses a word that has no place in a dump, wherever it stands.
fn refuse_word(word: &str) -> Result<(), DumpRefusal> {
    if word.eq_ignore_ascii_case("DEFINER") {
        return Err(DumpRefusal::DefinerClause);
    }
    if FORBIDDEN_WORDS
        .iter()
        .any(|forbidden| word.eq_ignore_ascii_case(forbidden))
    {
        return Err(DumpRefusal::ForbiddenKeyword);
    }

    Ok(())
}

/// Judges a `SET` statement, given everything after the `SET`.
///
/// Parentheses are refused outright, so there is no function call and no
/// subquery to compute a value with. Hex and bit literals and character-set
/// introducers are refused, so a value cannot be spelled in a form the text
/// scan cannot read. Adjacent string literals are joined before the one word
/// that changes how the server reads a quote is looked for, because the server
/// joins them too.
fn check_set(rest: &[DumpToken]) -> Result<(), DumpRefusal> {
    if rest.is_empty() {
        return Err(DumpRefusal::UnsafeSet);
    }

    let mut joined = String::new();
    for token in rest {
        match token {
            DumpToken::Symbol(b'(' | b')') => return Err(DumpRefusal::UnsafeSet),
            DumpToken::Literal(content) => joined.push_str(content),
            DumpToken::Word(word) if !set_word_allowed(word) || names_unsafe_charset(word) => {
                return Err(DumpRefusal::UnsafeSet);
            }
            _ => {}
        }
    }
    if joined.to_ascii_uppercase().contains("NO_BACKSLASH_ESCAPES") {
        return Err(DumpRefusal::ForbiddenKeyword);
    }
    let lowered = joined.to_ascii_lowercase();
    if UNSAFE_CHARSETS.iter().any(|name| lowered.contains(name)) {
        return Err(DumpRefusal::UnsafeSet);
    }

    for assignment in rest.split(|token| *token == DumpToken::Symbol(b',')) {
        check_assignment(assignment)?;
    }

    Ok(())
}

/// Whether a bare word names a character set the byte scan cannot follow.
fn names_unsafe_charset(word: &str) -> bool {
    let lower = word.to_ascii_lowercase();

    UNSAFE_CHARSETS.iter().any(|name| lower.starts_with(name))
}

/// Whether a bare word may stand in a `SET` statement.
fn set_word_allowed(word: &str) -> bool {
    let lower = word.to_ascii_lowercase();

    !(lower.starts_with('_')
        || lower == "x"
        || lower == "b"
        || lower.starts_with("0x")
        || lower.starts_with("0b"))
}

/// Judges one assignment of a `SET`: what is being assigned to.
fn check_assignment(segment: &[DumpToken]) -> Result<(), DumpRefusal> {
    let Some(DumpToken::Word(head)) = segment.first() else {
        return Err(DumpRefusal::UnsafeSet);
    };
    if head.eq_ignore_ascii_case("NAMES") || head.eq_ignore_ascii_case("CHARACTER") {
        return Ok(());
    }

    let variable: &str = if let Some(name) = head.strip_prefix("@@") {
        if name.eq_ignore_ascii_case("SESSION") || name.eq_ignore_ascii_case("LOCAL") {
            match (segment.get(1), segment.get(2)) {
                (Some(DumpToken::Symbol(b'.')), Some(DumpToken::Word(variable))) => {
                    variable.as_str()
                }
                _ => return Err(DumpRefusal::UnsafeSet),
            }
        } else if matches!(segment.get(1), Some(DumpToken::Symbol(b'.'))) {
            return Err(DumpRefusal::UnsafeSet);
        } else {
            name
        }
    } else if head.starts_with('@') {
        return Ok(());
    } else if head.eq_ignore_ascii_case("SESSION") || head.eq_ignore_ascii_case("LOCAL") {
        match segment.get(1) {
            Some(DumpToken::Word(variable)) => variable.as_str(),
            _ => return Err(DumpRefusal::UnsafeSet),
        }
    } else {
        head.as_str()
    };

    if SESSION_VARIABLES
        .iter()
        .any(|allowed| variable.eq_ignore_ascii_case(allowed))
    {
        Ok(())
    } else {
        Err(DumpRefusal::UnsafeSet)
    }
}
