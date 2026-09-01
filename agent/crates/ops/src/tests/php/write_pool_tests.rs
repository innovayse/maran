//! Tests for [`write_pool`].
//!
//! What these pin is the content the operation decides to write: that the
//! pool runs as the account and not as root, that its `listen` is the exact
//! path a vhost's `fastcgi_pass` names, and that the pool's own hardening
//! survives a customer override aimed at it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;

use crate::php::fake_php_host::{FakePhpHost, distro, pool_input};
use crate::php::model::php_override::PhpOverride;
use crate::php::model::pool_paths::PoolPaths;
use crate::php::{PhpHost, PhpOpError, write_pool};

use crate::sites::fake_site_host::{FakeSiteHost, create_test_site, php_input};
use crate::sites::model::site_paths::SitePaths;

/// Where the fake's Debian adapter puts `acme`'s 8.3 pool.
const POOL_PATH: &str = "/etc/php/8.3/fpm/pool.d/acme.conf";

/// Reads back the pool `write_pool` wrote, failing the test if it wrote none.
fn written(host: &FakePhpHost) -> String {
    host.config(Path::new(POOL_PATH))
        .expect("write_pool wrote no pool file")
}

#[test]
fn the_pool_runs_as_the_account_and_never_as_root() {
    // The whole point of one pool per account: a PHP bug in one customer's
    // site is that customer's problem and nobody else's. A pool that kept the
    // master's uid would make every account's files readable from every other
    // account's PHP.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    let pool = written(&host);
    assert!(pool.contains("user = acme"), "{pool}");
    assert!(pool.contains("group = acme"), "{pool}");
}

#[test]
fn the_pool_listens_on_exactly_the_socket_the_vhost_connects_to() {
    // The failure this defends against is silent: if the two ever disagreed,
    // `nginx -t` passes, `php-fpm -t` passes, both services start, and every
    // PHP request on the host returns 502 with nothing in either config to
    // explain it. So the assertion is written against the SAME expression the
    // vhost is rendered from, not against a copied literal.
    let host = FakePhpHost::with_installed(&["8.3"]);
    let input = pool_input(Vec::new());

    write_pool(&host, distro(), &input).unwrap();

    let expected = format!(
        "{}/{}-{}.sock",
        AgentPaths::PHP_FPM_SOCKET_DIRECTORY,
        input.account.as_str(),
        input.version.as_str()
    );
    assert!(
        written(&host).contains(&format!("listen = {expected}")),
        "pool does not listen on {expected}"
    );
}

#[test]
fn the_socket_directory_is_created_before_the_pool_is_written() {
    // It lives under /run, so it is gone after every reboot: php-fpm can
    // create its socket but not the directory holding it, and a pool written
    // into a missing directory fails to bind on the next restart.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    assert!(host.directory_exists(Path::new(AgentPaths::PHP_FPM_SOCKET_DIRECTORY)));
}

#[test]
fn disable_functions_survives_an_override_attempting_to_unset_it() {
    // Two defences, and this test pins both: the name is not on the whitelist
    // so it cannot be constructed at all, and the line the template does write
    // is `php_admin_value`, which no `php_value` can countermand at any
    // position in the file.
    assert!(matches!(
        PhpOverride::parse("disable_functions", ""),
        Err(PhpOpError::OverrideNotAllowed { .. })
    ));

    let host = FakePhpHost::with_installed(&["8.3"]);
    let overrides = vec![PhpOverride::parse("memory_limit", "256M").unwrap()];

    write_pool(&host, distro(), &pool_input(overrides)).unwrap();

    let pool = written(&host);
    assert!(
        pool.contains("php_admin_value[disable_functions] ="),
        "{pool}"
    );
    assert!(
        pool.contains("php_admin_value[open_basedir] = /home/acme:"),
        "{pool}"
    );
    assert!(
        pool.contains("php_admin_value[cgi.fix_pathinfo] = 0"),
        "{pool}"
    );
    // And the customer's own setting is there, below them, as a php_value.
    assert!(pool.contains("php_value[memory_limit] = 256M"), "{pool}");
}

