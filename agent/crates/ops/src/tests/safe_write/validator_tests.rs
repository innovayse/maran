//! The census of every [`Validator`] this crate builds, and which of them are
//! checks rather than actions.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints").
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};

/// Every `Validator { .. }` this crate builds, as
/// `(path relative to `src/`, the `program:` expression verbatim, the `class:`
/// expression verbatim)`.
///
/// DISCOVERED from the source rather than listed, because a list is blind to
/// exactly the addition this is watching for: a new area passing a mutating
/// command as its validator would simply not appear in a hand-written table.
/// The `program` expression is the classifier and it is a real one rather than a
/// convention — a validator whose program is the service manager is spawning
/// `systemctl`, which is an action on the running system, while one whose program
/// is `nginx_binary()` or `php_fpm_binary(..)` is asking a question about bytes.
///
/// The test module's own file is excluded, and so is everything under `tests/`:
/// a fake's fixture building a `Validator` is not a production call site.
fn validator_call_sites() -> Vec<(String, String, String)> {
    let source_root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("src");
    let mut files = Vec::new();
    collect_rust_files(&source_root, &mut files);
    files.sort();

    let mut found = Vec::new();
    for file in files {
        let relative = file
            .strip_prefix(&source_root)
            .expect("every collected file is under src/")
            .to_string_lossy()
            .replace('\\', "/");

        // `tests/` holds the fakes and the fixtures. A `Validator` built there is
        // an input to a test, not a decision the shipped agent makes.
        if relative.starts_with("tests/") {
            continue;
        }

        let contents = std::fs::read_to_string(&file).expect("a source file this crate compiles");
        for (program, class) in sites_in(&contents) {
            found.push((relative.clone(), program, class));
        }
    }

    found
}

/// One expected row of the census, as the scan reports it.
fn row(file: &str, program: &str, class: &str) -> (String, String, String) {
    (file.to_owned(), program.to_owned(), class.to_owned())
}

/// Every `.rs` file under `directory`, recursively.
fn collect_rust_files(directory: &Path, into: &mut Vec<PathBuf>) {
    let entries = std::fs::read_dir(directory).expect("the crate's src/ directory is readable");
    for entry in entries {
        let path = entry.expect("a readable directory entry").path();
        if path.is_dir() {
            collect_rust_files(&path, into);
        } else if path.extension().is_some_and(|extension| extension == "rs") {
            into.push(path);
        }
    }
}

/// The `program:` and `class:` expressions of every `Validator { .. }` literal in
/// `contents`.
///
/// Reads forward from each `Validator {` to the first line whose trimmed start is
/// `program:` and then to the first one that starts with `class:`, so the comment
/// paragraphs several of these carry between the brace and the fields are skipped
/// rather than mistaken for them. A literal with no `class:` line cannot compile,
/// so a missing one is reported as an empty expression and fails the census loudly.
fn sites_in(contents: &str) -> Vec<(String, String)> {
    let mut sites = Vec::new();
    let mut rest = contents;

    while let Some(offset) = rest.find("Validator {") {
        rest = &rest[offset + "Validator {".len()..];
        let mut program = None;
        let mut class = None;
        for line in rest.lines() {
            let trimmed = line.trim();
            if program.is_none() {
                program = trimmed
                    .strip_prefix("program:")
                    .map(|expression| expression.trim().trim_end_matches(',').to_owned());
            } else if let Some(expression) = trimmed.strip_prefix("class:") {
                class = Some(expression.trim().trim_end_matches(',').to_owned());
                break;
            }
        }
        if let Some(program) = program {
            sites.push((program, class.unwrap_or_default()));
        }
    }

    sites
}

