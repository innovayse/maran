//! The byte-level half of the restricted loader's pre-scan.

use crate::backup::dump_statement_checker::DumpStatementChecker;
use crate::backup::model::dump_refusal::DumpRefusal;
use crate::backup::model::dump_token::DumpToken;

/// Longest bare word kept; longer words are cut, which no keyword reaches.
const WORD_LIMIT: usize = 64;

/// Longest backtick identifier kept.
const IDENTIFIER_LIMIT: usize = 128;

/// Most bytes of a string literal kept inside a `SET`; a longer one is refused.
const LITERAL_LIMIT: usize = 4096;

/// The version number from which an executable comment can never run.
///
/// No server has a version near it (it would be major 99), and the dump tools
/// themselves write `/*M!999999\- enable the sandbox mode */` as the first line
/// of a dump with exactly that number. Such a comment is not code to the server,
/// but it is still text to the CLIENT, so it is read in a state of its own that
/// allows no quote, no `;` and no backslash but the one the dump tools write.
const NEVER_RUN_VERSION: u32 = 990_000;

/// Where in the text the lexer is.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum State {
    /// Between tokens, or inside a bare word.
    Normal,
    /// After a `/`: a comment may be starting.
    Slash,
    /// After `/*`: deciding whether the comment is executable.
    CommentOpen,
    /// After `/*M`: an executable comment if a `!` follows.
    MariaDbMarker,
    /// After `/*!`: the version number, then code.
    VersionDigits,
    /// Inside an ordinary comment.
    Block,
    /// Inside an ordinary comment, after a `*`.
    BlockStar,
    /// After one `-`.
    Dash,
    /// After `--`: a comment only if whitespace or a control byte follows.
    DashDash,
    /// Inside an executable comment, after a `*`: the end if a `/` follows.
    Star,
    /// Inside a `#` or `-- ` comment, to the end of the line.
    LineComment,
    /// Inside an executable comment whose version no server reaches.
    DeadComment,
    /// Inside such a comment, after a `*`.
    DeadStar,
    /// Inside such a comment, after a backslash.
    DeadBackslash,
    /// Inside a string literal opened by this quote.
    Quote(u8),
    /// Inside a string literal, after a backslash.
    QuoteEscape(u8),
    /// Inside a backtick identifier.
    Backtick,
}

/// Reads a dump byte by byte into tokens and hands them to a
/// [`DumpStatementChecker`].
///
/// A state machine and not a buffer, so a dump of any size is read in constant
/// memory and a chunk boundary can fall anywhere, including inside `/*!`.
///
/// It lexes what the database CLIENT lexes, because the client decides where a
/// statement ends and what is a command. Three consequences are deliberate. A
/// version comment (`/*!50003 ... */`, `/*M!100000 ... */`) is CODE, since the
/// server runs it: its content is read as tokens, never skipped. A backslash
/// outside a quote is refused as a client command, wherever it stands in the
/// line. And a literal's backslash escapes the next byte, as in the server's
/// default mode; the one setting that changes that is refused by word.
///
/// Residual, stated: the lexer and the client could still disagree on a rule
/// this was not built against. The server-side privilege of the loader's user
/// bounds what a disagreement can do to the DATABASE, and the client is also
/// started in binary mode, which turns its commands off; nothing bounds a
/// client command except these two, which is why neither is the only control.
pub(crate) struct DumpLexer {
    /// Where in the text the lexer is.
    state: State,
    /// The bare word being read.
    word: Vec<u8>,
    /// The backtick identifier being read.
    identifier: Vec<u8>,
    /// The string literal being read, when its content is kept.
    literal: Vec<u8>,
    /// Whether the lexer is inside an executable comment.
    in_version_comment: bool,
    /// Whether that comment is a `/*M!` one, which only a MariaDB client reads
    /// as code; a MySQL client reads it as an ordinary comment.
    maria_comment: bool,
    /// The version number of the executable comment being opened.
    version: u32,
    /// The statement-level judge.
    checker: DumpStatementChecker,
}

impl DumpLexer {
    /// Creates a lexer for a dump that is for `database`.
    pub(crate) fn new(database: &str) -> Self {
        Self {
            state: State::Normal,
            word: Vec::new(),
            identifier: Vec::new(),
            literal: Vec::new(),
            in_version_comment: false,
            maria_comment: false,
            version: 0,
            checker: DumpStatementChecker::new(database),
        }
    }

    /// Reads the next bytes of the dump.
    ///
    /// # Errors
    ///
    /// The first [`DumpRefusal`] the text earns. Nothing is read after it.
    pub(crate) fn feed(&mut self, bytes: &[u8]) -> Result<(), DumpRefusal> {
        for &byte in bytes {
            self.push(byte)?;
        }

        Ok(())
    }

