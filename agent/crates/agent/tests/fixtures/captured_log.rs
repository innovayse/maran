//! Everything the tracing subscriber of a test wrote, kept in memory.
//!
//! The subscriber is installed for the CURRENT THREAD only (`set_default`), so tests
//! running in parallel never see each other's output. The tests that use it run on the
//! default single-threaded test runtime, which is what puts the in-process server's
//! tasks on the same thread. Hold the value for the whole test: dropping it uninstalls
//! the subscriber.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io;
use std::sync::{Arc, Mutex};

use tracing::subscriber::DefaultGuard;
use tracing_subscriber::fmt::MakeWriter;

/// A writer every event is appended to.
#[derive(Clone, Default)]
struct SharedBuffer(Arc<Mutex<Vec<u8>>>);

impl io::Write for SharedBuffer {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        self.0.lock().unwrap().extend_from_slice(bytes);
        Ok(bytes.len())
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

impl<'a> MakeWriter<'a> for SharedBuffer {
    type Writer = SharedBuffer;

    fn make_writer(&'a self) -> Self::Writer {
        self.clone()
    }
}

/// The installed subscriber and the buffer it writes to.
pub struct CapturedLog {
    /// Where every formatted event lands.
    buffer: SharedBuffer,
    /// Uninstalls the subscriber when dropped.
    _guard: DefaultGuard,
}

impl CapturedLog {
    /// Installs a plain-text (no ANSI) subscriber for this thread.
    pub fn start() -> Self {
        let buffer = SharedBuffer::default();
        let subscriber = tracing_subscriber::fmt()
            .with_writer(buffer.clone())
            .with_ansi(false)
            .without_time()
            .with_target(true)
            .finish();

        Self {
            buffer,
            _guard: tracing::subscriber::set_default(subscriber),
        }
    }

    /// Every byte written so far.
    pub fn bytes(&self) -> Vec<u8> {
        self.buffer.0.lock().unwrap().clone()
    }

    /// Every physical line written so far.
    pub fn lines(&self) -> Vec<String> {
        String::from_utf8(self.bytes())
            .unwrap()
            .lines()
            .map(str::to_owned)
            .collect()
    }

    /// The lines that are audit lines: the ones carrying `event=rpc`.
    pub fn audit_lines(&self) -> Vec<String> {
        self.lines()
            .into_iter()
            .filter(|line| line.contains("event=rpc"))
            .collect()
    }
}