#[test]
fn the_pool_grants_no_access_to_the_shared_tmp() {
    // The cross-tenant failure this closes: /tmp is shared and world-readable,
    // and PHP's default session handler falls back to it when the packaged
    // session directory is root-owned — which it is. Account A's PHP could
    // then enumerate and read account B's sess_* files, which is session-token
    // theft between customers from inside the pool meant to prevent it.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    let pool = written(&host);
    let basedir = pool
        .lines()
        .find_map(|line| line.strip_prefix("php_admin_value[open_basedir] = "))
        .expect("the pool grants no open_basedir at all");
    assert!(
        basedir
            .split(':')
            .all(|granted| granted.starts_with("/home/acme")),
        "open_basedir grants something outside the account's home: {basedir}"
    );
}

#[test]
fn sessions_and_uploads_are_written_inside_the_account_home() {
    // Named admin-side, so a customer's php_value cannot move either of them
    // back to a shared location — which is the whole point of closing /tmp.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    let pool = written(&host);
    assert!(
        pool.contains("php_admin_value[session.save_path] = /home/acme/.maran/sessions"),
        "{pool}"
    );
    assert!(
        pool.contains("php_admin_value[upload_tmp_dir] = /home/acme/.maran/tmp"),
        "{pool}"
    );
}

#[test]
fn the_session_and_upload_directories_are_created_as_the_account() {
    // As the account and not as root, for two independent reasons: a customer
    // path is only ever touched after a privilege drop (rules/security.md),
    // and a root-owned session directory is unwritable by the pool's workers —
    // which is the exact condition that sends PHP back to /tmp.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    let created = host.created_as_account();
    assert!(
        created.contains(&PathBuf::from("/home/acme/.maran/sessions")),
        "{created:?}"
    );
    assert!(
        created.contains(&PathBuf::from("/home/acme/.maran/tmp")),
        "{created:?}"
    );
}

#[test]
fn the_socket_directory_gets_an_explicit_mode() {
    // Not inherited from the umask. World-writable and non-sticky — what a
    // umask of zero yields — lets one account unlink a neighbour's socket and
    // bind its own in its place, so every request for the neighbour's sites
    // reaches the wrong customer's PHP.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    assert_eq!(
        host.mode(Path::new(AgentPaths::PHP_FPM_SOCKET_DIRECTORY)),
        Some(0o755)
    );
}

#[test]
fn the_session_and_upload_directories_are_readable_only_by_the_account() {
    // A PHP session filename IS the session ID — `sess_<id>` — so a
    // world-listable session directory hands a live session to anyone who can
    // run `ls` in it, without a byte of file content being read. Left to the
    // forked child's umask this is typically 0755, and the cross-tenant hole
    // closed by moving sessions out of /tmp would reopen through the directory
    // listing instead.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    assert_eq!(
        host.mode(Path::new("/home/acme/.maran/sessions")),
        Some(0o700)
    );
    assert_eq!(host.mode(Path::new("/home/acme/.maran/tmp")), Some(0o700));
}

#[test]
fn a_stuck_request_is_reclaimed_by_php_fpms_own_timer() {
    // max_execution_time stops counting the moment a request blocks in a
    // syscall, so a customer waiting on a dead database is never counted by
    // PHP at all and holds a worker forever. request_terminate_timeout is the
    // limit that actually reclaims it, and it is set where a customer cannot
    // reach it.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    assert!(
        written(&host).contains("request_terminate_timeout = 330s"),
        "{}",
        written(&host)
    );
}

#[test]
fn a_shorter_execution_limit_shortens_the_hard_timeout_too() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let overrides = vec![PhpOverride::parse("max_execution_time", "30").unwrap()];

    write_pool(&host, distro(), &pool_input(overrides)).unwrap();

    assert!(
        written(&host).contains("request_terminate_timeout = 60s"),
        "{}",
        written(&host)
    );
}