    /// Ends the input and judges the last statement.
    ///
    /// # Errors
    ///
    /// [`DumpRefusal::Malformed`] when the text ends inside a quote or a comment,
    /// otherwise the refusal of the last statement.
    pub(crate) fn finish(mut self) -> Result<(), DumpRefusal> {
        self.flush_word()?;
        match self.state {
            State::Normal | State::LineComment | State::Dash | State::DashDash => {}
            State::Slash => self.emit(DumpToken::Symbol(b'/'))?,
            State::Star => self.emit(DumpToken::Symbol(b'*'))?,
            _ => return Err(DumpRefusal::Malformed),
        }
        if self.in_version_comment {
            return Err(DumpRefusal::Malformed);
        }

        self.checker.finish()
    }

    /// Hands a token to the checker.
    fn emit(&mut self, token: DumpToken) -> Result<(), DumpRefusal> {
        self.checker.accept(token)
    }

    /// Ends the bare word being read, if there is one.
    fn flush_word(&mut self) -> Result<(), DumpRefusal> {
        if self.word.is_empty() {
            return Ok(());
        }
        let text = String::from_utf8_lossy(&self.word).into_owned();
        self.word.clear();

        self.emit(DumpToken::Word(text))
    }

    /// Reads one byte in whatever state the lexer is in.
    fn push(&mut self, byte: u8) -> Result<(), DumpRefusal> {
        match self.state {
            State::Normal => self.normal(byte),
            State::Slash => {
                if byte == b'*' {
                    self.state = State::CommentOpen;
                    Ok(())
                } else {
                    self.state = State::Normal;
                    self.emit(DumpToken::Symbol(b'/'))?;
                    self.normal(byte)
                }
            }
            State::CommentOpen => match byte {
                b'!' => {
                    self.open_version_comment(false);
                    Ok(())
                }
                b'M' => {
                    self.state = State::MariaDbMarker;
                    Ok(())
                }
                _ => {
                    self.state = State::Block;
                    self.push(byte)
                }
            },
            State::MariaDbMarker => {
                if byte == b'!' {
                    self.open_version_comment(true);
                    Ok(())
                } else {
                    self.state = State::Block;
                    self.push(byte)
                }
            }
            State::VersionDigits => {
                if byte.is_ascii_digit() {
                    self.version = self
                        .version
                        .saturating_mul(10)
                        .saturating_add(u32::from(byte - b'0'));
                    Ok(())
                } else if self.version >= NEVER_RUN_VERSION {
                    self.in_version_comment = false;
                    self.maria_comment = false;
                    self.state = State::DeadComment;
                    self.push(byte)
                } else {
                    self.state = State::Normal;
                    self.normal(byte)
                }
            }
            State::DeadComment => match byte {
                b'*' => {
                    self.state = State::DeadStar;
                    Ok(())
                }
                b'\\' => {
                    self.state = State::DeadBackslash;
                    Ok(())
                }
                b'\'' | b'"' | b'`' | b';' => Err(DumpRefusal::Malformed),
                _ => Ok(()),
            },
            State::DeadStar => {
                if byte == b'/' {
                    self.state = State::Normal;
                    Ok(())
                } else {
                    self.state = State::DeadComment;
                    self.push(byte)
                }
            }
            State::DeadBackslash => {
                if byte == b'-' {
                    self.state = State::DeadComment;
                    Ok(())
                } else {
                    Err(DumpRefusal::ClientCommand)
                }
            }
            State::Block => {
                if byte == b'*' {
                    self.state = State::BlockStar;
                }
                Ok(())
            }
            State::BlockStar => {
                self.state = match byte {
                    b'/' => State::Normal,
                    b'*' => State::BlockStar,
                    _ => State::Block,
                };
                Ok(())
            }
            State::Dash => {
                if byte == b'-' {
                    self.state = State::DashDash;
                    Ok(())
                } else {
                    self.state = State::Normal;
                    self.emit(DumpToken::Symbol(b'-'))?;
                    self.normal(byte)
                }
            }
            State::DashDash => {
                if is_client_space(byte) {
                    self.state = if byte == b'\n' {
                        State::Normal
                    } else {
                        State::LineComment
                    };
                    Ok(())
                } else if byte < b' ' {
                    // The server starts a comment here and the client does not.
                    Err(DumpRefusal::Malformed)
                } else {
                    self.state = State::Normal;
                    self.emit(DumpToken::Symbol(b'-'))?;
                    self.emit(DumpToken::Symbol(b'-'))?;
                    self.normal(byte)
                }
            }
            State::Star => {
                if byte == b'/' {
                    self.state = State::Normal;
                    self.in_version_comment = false;
                    self.maria_comment = false;
                    Ok(())
                } else {
                    self.state = State::Normal;
                    self.emit(DumpToken::Symbol(b'*'))?;
                    self.normal(byte)
                }
            }
            State::LineComment => {
                if byte == b'\n' {
                    self.state = State::Normal;
                }
                Ok(())
            }
            State::Quote(quote) => self.in_quote(quote, byte),
            State::QuoteEscape(quote) => {
                self.keep_literal_byte(byte)?;
                self.state = State::Quote(quote);
                Ok(())
            }
            State::Backtick => {
                if byte == b'\\' {
                    // The client escapes the next byte inside any quote, the server
                    // does not inside a backtick one; no identifier needs it.
                    Err(DumpRefusal::Malformed)
                } else if byte == b'`' {
                    let name = String::from_utf8_lossy(&self.identifier).into_owned();
                    self.identifier.clear();
                    self.state = State::Normal;
                    self.emit(DumpToken::Identifier(name))
                } else {
                    if self.identifier.len() < IDENTIFIER_LIMIT {
                        self.identifier.push(byte);
                    }
                    Ok(())
                }
            }
        }
    }

