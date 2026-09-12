//! One FTPS control session that stays OPEN while the test does something else
//! to the account behind it.
//!
//! Every other client in the FTPS suite is `curl`, which authenticates, does one
//! thing and hangs up — so no `curl` assertion can say anything about a session
//! that is already authenticated when the panel changes the account's state. The
//! question this type exists for is precisely that one: what a daemon does to a
//! session it has already let in.
//!
//! It drives `openssl s_client -starttls ftp`, which performs the RFC 4217
//! upgrade (`AUTH TLS`) and then hands the decrypted control channel over as a
//! pipe. The session's own commands and the server's replies are therefore the
//! real protocol, over the real TLS the daemon was configured for — not a
//! plaintext side door, which this daemon refuses anyway.
//!
//! **UNOBSERVED HERE: certificate verification.** `s_client` is told nothing
//! about a trust store and the suite's material is self-signed, exactly as the
//! `curl -k` in the suite beside it. Whether a client would accept the chain is
//! not what any of these tests are about.

use std::io::{BufRead as _, BufReader, Read as _, Write as _};
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::mpsc::{Receiver, RecvTimeoutError, channel};
use std::thread;
use std::time::Duration;

use crate::retrieved_file::RetrievedFile;

/// The TLS client both polygon families install, and the only one here.
const OPENSSL_BINARY: &str = "/usr/bin/openssl";

/// The address every session in this suite connects to.
const CONTROL_ADDRESS: &str = "127.0.0.1:21";

/// How long a single reply may take before the session reports it never came.
///
/// A bound rather than a wait: nothing here sleeps for it in the good case, and
/// a daemon that has stopped answering is a finding this type must be able to
/// report rather than hang on.
const REPLY_TIMEOUT: Duration = Duration::from_secs(20);

/// An authenticated FTPS control session, held open for as long as the value
/// lives.
///
/// Dropping it kills the client, which is what closes the session — so a test
/// that wants the session alive across an event must keep the value alive across
/// that event.
pub struct FtpsControlSession {
    /// The TLS client process carrying the control channel.
    client: Child,
    /// Its standard input: the command channel.
    commands: ChildStdin,
    /// Lines the server sent, as a reader thread saw them.
    replies: Receiver<String>,
}

impl FtpsControlSession {
    /// Opens a session and logs `login` in with `password`.
    ///
    /// Returns the session and the server's reply to `PASS`, so the caller
    /// asserts on the login rather than being told about it.
    ///
    /// # Panics
    ///
    /// Panics when `openssl` cannot be started at all — a session that could not
    /// be opened has measured nothing, and a quiet `None` here would read as a
    /// refusal by the daemon.
    pub fn login(login: &str, password: &str) -> (Self, String) {
        let mut client = Command::new(OPENSSL_BINARY)
            .args([
                "s_client",
                "-quiet",
                "-connect",
                CONTROL_ADDRESS,
                "-starttls",
                "ftp",
            ])
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .unwrap_or_else(|error| {
                panic!(
                    "{OPENSSL_BINARY} must be runnable: it is the only client here that can hold \
                     an FTPS session open, and without it this test measures nothing ({error})"
                )
            });

        let stdout = client
            .stdout
            .take()
            .expect("standard output was piped one statement above");
        let (sender, replies) = channel();
        thread::spawn(move || {
            for line in BufReader::new(stdout).lines().map_while(Result::ok) {
                if sender.send(line).is_err() {
                    break;
                }
            }
        });

        let commands = client
            .stdin
            .take()
            .expect("standard input was piped one statement above");

        let mut session = Self {
            client,
            commands,
            replies,
        };

        let user = session.command(&format!("USER {login}"));
        assert!(
            user.starts_with("331") || user.starts_with("530"),
            "the daemon must answer USER with a password prompt or a refusal, got {user:?}"
        );
        let password_reply = session.command(&format!("PASS {password}"));

        (session, password_reply)
    }

    /// Sends one command and returns the server's complete reply.
    ///
    /// A reply that never arrives is returned as the sentence below rather than
    /// hanging the suite, so "the daemon stopped answering this session" is an
    /// outcome a test can assert on.
    ///
    /// # Panics
    ///
    /// Panics when the command cannot be written to the client at all.
    pub fn command(&mut self, command: &str) -> String {
        write!(self.commands, "{command}\r\n")
            .and_then(|()| self.commands.flush())
            .unwrap_or_else(|error| {
                panic!(
                    "the command {command:?} could not be written to the open session ({error}); \
                     the client has gone away, which is a different outcome from the daemon \
                     refusing, and this type will not report one as the other"
                )
            });

        self.reply()
    }

