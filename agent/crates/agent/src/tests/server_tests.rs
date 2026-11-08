//! Tests for the server assembly: which startup passes run, and what it does not set.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints"): a failed unwrap here is the test reporting, and
//! the workspace lints are denied rather than warned, so the exemption is
//! declared file by file as every other test file in this tree declares it.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

/// The source of the file these tests belong to, read so that
/// [`startup_reconcilers`] can name what `serve` actually calls rather than
/// what a reader remembers it calling.
const SERVER_SOURCE: &str = include_str!("../server.rs");

/// Every call inside `serve` to a function whose name carries `reconcile_` or
/// `recover_`, in source order.
///
/// Discovered from the source rather than listed, because a list is blind to
/// exactly the addition this is watching for. Comment lines are skipped so the
/// paragraphs in `serve` that DESCRIBE the reconciliation are not mistaken for
/// it.
fn startup_reconcilers() -> Vec<String> {
    let after_signature = SERVER_SOURCE
        .split_once("pub async fn serve(")
        .expect("serve is declared in this file")
        .1;
    let body = after_signature
        .split_once("\n}\n")
        .expect("serve's body ends at a closing brace in the first column")
        .0;

    let mut found = Vec::new();
    for line in body.lines() {
        let trimmed = line.trim_start();
        if trimmed.starts_with("//") {
            continue;
        }
        let bytes = line.as_bytes();
        let mut index = 0;
        while index < bytes.len() {
            let rest = &line[index..];
            let Some(offset) = rest.find("reconcile_").or_else(|| rest.find("recover_")) else {
                break;
            };
            let start = index + offset;
            let end = start
                + line[start..]
                    .find(|c: char| !c.is_alphanumeric() && c != '_')
                    .unwrap_or(line.len() - start);
            if line[end..].starts_with('(') {
                found.push(line[start..end].to_owned());
            }
            index = end;
        }
    }
    found
}

/// The startup pass covers interrupted restores, the web tree, the php-fpm
/// pool trees, the OPcache drop-ins (issue #157), the SFTP key directory and
/// the sites' credential files (issue #151), the DNS zone files (issue #178) and
/// the temporary database users of an interrupted import (issue #127), and
/// nothing else. The first three
/// are the still-OPEN part of the config-write midpoint written down as a check
/// instead of as a sentence; the drop-ins are the isolation settings of a cache
/// every tenant of a version shares; the key directory and the credential files
/// are not config trees but leftovers (issues #338, #151), and they are listed
/// so that they, too, cannot be removed unnoticed.
///
/// This test is meant to go RED the day the rest of the gap is closed. Three
/// trees remain unobserved: the SFTP jail mount unit, the FTPS jail mount unit
/// and `vsftpd.conf`, each of which passes a MUTATING command as its
/// `Validator` (`systemctl daemon-reload`, and `systemctl restart` for FTPS), so
/// re-running it at a start is an action and not an observation. When one of the
/// three gains a startup pass, this list grows, this test fails, and whoever
/// closed it must also update the blind-spot list in `reconcile_web_tree`'s doc
/// comment and in its own log line — which is the point: the note must not
/// outlive the gap.
///
/// The list GREW rather than loosened when the php-fpm pool pass landed: the
/// pass's own name is now asserted, in source order, so removing it is as red as
/// adding a fourth reconciler nobody wrote down. Loosening the assertion to
/// "contains" would have made this test agree with a tree that had stopped
/// observing the pools.
///
/// It cannot go silently blind: a scan that stopped finding anything returns
/// an empty list and fails here, rather than agreeing that nothing was added.
#[test]
fn the_only_startup_reconciliations_are_the_ones_serve_names() {
    assert_eq!(
        startup_reconcilers(),
        vec![
            "recover_restores".to_owned(),
            "reconcile_web_tree".to_owned(),
            "reconcile_php_pool_trees".to_owned(),
            "reconcile_opcache_drop_ins".to_owned(),
            "reconcile_sftp_key_files".to_owned(),
            "reconcile_site_auth_files".to_owned(),
            "reconcile_dns_zones".to_owned(),
            "reconcile_import_users".to_owned(),
        ],
        "a startup reconciliation was added or removed; update reconcile_web_tree's \
         stated blind spots and its log line, then update this list"
    );
}

/// The code lines of `server.rs`, without the comments that describe them.
fn server_code_lines() -> Vec<&'static str> {
    SERVER_SOURCE
        .lines()
        .map(str::trim_start)
        .filter(|line| !line.starts_with("//"))
        .collect()
}

/// A server-side request timeout drops the handler future and frees nothing: the
/// blocking work and the lock it holds keep running, and the caller is told a
/// write failed that then succeeds. A child that runs too long is bounded by its
/// call class instead (issue #90), so the server must not set one.
#[test]
fn the_server_sets_no_request_timeout_and_no_concurrency_limit() {
    let lines = server_code_lines();

    // Positive control: the scan reads the code that builds the server.
    assert!(lines.iter().any(|line| line.contains("Server::builder")));
    assert!(!lines.iter().any(|line| line.contains(".timeout(")));
    assert!(
        !lines
            .iter()
            .any(|line| line.contains("concurrency_limit_per_connection"))
    );
}