    /// Starts an executable comment: what follows is a version number, then code.
    ///
    /// `maria` is true for `/*M!`. A MySQL client reads that as an ordinary
    /// comment and a MariaDB one as code, so [`Self::normal`] lets such a comment
    /// hold nothing on which the two readings part: no quote, no `;`, no
    /// backslash, no `#`, `-` or `/`.
    fn open_version_comment(&mut self, maria: bool) {
        self.state = State::VersionDigits;
        self.in_version_comment = true;
        self.maria_comment = maria;
        self.version = 0;
    }

    /// Reads one byte inside a string literal.
    fn in_quote(&mut self, quote: u8, byte: u8) -> Result<(), DumpRefusal> {
        if byte == b'\\' {
            self.state = State::QuoteEscape(quote);
            return Ok(());
        }
        if byte == quote {
            let content = String::from_utf8_lossy(&self.literal).into_owned();
            self.literal.clear();
            self.state = State::Normal;
            return self.emit(DumpToken::Literal(content));
        }

        self.keep_literal_byte(byte)
    }

    /// Keeps one byte of a literal's content when the checker wants it.
    ///
    /// # Errors
    ///
    /// [`DumpRefusal::UnsafeSet`] when a literal inside a `SET` is longer than
    /// the lexer will hold.
    fn keep_literal_byte(&mut self, byte: u8) -> Result<(), DumpRefusal> {
        if !self.checker.wants_literal_content() {
            return Ok(());
        }
        if self.literal.len() >= LITERAL_LIMIT {
            return Err(DumpRefusal::UnsafeSet);
        }
        self.literal.push(byte);

        Ok(())
    }

    /// Reads one byte between tokens or inside a bare word.
    fn normal(&mut self, byte: u8) -> Result<(), DumpRefusal> {
        if is_word_byte(byte) {
            if self.word.len() < WORD_LIMIT {
                self.word.push(byte);
            }
            return Ok(());
        }
        self.flush_word()?;
        if self.maria_comment
            && matches!(
                byte,
                b'\'' | b'"' | b'`' | b';' | b'\\' | b'#' | b'-' | b'/'
            )
        {
            return Err(DumpRefusal::Malformed);
        }

        match byte {
            b' ' | b'\t' | b'\n' | b'\r' | 0x0b | 0x0c => Ok(()),
            0 => Err(DumpRefusal::Malformed),
            b';' => self.emit(DumpToken::Terminator),
            b'\\' => Err(DumpRefusal::ClientCommand),
            b'\'' | b'"' => {
                self.state = State::Quote(byte);
                Ok(())
            }
            b'`' => {
                self.state = State::Backtick;
                self.identifier.clear();
                Ok(())
            }
            b'#' => {
                self.state = State::LineComment;
                Ok(())
            }
            b'-' => {
                self.state = State::Dash;
                Ok(())
            }
            b'/' => {
                self.state = State::Slash;
                Ok(())
            }
            b'*' if self.in_version_comment => {
                self.state = State::Star;
                Ok(())
            }
            other => self.emit(DumpToken::Symbol(other)),
        }
    }
}

/// Whether the client takes `byte` as white space after a `--`.
///
/// The server also starts a comment at a control byte, which is why the caller
/// refuses one: the two would part on what follows.
fn is_client_space(byte: u8) -> bool {
    matches!(byte, b' ' | b'\t' | b'\n' | b'\r' | 0x0b | 0x0c)
}

/// Whether `byte` can be part of a bare word.
///
/// `@` is one, so `@@session` and `@old_mode` are words; `.` is not, so a
/// qualified name is a word, a symbol and a word. Bytes above ASCII are, since
/// an unquoted name may hold them and none of them is a delimiter.
fn is_word_byte(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || matches!(byte, b'_' | b'$' | b'@') || byte >= 0x80
}