    /// Sends one command that the session may no longer be alive to carry, and
    /// returns either the server's reply or [`SESSION_CLOSED`].
    ///
    /// This exists because [`command`](Self::command) deliberately panics when
    /// the write fails, and for every other caller that is right: a broken pipe
    /// there is the harness losing its client, which must never be reported as
    /// the daemon refusing. For ONE question it is the opposite. When a test has
    /// just suspended the account this session belongs to, the session's process
    /// has been signalled, and a **broken pipe on the write IS the measurement** —
    /// the client's `stdin` is a pipe to a process the cull killed, so the write
    /// fails before any reply could be read.
    ///
    /// So the two outcomes are separated by which method the test called rather
    /// than by guessing from the text: `command` for a session that must still be
    /// there, `probe` for one whose death is the thing being measured. A test
    /// asserting on the ABSENCE of a reply must use this, or it measures the
    /// fixture's panic instead of the product's behaviour — which is exactly what
    /// happened before this method existed.
    pub fn probe(&mut self, command: &str) -> String {
        match write!(self.commands, "{command}\r\n").and_then(|()| self.commands.flush()) {
            Ok(()) => self.reply(),
            Err(_) => SESSION_CLOSED.to_owned(),
        }
    }

    /// Reads lines until the server's reply is complete.
    ///
    /// FTP's continuation rule: a line whose fourth character is a `-` is
    /// followed by more, and the reply ends at the first line whose fourth
    /// character is a space.
    fn reply(&self) -> String {
        let mut collected = String::new();
        loop {
            match self.replies.recv_timeout(REPLY_TIMEOUT) {
                Ok(line) => {
                    let finished = is_final_reply_line(&line);
                    collected.push_str(&line);
                    collected.push('\n');
                    if finished {
                        return collected;
                    }
                }
                Err(RecvTimeoutError::Timeout) => {
                    collected.push_str(NO_REPLY);
                    return collected;
                }
                Err(RecvTimeoutError::Disconnected) => {
                    collected.push_str(SESSION_CLOSED);
                    return collected;
                }
            }
        }
    }

    /// Retrieves `path` through this session and returns what the data channel
    /// carried, together with the control replies that framed it.
    ///
    /// The data connection is a second TLS connection, because the rendered
    /// configuration sets `force_local_data_ssl=YES`; it is a fresh session
    /// rather than a resumption of the control one, which the rendered
    /// `require_ssl_reuse=NO` is what permits.
    ///
    /// # Panics
    ///
    /// Panics when the passive reply cannot be understood, or when the data
    /// client cannot be started.
    pub fn retrieve(&mut self, path: &str) -> RetrievedFile {
        let binary = self.command("TYPE I");
        let protection_size = self.command("PBSZ 0");
        let protection = self.command("PROT P");
        let passive = self.command("PASV");
        let port = passive_port(&passive).unwrap_or_else(|| {
            panic!(
                "the passive reply must name a port, or nothing can be transferred through this \
                 session. TYPE said {binary:?}, PBSZ {protection_size:?}, PROT {protection:?}, \
                 PASV {passive:?}"
            )
        });

        let mut data = Command::new(OPENSSL_BINARY)
            .args([
                "s_client",
                "-quiet",
                "-connect",
                &format!("127.0.0.1:{port}"),
            ])
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .unwrap_or_else(|error| panic!("the data client must be startable: {error}"));

        let began = self.command(&format!("RETR {path}"));

        let mut bytes = String::new();
        if let Some(mut stream) = data.stdout.take() {
            // The server closes the data connection at the end of the transfer,
            // which is the end of this read.
            let _ = stream.read_to_string(&mut bytes);
        }
        let _ = data.wait();
        let finished = self.reply();

        RetrievedFile {
            began,
            bytes,
            finished,
        }
    }
}

impl Drop for FtpsControlSession {
    /// Closes the session, whether the test passed or panicked.
    fn drop(&mut self) {
        let _ = self.client.kill();
        let _ = self.client.wait();
    }
}

/// What [`FtpsControlSession::command`] returns when no reply arrived in time.
pub const NO_REPLY: &str = "NO REPLY WITHIN THE TIMEOUT";

/// What it returns when the client's side of the session ended.
pub const SESSION_CLOSED: &str = "THE SESSION CLOSED";

/// Whether `line` is the last line of an FTP reply.
fn is_final_reply_line(line: &str) -> bool {
    let bytes = line.as_bytes();
    bytes.len() >= 4
        && bytes[0].is_ascii_digit()
        && bytes[1].is_ascii_digit()
        && bytes[2].is_ascii_digit()
        && bytes[3] == b' '
}

/// The port a `227 Entering Passive Mode (h1,h2,h3,h4,p1,p2)` reply names.
fn passive_port(reply: &str) -> Option<u32> {
    let inside = reply.split_once('(')?.1.split_once(')')?.0;
    let numbers: Vec<u32> = inside
        .split(',')
        .filter_map(|part| part.trim().parse().ok())
        .collect();
    match numbers.as_slice() {
        [_, _, _, _, high, low] => Some(high * 256 + low),
        _ => None,
    }
}
