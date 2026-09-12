//! One SFTP session that stays OPEN while the test does something else to the
//! account behind it.
//!
//! Every other client in the SFTP suite is a one-shot `sftp -b` run: it
//! authenticates, executes a script and exits, so no assertion built on it can
//! say anything about a session that was ALREADY authenticated when the panel
//! changed the account's state. That is the one question this type exists for,
//! and it is the SFTP half of what
//! `fixtures/ftps_control_session.rs` answers for FTPS — the two protocols'
//! answers are only comparable if both are asked the same way, on a real
//! daemon, in a session that outlives the event.
//!
//! It drives the image's own `sftp` client in batch mode reading from a pipe
//! (`-b -`), which keeps ONE ssh connection and one SFTP subsystem channel open
//! for as long as that pipe is open. Commands are written into the pipe as the
//! test needs them, so the connection is live across whatever happens in
//! between.
//!
//! **How a reply is known to be complete.** The client prints no prompt this
//! side can wait on, so every command is followed by a `pwd`, and the reply is
//! read up to the `Remote working directory:` line `pwd` always prints. That
//! line is the terminator. **It is NOT a liveness statement, and an earlier
//! version of this paragraph said it was — measured false on a polygon run:** the
//! openssh client holds the remote working directory in its own state, so after
//! the daemon had closed the connection the client still printed
//! `Remote working directory: /`. A test that wants to know whether the daemon is
//! still serving this session must move BYTES through it; `pwd` only proves the
//! client is alive. Nothing here sleeps for a reply.
//!
//! **UNOBSERVED HERE: host key verification.** The client is told
//! `StrictHostKeyChecking=no` against a throwaway known-hosts file, exactly as
//! the one-shot client in `polygon_sshd.rs`. Whether a client would accept this
//! daemon's key is not what any of these tests are about.

use std::io::{BufRead as _, BufReader, Write as _};
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::mpsc::{Receiver, RecvTimeoutError, channel};
use std::thread;
use std::time::Duration;

use crate::polygon_sshd::{PolygonSshd, SSHD_ADDRESS, SSHD_PORT};

/// How long a single reply may take before the session reports it never came.
///
/// A bound rather than a wait: nothing here sleeps for it in the good case, and
/// a daemon that has stopped answering is a finding this type must be able to
/// report rather than hang on.
const REPLY_TIMEOUT: Duration = Duration::from_secs(20);

/// The line `pwd` always prints, and therefore the end of every reply.
const PWD_REPLY: &str = "Remote working directory:";

/// What a reply reads as when nothing arrived inside [`REPLY_TIMEOUT`].
pub const NO_REPLY: &str = "NO REPLY WITHIN THE TIMEOUT";

/// What a reply reads as when the client's streams closed instead.
pub const SESSION_CLOSED: &str = "THE SESSION CLOSED";

/// An authenticated SFTP session, held open for as long as the value lives.
///
/// Dropping it kills the client, which is what closes the session — so a test
/// that wants the session alive across an event must keep the value alive
/// across that event.
pub struct SftpControlSession {
    /// The `sftp` client process carrying the session.
    client: Child,
    /// Its standard input: the command channel.
    commands: ChildStdin,
    /// Lines the client printed, as reader threads saw them.
    replies: Receiver<String>,
}