/// Every validator this crate builds, and the six that are actions rather than
/// checks.
///
/// This test is meant to go RED both ways. A new call site appears in the
/// discovered list and fails here, so a fifth mutating validator cannot be added
/// unremarked. And the day one of the six named below gains a pure validator,
/// its row changes and this test fails, so whoever closes it must also correct
/// the count and the argument in [`super::Validator`]'s own doc comment — which
/// is the point: the note must not outlive the gap.
///
/// It cannot go silently blind: a scan that stopped finding anything returns an
/// empty list and fails here rather than agreeing that nothing changed.
#[test]
fn every_validator_is_a_check_except_the_six_that_have_no_check_mode() {
    let sites = validator_call_sites();

    assert_eq!(
        sites,
        vec![
            // Pure — `id -u <name>` reads the name services and writes nothing; it checks the
            // account the limits drop-in is about to be written for (issue #194).
            row(
                "accounts/set_account_open_files_limit.rs",
                "&id",
                "CallClass::Validate"
            ),
            // Pure — `nginx -t` over the real configuration, twice: when the panel's port-80
            // block is suspended for Apache and when it is restored (issue #517).
            row("apps/nginx_port80.rs", "&nginx", "CallClass::Validate"),
            row("apps/nginx_port80.rs", "&nginx", "CallClass::Validate"),
            // Pure — Apache's own configuration test over the real tree, before it is reloaded
            // (issue #517).
            row(
                "apps/prepare_apache.rs",
                "&validator_program",
                "CallClass::Validate"
            ),
            // Pure — the DNS daemon's own configuration checker, over the real file, twice
            // (the enable and its rollback to the previous bytes); the restart is the Reload,
            // not the validator (issue #178).
            row("dns/enable_dns.rs", "&checker", "CallClass::Validate"),
            row("dns/enable_dns.rs", "&checker", "CallClass::Validate"),
            // Pure — the same checker over a throwaway probe configuration that states a
            // dnssec-policy, asking whether this build can sign (issue #452).
            row(
                "dns/probe_dnssec_support.rs",
                "&checker",
                "CallClass::Validate"
            ),
            // Pure — the zone checker reads the rendered zone and writes nothing.
            row("dns/put_zone.rs", "&checkzone", "CallClass::Validate"),
            // MUTATING — vsftpd.conf, twice. `systemctl restart`: vsftpd has no
            // check mode at all, so the only way to learn whether the file
            // parses is to make the daemon read it. The longer class, because
            // the service manager is being asked to change unit state.
            row(
                "ftps/enable_ftps.rs",
                "&service_manager",
                "CallClass::Reload"
            ),
            row(
                "ftps/enable_ftps.rs",
                "&service_manager",
                "CallClass::Reload"
            ),
            // MUTATING — the FTPS jail mount unit. `systemctl daemon-reload`: a
            // systemd unit file has no check-without-load.
            row(
                "ftps/ensure_account_jail.rs",
                "&service_manager",
                "CallClass::Reload"
            ),
            // DATA-DRIVEN — the mail stack's four daemons, one write path. Each stage carries
            // its own checker and its own class (`MailStage::validator_class`), pinned per stage
            // by `build_mail_stages_tests`. The key-value server has no check mode, so its stage
            // carries the service manager's restart: the one mutating validator that is chosen
            // by data and not by a literal, which is why it is a row of its own and not counted
            // among the four service-manager literals (issue #177).
            row(
                "mail/converge_mail_stage.rs",
                "&stage.validator_program",
                "stage.validator_class"
            ),
            // Seeding the empty lookup tables (issue #177). `postmap` writes the index file it
            // is asked to build, so it is an action on a file the daemon reads and runs in the
            // Mutation class; `doveconf -n` is a pure parse; `rspamadm configtest` is pure but
            // compiles the rule set, so it takes the long class.
            row("mail/seed_mail_maps.rs", "&postmap", "CallClass::Mutation"),
            row(
                "mail/seed_mail_maps.rs",
                "&distro.doveconf_binary()",
                "CallClass::Validate"
            ),
            row(
                "mail/seed_mail_maps.rs",
                "&distro.rspamadm_binary()",
                "CallClass::Reload"
            ),
            // Pure — php-fpm's own validate-only flag, per installed version.
            // The expression is a local because the binary's name carries the
            // version, so the adapter is asked for it before the literal is built.
            row(
                "php/reload_pool_trees.rs",
                "&validator_program",
                "CallClass::Validate"
            ),
            row(
                "php/remove_pool.rs",
                "&validator_program",
                "CallClass::Validate"
            ),
            // Pure — the same flag for an extension switch and the opcache drop-in (issue #157).
            row(
                "php/set_php_extension.rs",
                "&validator_program",
                "CallClass::Validate"
            ),
            row(
                "php/write_opcache_drop_in.rs",
                "&validator_program",
                "CallClass::Validate"
            ),
            row(
                "php/write_pool.rs",
                "&validator_program",
                "CallClass::Validate"
            ),
            // MUTATING — the SFTP jail mount unit, for the same reason as FTPS's.
            row(
                "sftp/ensure_account_jail.rs",
                "&service_manager",
                "CallClass::Reload"
            ),
            // Pure — OpenSSH's own parser reading an SFTP login's key file
            // (issue #135); it writes nothing.
            row(
                "sftp/set_sftp_authorized_keys.rs",
                "&keygen",
                "CallClass::Validate"
            ),
            // Pure — the web server's own validate-only flag, over the real tree.
            // `web_server_validator` is the one place the vhost writers (write, create,
            // remove) build it, for nginx and Apache alike; `reload_web_server` builds its own.
            row(
                "sites/reload_web_server.rs",
                "&binary",
                "CallClass::Validate"
            ),
            // MUTATING — the Node.js site's unit: `systemctl daemon-reload`, because a unit
            // file has no check-without-load. Written and removed through the protocol.
            row(
                "sites/remove_node_unit.rs",
                "&service_manager",
                "CallClass::Reload"
            ),
            row(
                "sites/web_server_validator.rs",
                "binary",
                "CallClass::Validate"
            ),
            row(
                "sites/write_node_unit.rs",
                "&service_manager",
                "CallClass::Reload"
            ),
            // Pure — the TLS material is loaded by nginx, so nginx checks it.
            row("ssl/remove_material.rs", "&nginx", "CallClass::Validate"),
            row("ssl/write_material.rs", "&binary", "CallClass::Validate"),
        ],
        "a Validator call site was added, removed or re-aimed. If a tree gained a PURE \
         validator, update the count and the argument in Validator's own doc comment. If a \
         fifth MUTATING one was added, say why in that doc comment first"
    );
}

