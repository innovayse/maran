//! Test support: captures what the audit target writes, so a test can read the journal
//! line back the way an operator would.
//!
//! The subscriber is installed for the current thread only, so tests running in
//! parallel never see each other's lines. Hold the `LineCapture` for the whole test:
//! dropping it uninstalls the subscriber.

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
pub(crate) struct LineCapture {
    buffer: SharedBuffer,
    _guard: DefaultGuard,
}

impl LineCapture {
    /// Installs a plain-text subscriber for this thread.
    pub(crate) fn start() -> Self {
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

    /// Everything written so far, one entry per physical line.
    pub(crate) fn raw_lines(&self) -> Vec<String> {
        let bytes = self.buffer.0.lock().unwrap().clone();
        String::from_utf8(bytes)
            .unwrap()
            .lines()
            .map(str::to_owned)
            .collect()
    }

    /// The message of every event written on the audit target, in order.
    pub(crate) fn audit_lines(&self) -> Vec<String> {
        self.raw_lines()
            .iter()
            .filter_map(|line| line.split_once("maran_audit: "))
            .map(|(_, message)| message.to_owned())
            .collect()
    }
}
