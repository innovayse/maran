//! The in-memory [`SiteHost`] the site tests decide against, and the inputs
//! they decide about.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host writes into `/etc/maran/nginx/sites` and reloads a live nginx:
//! what a unit test can pin is which content an operation chooses to write and
//! when it chooses to write none, not what nginx then does with it. The write
//! protocol has its own tests in `safe_write`, and the privilege drop in
//! `agent-core::privs`.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::php::fake_php_host::FakePhpHost;
use crate::safe_write::model::{Reload, Validator};
use crate::sites::log_sink::LogSink;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::log_tail_request::LogTailRequest;
use crate::sites::model::site_identity::SiteIdentity;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::tail_end::TailEnd;
use crate::sites::{SiteHost, SiteMaintenanceHost, SitesOpError};

/// Where this fake's homes really are, once resolved.
///
/// Different from `/home` on purpose; see [`FakeSiteHost::resolve_in_account_home`].
pub(crate) const CANONICAL_HOME_ROOT: &str = "/srv/homes";

/// A [`SiteHost`] that keeps the vhost directory in memory.
///
/// The real one writes into `/etc/maran/nginx/sites` and reloads a live nginx;
/// this one records what it was asked to do so a test can assert on the
/// decision instead of on the machine.
pub(crate) struct FakeSiteHost {
    /// The vhost directory: path to content.
    files: Mutex<BTreeMap<PathBuf, String>>,
    /// Directories the host was asked to create as an account.
    created: Mutex<Vec<PathBuf>>,
    /// `nginx -t`'s answer, and what it says when it refuses.
    validation: Mutex<(i32, String)>,
    /// How many times a write or a removal actually reached the protocol —
    /// the number a no-op test pins at zero.
    writes: Mutex<usize>,
    /// The lines a tail replays.
    log_lines: Mutex<Vec<String>>,
    /// What the last tail was asked for — the decision a test pins.
    tailed: Mutex<Option<LogTailRequest>>,
}

impl FakeSiteHost {
    /// A host whose validator accepts everything.
    pub(crate) fn passing() -> Self {
        Self {
            files: Mutex::new(BTreeMap::new()),
            created: Mutex::new(Vec::new()),
            validation: Mutex::new((0, String::new())),
            writes: Mutex::new(0),
            log_lines: Mutex::new(Vec::new()),
            tailed: Mutex::new(None),
        }
    }

    /// Loads the lines a tail will replay.
    pub(crate) fn with_log(&self, lines: &[&str]) {
        *self.log_lines.lock().unwrap() = lines.iter().map(|line| (*line).to_owned()).collect();
    }

    /// What the last tail was asked for.
    pub(crate) fn tailed(&self) -> Option<LogTailRequest> {
        self.tailed.lock().unwrap().clone()
    }

    /// Makes `nginx -t` refuse every configuration, with `stderr` as the
    /// reason an operator would read in the log.
    pub(crate) fn reject_validation(&self, stderr: &str) {
        *self.validation.lock().unwrap() = (1, stderr.to_owned());
    }

    /// The content of a vhost, if the host holds one.
    pub(crate) fn config(&self, path: &Path) -> Option<String> {
        self.files.lock().unwrap().get(path).cloned()
    }

    /// How many writes or removals reached the protocol.
    pub(crate) fn writes(&self) -> usize {
        *self.writes.lock().unwrap()
    }

    /// The directories the host was asked to create as the account.
    pub(crate) fn created(&self) -> Vec<PathBuf> {
        self.created.lock().unwrap().clone()
    }
}

impl SiteHost for FakeSiteHost {
    fn read_config(&self, path: &Path) -> Result<Option<String>, SitesOpError> {
        Ok(self.config(path))
    }

    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        *self.writes.lock().unwrap() += 1;

        let (status, stderr) = self.validation.lock().unwrap().clone();
        if status != 0 {
            // The real protocol restores the previous content before
            // returning, so the fake leaves the map untouched.
            return Err(SitesOpError::NginxValidation { stderr });
        }