/// The six mutating validators are exactly the ones spawning the service
/// manager, and there are exactly six.
///
/// The positive control for the census above, on the axis that can go blind: the
/// list is long, and a reader checking it by eye would not notice a `nginx_binary`
/// row quietly becoming a `service_manager` one. This counts the property
/// directly, so the number in [`super::Validator`]'s doc comment has a check
/// behind it rather than a reader's memory.
#[test]
fn exactly_six_validators_spawn_the_service_manager_instead_of_a_checker() {
    let mutating: Vec<(String, String, String)> = validator_call_sites()
        .into_iter()
        .filter(|(_, program, _)| program.contains("service_manager"))
        .collect();

    assert_eq!(
        mutating.len(),
        6,
        "Validator's doc comment says six of this crate's validators are actions rather \
         than checks; the source now says {}: {mutating:?}",
        mutating.len()
    );
}

/// The scan finds something, and finds it in more than one area.
///
/// The guard against the whole census becoming vacuous. A `sites_in` that had
/// stopped matching — a rename of the type, a reformat that put `program:` on the
/// brace line — would return an empty list, and an empty list compared against an
/// empty expectation is a test that measures nothing (rules/testing.md).
#[test]
fn the_scan_actually_finds_validators_across_several_areas() {
    let sites = validator_call_sites();

    assert!(
        sites.len() >= 12,
        "the scan found only {} sites",
        sites.len()
    );

    let areas: std::collections::BTreeSet<String> = sites
        .iter()
        .filter_map(|(file, _, _)| file.split('/').next().map(str::to_owned))
        .collect();
    assert!(
        areas.len() >= 4,
        "a validator census that sees one area is not seeing the crate: {areas:?}"
    );
}

/// A validator that asks the service manager runs in the long class, and a pure one in
/// the short class.
///
/// The census above pins the whole list; this states the RULE the list follows, so a
/// new validator is judged by it rather than copied from a neighbour. A mutating
/// validator given the 60 s class is killed in the middle of a restart, and a pure one
/// given the 240 s class holds every tenant's write for four minutes.
#[test]
fn every_mutating_validator_runs_in_the_reload_class_and_every_pure_one_in_validate() {
    let sites = validator_call_sites();

    for (file, program, class) in &sites {
        // Named exceptions, each with its reason in the census above: a stage whose class is
        // data, `postmap` (writes an index) and `rspamadm configtest` (compiles the rule set).
        if class == "stage.validator_class"
            || (file == "mail/seed_mail_maps.rs"
                && (program == "&postmap" || program == "&distro.rspamadm_binary()"))
        {
            continue;
        }
        let expected = if program.contains("service_manager") {
            "CallClass::Reload"
        } else {
            "CallClass::Validate"
        };
        assert_eq!(
            class, expected,
            "{file} builds a validator over `{program}` and gives it `{class}`"
        );
    }

    // The positive control: both sides of the rule were actually exercised.
    assert!(
        sites
            .iter()
            .any(|(_, _, class)| class == "CallClass::Reload")
    );
    assert!(
        sites
            .iter()
            .any(|(_, _, class)| class == "CallClass::Validate")
    );
}
