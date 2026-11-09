//! The in-memory [`ArchiveHost`] the archive operation tests drive.
//!
//! The real host forks, drops to a hosting account and reads or writes a real
//! customer's home, none of which a unit test may do. What a unit test pins with
//! this fake is the SHAPE of an operation: that the floor is asked first and
//! that the host is called once, with what the operation was given. The
//! hardening is tested for real against a temporary directory in
//! `extract_in_home_tests` and `create_in_home_tests`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::Mutex;

use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::create_archive_input::CreateArchiveInput;
use crate::files::model::extract_archive_input::ExtractArchiveInput;
use crate::files::{ArchiveHost, FilesOpError};

/// One thing the host was asked to do.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum ArchiveCall {
    /// The space on a filesystem was measured.
    Measure,
    /// An extraction of this archive, by account and path.
    Extract { account: String, archive: String },
    /// A creation of this archive, by account and path.
    Create {
        account: String,
        destination: String,
    },
}

/// A host that records what it is asked and answers what it was told to.
pub(crate) struct FakeArchiveHost {
    /// Every call, in order.
    calls: Mutex<Vec<ArchiveCall>>,
    /// What `available_bytes` answers.
    available: u64,
    /// What both operations answer; a function because the error type is not `Clone`.
    answer: Box<dyn Fn() -> Result<ArchiveReport, FilesOpError> + Send + Sync>,
}

impl FakeArchiveHost {
    /// A roomy host whose operations succeed with `report`.
    pub(crate) fn answering(report: ArchiveReport) -> Self {
        Self {
            calls: Mutex::new(Vec::new()),
            available: u64::MAX,
            answer: Box::new(move || Ok(report)),
        }
    }

    /// A roomy host whose operations fail with `error`.
    pub(crate) fn refusing(error: impl Fn() -> FilesOpError + Send + Sync + 'static) -> Self {
        Self {
            calls: Mutex::new(Vec::new()),
            available: u64::MAX,
            answer: Box::new(move || Err(error())),
        }
    }

    /// The same host on a filesystem with exactly `available` bytes left.
    pub(crate) fn with_available(mut self, available: u64) -> Self {
        self.available = available;
        self
    }

    /// The calls made so far.
    pub(crate) fn calls(&self) -> Vec<ArchiveCall> {
        self.calls.lock().unwrap().clone()
    }
}

impl ArchiveHost for FakeArchiveHost {
    fn extract_as_account(
        &self,
        input: &ExtractArchiveInput,
    ) -> Result<ArchiveReport, FilesOpError> {
        self.calls.lock().unwrap().push(ArchiveCall::Extract {
            account: input.account.as_str().to_owned(),
            archive: input.archive.components().join("/"),
        });
        (self.answer)()
    }

    fn create_as_account(&self, input: &CreateArchiveInput) -> Result<ArchiveReport, FilesOpError> {
        self.calls.lock().unwrap().push(ArchiveCall::Create {
            account: input.account().as_str().to_owned(),
            destination: input.destination().components().join("/"),
        });
        (self.answer)()
    }

    fn available_bytes(&self, _directory: &Path) -> Result<u64, std::io::Error> {
        self.calls.lock().unwrap().push(ArchiveCall::Measure);
        Ok(self.available)
    }
}