#[test]
fn php_reports_a_fatal_error_before_php_fpm_kills_the_worker() {
    // The two timers must not fire together. At equal values an ordinary
    // CPU-bound request that legitimately runs to its limit is killed by
    // php-fpm before PHP raises its fatal error, so the customer gets a blank
    // page with no stack trace and nothing in their error log. The margin
    // means fpm's timer only ever fires for the case it was added for: a
    // request blocked in a syscall, which PHP's timer does not count at all.
    let host = FakePhpHost::with_installed(&["8.3"]);
    let overrides = vec![PhpOverride::parse("max_execution_time", "45").unwrap()];

    write_pool(&host, distro(), &pool_input(overrides)).unwrap();

    let pool = written(&host);
    let terminate: u32 = pool
        .lines()
        .find_map(|line| line.strip_prefix("request_terminate_timeout = "))
        .and_then(|value| value.trim_end_matches('s').parse().ok())
        .expect("the pool sets no request_terminate_timeout");
    let execution: u32 = pool
        .lines()
        .find_map(|line| line.strip_prefix("php_value[max_execution_time] = "))
        .and_then(|value| value.parse().ok())
        .expect("the pool sets no max_execution_time");

    assert!(
        terminate > execution,
        "php-fpm ({terminate}s) does not outlive PHP ({execution}s)"
    );
}

#[test]
fn disable_functions_covers_the_published_ld_preload_escape() {
    // putenv("LD_PRELOAD=…") followed by any function that spawns a process is
    // the standard bypass of a disable_functions list, and it makes the rest
    // of the list decorative. It does not cross the uid boundary — the pool's
    // real defence — but the file advertises this protection, so it must
    // actually hold.
    let host = FakePhpHost::with_installed(&["8.3"]);

    write_pool(&host, distro(), &pool_input(Vec::new())).unwrap();

    let pool = written(&host);
    let disabled = pool
        .lines()
        .find_map(|line| line.strip_prefix("php_admin_value[disable_functions] = "))
        .expect("the pool disables nothing");
    for function in ["putenv", "dl", "pcntl_exec", "proc_open", "system"] {
        assert!(
            disabled.split(',').any(|listed| listed == function),
            "{function} is not disabled: {disabled}"
        );
    }
    // mail stays enabled deliberately: it spawns sendmail, but disabling it
    // breaks ordinary sites and the panel offers no SMTP alternative yet.
    assert!(
        !disabled.split(',').any(|listed| listed == "mail"),
        "{disabled}"
    );
}

/// Runs `write_pool` with `max_children` and nothing else changed.
fn with_workers(host: &FakePhpHost, max_children: u32) -> Result<(), PhpOpError> {
    let mut input = pool_input(Vec::new());
    input.max_children = max_children;
    write_pool(host, distro(), &input)
}

#[test]
fn an_absurd_worker_count_from_the_panel_is_refused_not_clamped() {
    // Refused, because clamping would stop the denial of service and then
    // silently write a pool that does not match the plan the customer is
    // paying for. That is the same failure the whitelist refuses for customer
    // settings, and worse here: nobody chose this number by hand, so a wrong
    // one is a panel bug that only surfaces if the agent says so.
    let host = FakePhpHost::with_installed(&["8.3"]);

    match with_workers(&host, u32::MAX) {
        Err(PhpOpError::WorkerBudgetOutOfRange {
            requested,
            minimum,
            maximum,
        }) => {
            assert_eq!(requested, u32::MAX);
            assert_eq!((minimum, maximum), (1, 256));
        }
        other => panic!("expected WorkerBudgetOutOfRange, got {other:?}"),
    }
    assert_eq!(host.writes(), 0);
}

#[test]
fn a_plan_carrying_no_workers_is_refused() {
    // Zero renders a pool that forks nothing and serves nothing. Silently
    // raising it to one would hide the plan that produced it.
    let host = FakePhpHost::with_installed(&["8.3"]);

    assert!(matches!(
        with_workers(&host, 0),
        Err(PhpOpError::WorkerBudgetOutOfRange { requested: 0, .. })
    ));
}

#[test]
fn the_worker_budget_boundaries_are_both_accepted() {
    // The edges pinned in the direction that matters: an off-by-one either way
    // refuses a budget the constants document as legal, and a plan sold at 256
    // workers would stop provisioning with no code having changed meaning.
    let host = FakePhpHost::with_installed(&["8.3"]);

    with_workers(&host, 1).unwrap();
    assert!(
        written(&host).contains("pm.max_children = 1"),
        "{}",
        written(&host)
    );

    with_workers(&host, 256).unwrap();
    assert!(
        written(&host).contains("pm.max_children = 256"),
        "{}",
        written(&host)
    );
}

