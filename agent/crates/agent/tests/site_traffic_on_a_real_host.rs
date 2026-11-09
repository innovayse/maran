//! The traffic summary and the log prune against a real root-owned directory.
//!
//! The unit tests drive the decisions against an in-memory directory and the
//! descriptor discipline against a directory the test owns. What neither can
//! show is the production path: `/var/log/maran/sites/<account>` owned by root,
//! opened by `ProcessSiteLogHost` with `O_NOFOLLOW`, listed through
//! `/proc/self/fd`, and unlinked by name through the held descriptor. This file
//! runs that, in the polygon container, as root.
//!
//! UNOBSERVED HERE: SELinux enforcement on `var_log_t` (the polygon images run
//! no policy), and logrotate's own timing — the rotation below is a rename.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::{PermissionsExt as _, symlink};
use std::path::PathBuf;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::log_retention::LogRetention;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_ops::sites::{
    ProcessSiteLogHost, PruneLogsInput, SitesOpError, TrafficSummaryInput, prune_site_logs,
    summarise_site_traffic,
};

/// Account names no real account on the polygon uses, one per test: the tests
/// run in parallel and each owns its directory.
const ACCOUNT: &str = "trafficprobe";

/// The second test's account.
const OTHER_ACCOUNT: &str = "trafficother";

/// The probe site.
const DOMAIN: &str = "probe.example.com";

/// One request line of the `combined` format.
const LINE: &str = "203.0.113.9 - - [07/Nov/2026:12:00:00 +0000] \"GET /probe?x=1 HTTP/1.1\" 200 100 \"-\" \"probe\"\n";

/// A root-owned log directory that removes itself, even on a failing assertion.
struct LogDirectory(PathBuf);

impl LogDirectory {
    fn create(account: &str) -> Self {
        let path = AgentPaths::account_site_log_dir(&AccountName::parse(account).unwrap());
        std::fs::create_dir_all(&path).unwrap();
        std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o750)).unwrap();
        Self(path)
    }

    fn file(&self, name: &str) -> PathBuf {
        self.0.join(format!("{DOMAIN}.{name}"))
    }
}

impl Drop for LogDirectory {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

fn summary_input(
    account: &str,
    cursor: Option<maran_ops::sites::TrafficCursor>,
) -> TrafficSummaryInput {
    TrafficSummaryInput {
        account: AccountName::parse(account).unwrap(),
        domain: Domain::parse(DOMAIN).unwrap(),
        cursor,
    }
}

#[test]
#[ignore = "reads and unlinks in /var/log/maran/sites as root: polygon only"]
fn a_summary_resumes_from_its_cursor_and_a_prune_removes_only_archives_above_the_retention() {
    let directory = LogDirectory::create(ACCOUNT);
    std::fs::write(directory.file("access.log"), LINE.repeat(3)).unwrap();
    std::fs::write(directory.file("access.log.1"), LINE.repeat(2)).unwrap();
    std::fs::write(directory.file("access.log.2.gz"), b"archive").unwrap();
    std::fs::write(directory.file("access.log.3.gz"), b"archive").unwrap();
    std::fs::write(directory.file("error.log.2.gz"), b"archive").unwrap();
    let host = ProcessSiteLogHost::new();

    let first = summarise_site_traffic(&host, &summary_input(ACCOUNT, None)).unwrap();
    std::fs::OpenOptions::new()
        .append(true)
        .open(directory.file("access.log"))
        .and_then(|mut file| std::io::Write::write_all(&mut file, LINE.as_bytes()))
        .unwrap();
    let second =
        summarise_site_traffic(&host, &summary_input(ACCOUNT, first.cursor.clone())).unwrap();

    let total = |summary: &maran_ops::sites::TrafficSummary| -> u64 {
        summary.days.iter().map(|day| day.requests).sum()
    };
    assert_eq!(total(&first), 5, ".1 and the live file");
    assert_eq!(total(&second), 1, "only the appended line");
    assert!(!second.gap);
    assert_eq!(
        second.days[0].paths[0].path, "/probe",
        "the query string is cut"
    );

    let pruned = prune_site_logs(
        &host,
        &PruneLogsInput {
            account: AccountName::parse(ACCOUNT).unwrap(),
            domain: Domain::parse(DOMAIN).unwrap(),
            retain: LogRetention::parse(1).unwrap(),
        },
    )
    .unwrap();

    assert_eq!(pruned.removed_files, 3);
    assert!(directory.file("access.log").exists());
    assert!(directory.file("access.log.1").exists());
    assert!(!directory.file("access.log.2.gz").exists());
    assert!(!directory.file("error.log.2.gz").exists());
}

#[test]
#[ignore = "reads in /var/log/maran/sites as root: polygon only"]
fn a_symlink_planted_as_the_live_log_is_an_error_and_is_never_followed() {
    let directory = LogDirectory::create(OTHER_ACCOUNT);
    let elsewhere = tempfile::tempdir().unwrap();
    std::fs::write(elsewhere.path().join("secret"), LINE).unwrap();
    symlink(
        elsewhere.path().join("secret"),
        directory.file("access.log"),
    )
    .unwrap();

    let outcome = summarise_site_traffic(
        &ProcessSiteLogHost::new(),
        &summary_input(OTHER_ACCOUNT, None),
    );

    assert!(matches!(outcome, Err(SitesOpError::LogUnreadable { .. })));
}