impl SftpControlSession {
    /// Opens a session as `user` with `password` and returns it together with
    /// the client's first reply.
    ///
    /// The first reply is the answer to a `pwd`, so the caller asserts on the
    /// login having succeeded rather than being told that it did.
    ///
    /// # Panics
    ///
    /// Panics when the client cannot be started at all, or when its pipes
    /// cannot be taken — a session that was never opened has measured nothing,
    /// and a quiet failure here would read as a refusal by the daemon.
    pub fn login(user: &str, password: &str) -> (Self, String) {
        let askpass = PolygonSshd::askpass(password);

        let mut client = Command::new("sftp")
            .args([
                "-P",
                SSHD_PORT,
                "-o",
                "BatchMode=no",
                "-o",
                "StrictHostKeyChecking=no",
                "-o",
                "UserKnownHostsFile=/dev/null",
                "-o",
                "NumberOfPasswordPrompts=1",
                "-b",
                "-",
                &format!("{user}@{SSHD_ADDRESS}"),
            ])
            .env("SSH_ASKPASS", &askpass)
            .env("SSH_ASKPASS_REQUIRE", "force")
            .env("DISPLAY", ":0")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .unwrap_or_else(|error| panic!("the polygon image installs an sftp client: {error}"));

        let commands = client
            .stdin
            .take()
            .unwrap_or_else(|| panic!("the client was spawned with a piped standard input"));
        let (sender, replies) = channel();

        // Both streams, because the client splits its output across them: the
        // echoed command and a successful answer go to standard output, and a
        // refusal goes to standard error. A session type that read only one of
        // them would report a refusal as silence.
        for stream in [
            client
                .stdout
                .take()
                .map(|handle| Box::new(handle) as Box<dyn std::io::Read + Send>),
            client
                .stderr
                .take()
                .map(|handle| Box::new(handle) as Box<dyn std::io::Read + Send>),
        ]
        .into_iter()
        .flatten()
        {
            let sender = sender.clone();
            thread::spawn(move || {
                for line in BufReader::new(stream).lines().map_while(Result::ok) {
                    if sender.send(line).is_err() {
                        return;
                    }
                }
            });
        }
        drop(sender);

        let mut session = Self {
            client,
            commands,
            replies,
        };
        let greeting = session.probe();

        (session, greeting)
    }

    /// Sends one command that the session may no longer be alive to carry, and
    /// returns what the client printed — or [`SESSION_CLOSED`] when the command
    /// could not even be written.
    ///
    /// A failed write is not the daemon refusing — it is the harness's own client
    /// having gone away. It is reported as [`SESSION_CLOSED`] here because this is
    /// the one question where the session's DEATH is the measurement: a test that
    /// has just suspended the account behind it, where `stdin` is a pipe to a
    /// process the cull killed and the write is precisely what fails. What the
    /// test then concludes rests on whether BYTES landed, never on reading the
    /// client's chatter — [`Self::probe`] records why the client's own output is
    /// not a liveness statement. The strict counterpart this type used to carry,
    /// which panicked on a failed write, was removed when its last caller went: an
    /// uncalled method is decoration, and its absence is why the tolerance here
    /// cannot be reached by a caller that wanted strictness.
    pub fn probe_command(&mut self, command: &str) -> String {
        match write!(self.commands, "{command}\npwd\n").and_then(|()| self.commands.flush()) {
            Ok(()) => self.reply(),
            Err(_) => SESSION_CLOSED.to_owned(),
        }
    }

    /// Sends a bare `pwd` and returns its reply.
    ///
    /// **`pwd` is NOT a liveness test, and the module documentation used to claim
    /// it was.** Measured on a polygon run: after the account's session had been
    /// killed, the client printed `Connection to 127.0.0.1 closed by remote host.`
    /// and then answered `pwd` with `Remote working directory: /` anyway — the
    /// openssh client keeps the remote working directory in its OWN state and
    /// answers `pwd` from there with no round trip. So this method proves the
    /// client is running, and nothing about the daemon. It is kept because it is
    /// the reply TERMINATOR every other command depends on, and because the
    /// login greeting is genuinely a `pwd` the client could not have answered
    /// before the connection came up. A test asking "is this session still
    /// served" must move BYTES instead.
    ///
    /// Not [`Self::probe_command`]: that one APPENDS a `pwd` as its terminator, so
    /// asking it for a `pwd` would send two and leave the second reply sitting
    /// in the pipe — where the NEXT command would read it as its own and return
    /// before that command had run at all. Measured, on the first polygon run:
    /// the transfer's reply was the login's leftover `Remote working directory:`
    /// and the file it was supposed to fetch landed nowhere.
    ///
    /// # Panics
    ///
    /// Panics when the probe cannot be written to the client at all.
    pub fn probe(&mut self) -> String {
        writeln!(self.commands, "pwd")
            .and_then(|()| self.commands.flush())
            .unwrap_or_else(|error| {
                panic!("the opening probe could not be written to the session ({error})")
            });

        self.reply()
    }

    /// Reads lines until the appended `pwd` has answered.
    fn reply(&self) -> String {
        let mut collected = String::new();
        loop {
            match self.replies.recv_timeout(REPLY_TIMEOUT) {
                Ok(line) => {
                    let finished = line.contains(PWD_REPLY);
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
}

impl Drop for SftpControlSession {
    /// Closes the session by killing the client it is carried on.
    fn drop(&mut self) {
        let _ = self.client.kill();
        let _ = self.client.wait();
    }
}