#[test]
fn a_budget_one_past_the_ceiling_is_refused() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    assert!(matches!(
        with_workers(&host, 257),
        Err(PhpOpError::WorkerBudgetOutOfRange { requested: 257, .. })
    ));
}

#[test]
fn the_worker_budget_from_the_plan_is_what_the_pool_gets() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let mut input = pool_input(Vec::new());
    input.max_children = 40;

    write_pool(&host, distro(), &input).unwrap();

    let pool = written(&host);
    assert!(pool.contains("pm.max_children = 40"), "{pool}");
    // The spare bounds scale with it rather than staying at a fixed pair that
    // would make a 40-worker pool behave like a 4-worker one.
    assert!(pool.contains("pm.min_spare_servers = 5"), "{pool}");
    assert!(pool.contains("pm.max_spare_servers = 13"), "{pool}");
}

#[test]
fn a_version_that_is_not_installed_is_refused_before_anything_is_written() {
    // Without this the write fails at the temporary file, deep inside the
    // protocol, with "no such directory" — true, and useless to the operator
    // who has to work out that a package is missing.
    let host = FakePhpHost::empty();

    match write_pool(&host, distro(), &pool_input(Vec::new())) {
        Err(PhpOpError::PhpVersionNotInstalled { version }) => assert_eq!(version, "8.3"),
        other => panic!("expected PhpVersionNotInstalled, got {other:?}"),
    }
    assert_eq!(host.writes(), 0);
}

#[test]
fn an_unsupported_version_is_refused_by_the_agent() {
    let host = FakePhpHost::empty();
    let mut input = pool_input(Vec::new());
    input.version = PhpVersion::parse("9.9").unwrap();

    assert!(matches!(
        write_pool(&host, distro(), &input),
        Err(PhpOpError::UnsupportedVersion { .. })
    ));
}

#[test]
fn a_pool_php_fpm_refuses_is_not_left_behind() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.reject_validation("unknown entry 'pm.foo'");

    match write_pool(&host, distro(), &pool_input(Vec::new())) {
        Err(PhpOpError::PoolValidation { stderr }) => assert!(stderr.contains("pm.foo")),
        other => panic!("expected PoolValidation, got {other:?}"),
    }
    assert_eq!(host.config(Path::new(POOL_PATH)), None);
}

#[test]
fn the_vhost_connects_to_the_socket_this_pool_listens_on() {
    // The two ends of the socket, compared against each other rather than
    // each against a literal. `sites::render_vhost` writes the
    // `fastcgi_pass` and `write_pool` writes the `listen`; if either is
    // edited alone the host serves 502 on every PHP site and no other test in
    // the suite fails. Both sides are read out of what was actually written.
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let site_host = FakeSiteHost::passing();
    let site = php_input();

    write_pool(&php_host, distro(), &pool_input(Vec::new())).unwrap();
    create_test_site(&site_host, &site).unwrap();

    let listen = written(&php_host)
        .lines()
        .find_map(|line| line.strip_prefix("listen = ").map(str::to_owned))
        .expect("the pool declares no listen");
    let vhost = site_host
        .config(&SitePaths::for_site(&site.account, &site.domain).config_path)
        .expect("create_site wrote no vhost");

    assert!(
        vhost.contains(&format!("fastcgi_pass unix:{listen};")),
        "the vhost does not connect to `{listen}`"
    );
}

#[test]
fn the_pool_file_lands_in_the_directory_the_adapter_names() {
    // Not a literal in `ops`: the families disagree on both the shape of this
    // path and on whether the version keeps its dot (rules/rust.md "Distro
    // adapter").
    let account = AccountName::parse("acme").unwrap();
    let version = PhpVersion::parse("8.3").unwrap();

    let paths = PoolPaths::for_pool(distro(), &account, &version);

    assert_eq!(paths.config_path.display().to_string(), POOL_PATH);
    assert_eq!(paths.pool_name, "acme-8.3");
}