        self.files
            .lock()
            .unwrap()
            .insert(target.to_path_buf(), contents.to_owned());
        Ok(())
    }

    fn remove_config(
        &self,
        target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        *self.writes.lock().unwrap() += 1;
        self.files.lock().unwrap().remove(target);
        Ok(())
    }

    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        directories: &[&Path],
    ) -> Result<(), SitesOpError> {
        let mut created = self.created.lock().unwrap();
        for directory in directories {
            created.push(directory.to_path_buf());
        }
        Ok(())
    }

    /// Answers with a canonical path that is deliberately NOT the named one.
    ///
    /// `/home` symlinked elsewhere, or a bind-mounted home layout, are both
    /// ordinary — and on such a host `resolve_in_home` returns a path that
    /// differs textually from `/home/<account>/sites/<domain>`. An identity
    /// answer here would make every operation look as if it rendered the same
    /// text whether it resolved or not, which is exactly the bug that hid: one
    /// operation rendering the resolved root and another the named one, so
    /// their comparison never matched and every retry reloaded nginx.
    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, SitesOpError> {
        Ok(PathBuf::from(CANONICAL_HOME_ROOT)
            .join(account.as_str())
            .join(relative))
    }
}

impl SiteMaintenanceHost for FakeSiteHost {
    /// Answers the batch reload the same way the write path's validator does,
    /// so one `reject_validation` call governs both.
    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        *self.writes.lock().unwrap() += 1;

        let (status, stderr) = self.validation.lock().unwrap().clone();
        if status == 0 {
            Ok(())
        } else {
            Err(SitesOpError::NginxValidation { stderr })
        }
    }

    /// Replays the lines the test loaded, then reports the client gone.
    ///
    /// What a fake can pin here is what `tail_site_log` DECIDES: which
    /// directory it resolved, which file name it derived, and that it clamped
    /// the history. What it deliberately cannot pin is the reading itself —
    /// an oversized file, a FIFO, a hardlink and a swapped directory are
    /// properties of real inodes, so those belong to tests over
    /// `sites::log_tail::follow` against a temporary directory.
    fn tail_log(
        &self,
        request: &LogTailRequest,
        sink: &mut dyn LogSink,
    ) -> Result<TailEnd, SitesOpError> {
        *self.tailed.lock().unwrap() = Some(request.clone());

        for line in self.log_lines.lock().unwrap().iter() {
            if let Err(end) = sink.line(line, true) {
                return Ok(end);
            }
        }

        Ok(TailEnd::ClientClosed)
    }
}

/// The adapter every test in this file runs against. Which family is
/// immaterial to the decisions being tested; that it comes from the adapter
/// rather than a literal is the point.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// A PHP site for `acme`, with no certificate.
pub(crate) fn php_input() -> CreateSiteInput {
    CreateSiteInput {
        account: AccountName::parse("acme").unwrap(),
        domain: Domain::parse("example.com").unwrap(),
        aliases: vec![Domain::parse("www.example.com").unwrap()],
        kind: SiteKind::Php {
            version: PhpVersion::parse("8.3").unwrap(),
        },
        certificate: None,
    }
}

/// The same site as [`php_input`], named only.
///
/// The pair is the point: an operation that takes a [`SiteIdentity`] cannot be
/// handed the kind, the aliases or the certificate, so a test cannot
/// accidentally assert that it read one.
pub(crate) fn php_identity() -> SiteIdentity {
    SiteIdentity {
        account: AccountName::parse("acme").unwrap(),
        domain: Domain::parse("example.com").unwrap(),
    }
}

/// The plan worker budget the site tests create against.
///
/// Immaterial to every decision they make about a vhost; it exists because a
/// pool has to be written for a PHP site to work at all, and `pm.max_children`
/// is part of what a pool is.
pub(crate) const TEST_WORKERS: u32 = 10;

/// Creates a site against a PHP host that has the fixtures' versions installed.
///
/// Every site test that only wants a site to exist goes through this, so the
/// pool half of creation is supplied once rather than in thirty places. A test
/// that is ABOUT the pool calls [`crate::sites::create_site`] directly with its
/// own [`FakePhpHost`], because it needs to read that host back.
pub(crate) fn create_test_site<H: SiteHost>(
    host: &H,
    input: &CreateSiteInput,
) -> Result<crate::sites::model::created_site::CreatedSite, SitesOpError> {
    crate::sites::create_site(
        host,
        &FakePhpHost::with_installed(&["8.3", "8.4"]),
        distro(),
        input,
        TEST_WORKERS,
        &[],
    )
}
