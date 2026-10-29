//! Sites against a real nginx, which is the only place `safe_write` means
//! anything.
//!
//! The config-write protocol renames a rendered vhost into place and then runs
//! the validating binary against the real configuration tree, restoring the
//! previous content if that binary refuses. Every test of it so far has used a
//! `ConfigHost` that returns whatever the test wanted, so the protocol has been
//! proved to react correctly to an answer nobody has ever asked nginx for. This
//! suite asks nginx.
//!
//! It runs in the polygon container, as root, where `/etc/nginx/nginx.conf`
//! includes the agent's own `/etc/maran/nginx/sites` — the one line an installer
//! adds on a real server (spec §9). Without that include, `nginx -t` would parse
//! a tree the agent's vhosts are not in and approve anything they said.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/in_process_agent.rs"]
mod in_process_agent;
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_config_file.rs"]
mod polygon_config_file;

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;
use std::sync::mpsc;
use std::time::Duration;

use maran_agent::proto::sites_service_client::SitesServiceClient;
use maran_agent::proto::{CreateSiteRequest, ErrorCode, SiteBackendType, create_site_response};
use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_agent_core::validation::web::upstream::Upstream;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::files::{ProcessFilesHost, WriteFileInput};
use maran_ops::php::ProcessPhpHost;
use maran_ops::sites::{
    CreateSiteInput, ProcessSiteHost, SiteCertificate, SiteHost, SiteIdentity, SiteKind, SitePaths,
    SitesOpError, create_site,
};
use maran_ops::ssl::{ProcessSslHost, delete_site_with_certificate, generate_self_signed};

use in_process_agent::InProcessAgent;
use polygon_account::PolygonAccount;
use polygon_config_file::PolygonConfigFile;

/// The PHP version both polygon images install: 8.3 on Sury, 83 on Remi.
///
/// The agent writes it in the one form the panel uses, and the adapter turns it
/// into whatever the family spells it as — which is the reason a site can name a
/// version at all without the operation knowing a distribution's name.
const POLYGON_PHP_VERSION: &str = "8.3";

/// The argument that makes nginx check its configuration instead of serving it.
///
/// Spelled again here rather than imported. `ops::sites::write_vhost` declares it
/// `pub(crate)`, so an integration test — a separate crate — could not import it
/// in any case; and it should not, because a test that took its expectation from
/// the code under test would pass even if that code stopped passing the argument.
const VALIDATE_ARGUMENT: &str = "-t";

/// The distribution adapter for the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

#[test]
#[ignore = "writes a real vhost and runs the real nginx -t: polygon only"]
fn a_php_site_is_written_and_the_real_nginx_accepts_it() {
    // The guard first, before any side effect. `ensure_nginx_is_running` would
    // otherwise start an nginx master on the machine of a developer who ran this
    // suite outside a container, and only then discover it should not have run.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysitesone");
    let _pool = pool_guard(&account);
    let domain = Domain::parse("php.example.test").expect("a valid domain");
    let created = create_polygon_site(&ProcessSiteHost::new(), &php_site(&account, &domain))
        .unwrap_or_else(|error| panic!("creating a PHP site must succeed in the polygon: {error}"));

    let vhost = Path::new(&created.config_path);
    let _vhost = PolygonConfigFile::at(vhost);
    assert!(vhost.exists(), "the vhost must be on disk at {vhost:?}");

    let contents = std::fs::read_to_string(vhost).expect("the vhost must be readable");
    assert!(
        contents.contains("fastcgi_pass unix:/run/maran-php/"),
        "a PHP site's vhost must point at the pool socket the agent owns"
    );
    assert!(
        Path::new(&created.document_root).starts_with(account.home()),
        "the document root must be inside the account's own home"
    );
    assert!(
        contents.contains(&created.document_root),
        "the vhost must serve the document root the operation resolved"
    );

    // The document root is inside the account's home and belongs to the
    // account, because it was created by a child that had dropped to it.
    let root = std::fs::metadata(&created.document_root).expect("the document root must exist");
    assert_eq!(
        std::os::unix::fs::MetadataExt::uid(&root),
        account.ids().uid()
    );

    // And the claim this whole suite exists for: the file the agent wrote is
    // one the real nginx parses, in the real tree, alongside everything else
    // that tree includes.
    assert_valid_nginx_tree("the vhost the agent just wrote");
    // A passing `nginx -t` alone would not distinguish "nginx parsed this file"
    // from "nginx never reached it", which is what a missing include line looks
    // like — so the dump is asked whether the file is in the configuration it
    // actually loaded.
    assert_nginx_loads(vhost);
}

#[test]
#[ignore = "writes a real vhost and runs the real nginx -t: polygon only"]
fn a_site_the_real_nginx_rejects_is_refused_by_create_site_and_leaves_the_tree_as_it_was() {
    // The guard first, before any side effect. `ensure_nginx_is_running` would
    // otherwise start an nginx master on the machine of a developer who ran this
    // suite outside a container, and only then discover it should not have run.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysitestwo");
    let _pool = pool_guard(&account);
    let host = ProcessSiteHost::new();

    // One good site first, so there is something a botched write could damage.
    let good_domain = Domain::parse("rollback.example.test").expect("a valid domain");
    let created = create_polygon_site(&host, &php_site(&account, &good_domain))
        .unwrap_or_else(|error| panic!("the first site must be created: {error}"));
    let vhost = Path::new(&created.config_path).to_path_buf();
    let _vhost = PolygonConfigFile::at(&vhost);
    let good = std::fs::read_to_string(&vhost).expect("the vhost must be readable");

    // The rejection goes through `create_site`, which is the whole point of this
    // test: everything the agent does to a vhost in production runs through that
    // function and the validator IT chooses. A test that assembled its own
    // `Validator` and called `write_config` directly would pass unchanged if
    // `write_vhost` started validating with `/bin/true`, which is the one defect
    // this suite exists to make impossible.
    //
    // The input is entirely legitimate to the panel — `Domain::parse` allows 253
    // characters in labels of up to 63 — and the real nginx refuses it, because
    // a `server_name` longer than its `server_names_hash_bucket_size` cannot be
    // hashed. A valid input the validator rejects is exactly the case
    // `NginxValidation` exists for.
    let refused_domain = Domain::parse(&format!(
        "{}.{}.example.test",
        "a".repeat(63),
        "b".repeat(63)
    ))
    .expect("a long domain is still a valid domain");
    let refused_vhost = SitePaths::for_site(account.name(), &refused_domain).config_path;

    let refusal = create_polygon_site(&host, &php_site(&account, &refused_domain));

    assert!(
        matches!(refusal, Err(SitesOpError::NginxValidation { .. })),
        "create_site must return the real nginx's refusal, got {refusal:?}"
    );

    // Nothing of the refused site is left in the tree. The write renames the file
    // into place BEFORE validating, so at one instant this file existed and the
    // tree was invalid; the protocol has to take it away again.
    assert!(
        !refused_vhost.exists(),
        "a refused site must leave no vhost behind at {refused_vhost:?}"
    );

    // The assertion that had never been made against a real validator: the site
    // that was already there survived a neighbouring write nginx refused, byte
    // for byte.
    let after = std::fs::read_to_string(&vhost).expect("the first vhost must still be there");
    assert_eq!(
        after, good,
        "a rejected write must not disturb another site"
    );

    // And the tree is valid again, which is the property an operator cares
    // about: a failed write leaves a server that can still be reloaded.
    assert_valid_nginx_tree("the tree after a refused site was rolled back");
    assert_nginx_loads(&vhost);
}

#[test]
#[ignore = "writes a real vhost and runs the real nginx -t: polygon only"]
fn a_site_that_already_exists_is_not_rewritten() {
    // The guard first, before any side effect. `ensure_nginx_is_running` would
    // otherwise start an nginx master on the machine of a developer who ran this
    // suite outside a container, and only then discover it should not have run.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysitesthree");
    let _pool = pool_guard(&account);
    let domain = Domain::parse("idempotent.example.test").expect("a valid domain");
    let input = php_site(&account, &domain);
    let created = create_polygon_site(&ProcessSiteHost::new(), &input)
        .unwrap_or_else(|error| panic!("the first write must succeed: {error}"));
    let vhost = Path::new(&created.config_path).to_path_buf();
    let _vhost = PolygonConfigFile::at(&vhost);
    let first = std::fs::read_to_string(&vhost).expect("the vhost must be readable");

    let again = create_polygon_site(&ProcessSiteHost::new(), &input);

    assert!(
        matches!(again, Err(SitesOpError::AlreadyExists { .. })),
        "a retried creation must report the site as existing, got {again:?}"
    );
    assert_eq!(
        std::fs::read_to_string(&vhost).expect("the vhost must still be readable"),
        first,
        "a retry must not rewrite a live vhost"
    );
}

/// The account plan worker budget these sites are created against.
///
/// Immaterial to what nginx thinks of a vhost, which is what this suite is
/// about; it is here because a PHP site now writes its pool as part of being
/// created, and `pm.max_children` is part of what a pool is.
const POLYGON_WORKERS: u32 = 5;

/// Creates a site the way the running agent creates one: with the real site
/// host, the real PHP host and this polygon's own adapter.
///
/// The php host is the REAL one, so a PHP site created here writes a real pool
/// and reloads a real php-fpm — which is the point. A creation that wrote only
/// a vhost would leave a site whose `fastcgi_pass` names a socket nothing has
/// bound, and a polygon that accepted that would be proving the wrong thing.
fn create_polygon_site(
    host: &ProcessSiteHost,
    input: &CreateSiteInput,
) -> Result<maran_ops::sites::CreatedSite, SitesOpError> {
    create_site(
        host,
        &ProcessPhpHost::new(),
        polygon_distro(),
        input,
        POLYGON_WORKERS,
        &[],
    )
}

/// The input for a PHP site owned by `account` at `domain`.
fn php_site(account: &PolygonAccount, domain: &Domain) -> CreateSiteInput {
    CreateSiteInput {
        account: account.name().clone(),
        domain: domain.clone(),
        aliases: Vec::new(),
        kind: SiteKind::Php {
            version: PhpVersion::parse(POLYGON_PHP_VERSION).expect("a valid PHP version"),
        },
        certificate: None,
    }
}

/// Fails the test unless nginx's own dump of the configuration it loaded names
/// `vhost`.
///
/// `nginx -T` prints every file it read, each behind a `# configuration file
/// <path>:` line, and it is THAT form the assertion requires — not the path
/// anywhere in the dump. A bare substring would also match a path nginx merely
/// printed: a `root` or `access_log` sharing the prefix, an `include` naming a
/// sibling, or a future template that stamped its own path into a comment. Any
/// of those would make this assertion pass on a host whose nginx never opened
/// the file, which is the precise hole it was written to close. Asking it is the
/// difference between "the tree is valid" and "the tree
/// is valid AND contains the agent's work" — the first is true of a host whose
/// `nginx.conf` never includes `/etc/maran/nginx/sites` at all, where every
/// vhost the agent writes is a file nothing ever parses.
///
/// # Panics
///
/// Panics when nginx cannot be run, when it rejects the tree, or when the dump
/// does not name `vhost`.
fn assert_nginx_loads(vhost: &Path) {
    let output = std::process::Command::new(polygon_distro().nginx_binary())
        .arg("-T")
        .output()
        .expect("the polygon image installs nginx");

    assert!(
        output.status.success(),
        "nginx -T must succeed:\n{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let dump = String::from_utf8_lossy(&output.stdout);
    let header = format!("# configuration file {}:", vhost.display());
    assert!(
        dump.contains(&header),
        "nginx must have loaded {vhost:?}; its own dump carries no {header:?} line"
    );
}

/// Starts an nginx master in the polygon, if this run has not started one yet.
///
/// Without a running master the polygon's stand-in for the service manager has
/// nothing to signal and answers a reload with success, so the reload step of
/// the config-write protocol would be a no-op that no test could tell from a
/// real one. With one, `systemctl reload nginx` becomes an `nginx -s reload`
/// that a live master either accepts or refuses.
///
/// Idempotent by the same reasoning the operations are: it is called by every
/// test in the file, and starting a second master would fail on the listening
/// socket.
///
/// # Panics
///
/// Panics when nginx cannot be started.
fn ensure_nginx_is_running() {
    if std::fs::read_to_string("/run/nginx.pid")
        .ok()
        .and_then(|pid| pid.trim().parse::<u32>().ok())
        .is_some_and(|pid| Path::new(&format!("/proc/{pid}")).exists())
    {
        return;
    }

    let started = std::process::Command::new(polygon_distro().nginx_binary())
        .output()
        .expect("the polygon image installs nginx");

    assert!(
        started.status.success(),
        "nginx must start in the polygon:\n{}",
        String::from_utf8_lossy(&started.stderr)
    );
}

/// Runs the real `nginx -t` over the real configuration tree and fails the test
/// with nginx's own words when it refuses.
///
/// `what` names the state being checked, so a failure says which of the two
/// moments in a rollback test went wrong.
///
/// # Panics
///
/// Panics when nginx cannot be run, or when it rejects the tree.
fn assert_valid_nginx_tree(what: &str) {
    let output = std::process::Command::new(polygon_distro().nginx_binary())
        .arg(VALIDATE_ARGUMENT)
        .output()
        .expect("the polygon image installs nginx");

    assert!(
        output.status.success(),
        "nginx -t must accept {what}:\n{}",
        String::from_utf8_lossy(&output.stderr)
    );
}

#[test]
#[ignore = "creates a real account and site and asks a real nginx for it: polygon only"]
fn a_real_nginx_serves_a_site_out_of_the_accounts_own_home() {
    // The end-to-end claim nothing else in this workspace makes: a site the panel
    // creates can actually be fetched. Every other test here proves nginx PARSES what
    // the agent wrote, which is a different question — and the answer to this one used
    // to be no. `useradd --create-home` leaves /home/<account> at 0750 owned by the
    // account, the web server is in no group that can enter it, and a real nginx logged
    // `stat() "/home/<account>/sites/<domain>/" failed (13: Permission denied)` for
    // every request. Creating an account now group-owns its home by the web server's
    // group, and this is the test that says so in the only terms that matter.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polyservesone");
    let domain = Domain::parse("serves.example.test").expect("a valid domain");

    // A STATIC site: this is about reaching the document root, and a PHP site would
    // fold the answer together with whether php-fpm is up.
    let input = CreateSiteInput {
        account: account.name().clone(),
        domain: domain.clone(),
        aliases: Vec::new(),
        kind: SiteKind::Static,
        certificate: None,
    };
    let created = create_polygon_site(&ProcessSiteHost::new(), &input)
        .unwrap_or_else(|error| panic!("creating a static site must succeed: {error}"));
    let _vhost = PolygonConfigFile::at(Path::new(&created.config_path));

    // The modes the web worker's traversal depends on, asserted directly: `sites/`
    // and the document root 0755 whatever umask the daemon runs under, the home
    // 0750 and never world-readable. (SELinux labels are NOT observed here.)
    let docroot = Path::new(&created.document_root);
    let mode_of = |path: &Path| {
        std::fs::metadata(path)
            .expect("must exist")
            .permissions()
            .mode()
            & 0o777
    };
    assert_eq!(mode_of(docroot), 0o755, "the document root must be 0755");
    assert_eq!(
        mode_of(docroot.parent().expect("sites/")),
        0o755,
        "sites/ must be 0755"
    );
    assert_eq!(mode_of(account.home()), 0o750, "the home must stay 0750");

    // Written AS THE ACCOUNT, through the same file operation the panel uses, so the
    // file's owner and mode are a customer's and not root's.
    let page = within("the index write", {
        let name = account.name().clone();
        move || {
            maran_ops::files::write_file(
                &ProcessFilesHost::new(),
                &WriteFileInput {
                    account: name,
                    path: RelativePath::parse("sites/serves.example.test/index.html")
                        .expect("the page path must be valid"),
                    contents: SERVED_BODY.as_bytes().to_vec(),
                    mode: FileMode::parse(0o644).expect("a plain permission mode"),
                },
            )
        }
    });
    assert_eq!(page, Ok(SERVED_BODY.len() as u64));

    reload_polygon_nginx();

    // Polled to a deadline rather than asked once: `nginx -s reload` returns as soon
    // as the master has taken the signal, and the workers that answer the next
    // connection are replaced a moment later. Asking once measured the race and not
    // the site, and the answer it gave was the default server's welcome page.
    let response = fetch_until(domain.as_str(), "/", |body| body.contains(SERVED_BODY));

    assert!(
        response.starts_with("HTTP/1.1 200 "),
        "a real nginx must serve the site out of the account's home; it answered:\n{response}"
    );
    assert!(
        response.contains(SERVED_BODY),
        "the body served must be the file the account wrote:\n{response}"
    );
}

/// The body the served-site test writes and then expects back over HTTP.
const SERVED_BODY: &str = "maran-serves-ok";

/// How long the HTTP exchange against the polygon's own nginx is given.
///
/// Bounded on purpose, like every other wait in these suites: a request to a
/// socket nothing answers would otherwise hang the run, and a hang is read as a
/// flaky runner and retried — which is how a real refusal survives a test run.
const HTTP_TIMEOUT: Duration = Duration::from_secs(15);

/// Runs `body` on its own thread and fails the test if it outlasts the timeout.
///
/// `write_file` forks and blocks in `waitpid` with no timeout of its own.
fn within<T: Send + 'static>(what: &str, body: impl FnOnce() -> T + Send + 'static) -> T {
    let (sender, receiver) = mpsc::channel();
    std::thread::spawn(move || {
        let _ = sender.send(body());
    });

    receiver
        .recv_timeout(CHILD_TIMEOUT)
        .unwrap_or_else(|_| panic!("{what} did not finish within {CHILD_TIMEOUT:?}"))
}

/// How long a forked child is given before the test declares it stuck.
const CHILD_TIMEOUT: Duration = Duration::from_secs(30);

/// Reloads the running nginx so a newly written vhost is being served.
///
/// The write protocol already reloads, but the page is written afterwards; this
/// keeps the request below asking about the configuration that is actually
/// loaded rather than about one that was loaded a moment earlier.
fn reload_polygon_nginx() {
    let reloaded = std::process::Command::new(polygon_distro().nginx_binary())
        .args(["-s", "reload"])
        .output()
        .expect("the polygon image installs nginx");

    assert!(
        reloaded.status.success(),
        "nginx must reload in the polygon:\n{}",
        String::from_utf8_lossy(&reloaded.stderr)
    );
}

/// Asks the polygon's own nginx for `path` on `host`, over a real socket.
///
/// A hand-written request rather than an HTTP client dependency: one GET with a
/// `Host` header is the whole protocol this needs, and rules/security.md item 11
/// makes a new dependency something to justify rather than reach for.
///
/// # Panics
///
/// Panics when nothing answers on port 80 within [`HTTP_TIMEOUT`], which is the
/// bounded failure this exists to produce instead of a hang.
fn fetch(host: &str, path: &str) -> String {
    use std::io::{Read as _, Write as _};

    let mut stream = std::net::TcpStream::connect_timeout(
        &std::net::SocketAddr::from(([127, 0, 0, 1], 80)),
        HTTP_TIMEOUT,
    )
    .expect("nginx must be listening on port 80 in the polygon");
    stream
        .set_read_timeout(Some(HTTP_TIMEOUT))
        .expect("a read timeout can always be set on a connected socket");
    stream
        .set_write_timeout(Some(HTTP_TIMEOUT))
        .expect("a write timeout can always be set on a connected socket");

    // `Connection: close`, so the read below ends at end-of-stream rather than
    // waiting out the keep-alive timeout on a response it has already had.
    let request = format!("GET {path} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n");
    stream
        .write_all(request.as_bytes())
        .expect("the request must reach nginx");

    let mut response = Vec::new();
    stream
        .read_to_end(&mut response)
        .expect("nginx must answer within the timeout");

    String::from_utf8_lossy(&response).into_owned()
}

/// How long the poll below keeps asking before it gives up and reports what it got.
const SERVE_DEADLINE: Duration = Duration::from_secs(20);

/// How long the poll waits between attempts.
const SERVE_POLL_INTERVAL: Duration = Duration::from_millis(100);

/// Asks nginx for `path` on `host` until `accepted` is satisfied or the deadline passes.
///
/// Bounded, and it returns the LAST response either way rather than panicking, so a
/// site that never becomes reachable produces the failing assertion at the call site —
/// with the wrong answer printed — instead of a timeout message that says nothing about
/// what nginx actually served.
fn fetch_until(host: &str, path: &str, accepted: impl Fn(&str) -> bool) -> String {
    let deadline = std::time::Instant::now() + SERVE_DEADLINE;
    let mut last = fetch(host, path);

    while !accepted(&last) && std::time::Instant::now() < deadline {
        std::thread::sleep(SERVE_POLL_INTERVAL);
        last = fetch(host, path);
    }

    last
}

/// Takes responsibility for the php-fpm pool a PHP site's creation writes.
///
/// Creating a PHP site now writes the account's pool as well as its vhost, and the
/// pool outlives the account: `PolygonAccount` removes the user, the pool file stays,
/// and the next `php-fpm -t` in this shared tree fails with
/// `cannot get uid for user '<gone>'` — a cascade in which one test's leftovers read
/// as three tests failing for reasons of their own.
///
/// The path is derived from the adapter rather than from the code under test, so a
/// pool writer that started putting files somewhere else would leave this one behind
/// and be caught, not quietly followed.
fn pool_guard(account: &PolygonAccount) -> PolygonConfigFile {
    PolygonConfigFile::at(
        Path::new(&polygon_distro().php_fpm_pool_directory(POLYGON_PHP_VERSION))
            .join(format!("{}.conf", account.name().as_str())),
    )
}

#[test]
#[ignore = "creates and deletes a real account with a real pool: polygon only"]
fn deleting_an_account_leaves_a_host_the_real_php_fpm_will_still_start() {
    // The trap this closes, and the reason it is asserted with `php-fpm -t`
    // rather than with a file-existence check: a pool file names the account it
    // runs as, php-fpm resolves that name at STARTUP, and once the account is
    // gone the master refuses to start or reload at all —
    // `cannot get uid for user '<account>'`. So a leftover pool is not one
    // broken customer. It is a landmine that goes off at the next reload, for
    // any reason, and takes PHP down for every tenant on the server. The
    // failure and its cause are separated by days.
    //
    // Nothing removed a pool anywhere in the agent, which is how this shipped.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();

    let pool = {
        let account = PolygonAccount::create("polydeleteone");
        let domain = Domain::parse("deleted.example.test").expect("a valid domain");
        let created = create_polygon_site(&ProcessSiteHost::new(), &php_site(&account, &domain))
            .unwrap_or_else(|error| panic!("creating the PHP site must succeed: {error}"));
        let _vhost = PolygonConfigFile::at(Path::new(&created.config_path));

        let pool = Path::new(&polygon_distro().php_fpm_pool_directory(POLYGON_PHP_VERSION))
            .join(format!("{}.conf", account.name().as_str()));
        assert!(
            pool.exists(),
            "the fixture is only meaningful if creating the site really wrote a pool at {pool:?}"
        );

        // The account is dropped HERE, at the end of this block, which is what
        // runs the real `AccountOperations::delete` — the operation under test.
        pool
    };

    // The real tool, asked FIRST and deliberately before the file check below.
    // A file-existence assertion is the weaker claim and it is also the one that
    // would fire first and hide this one: what matters is not that a path is
    // gone but that php-fpm will still start, and only php-fpm can say so — a
    // pool that was emptied, renamed, or left in a directory php-fpm no longer
    // reads would pass a file check and fail this.
    let php_version = PhpVersion::parse(POLYGON_PHP_VERSION).expect("a valid PHP version");
    let checked = std::process::Command::new(polygon_distro().php_fpm_binary(&php_version))
        .arg(VALIDATE_ARGUMENT)
        .output()
        .expect("the polygon image installs php-fpm");

    assert!(
        checked.status.success(),
        "after deleting an account, the real php-fpm must still accept its configuration — \
         otherwise the next reload takes PHP down for every other tenant:\n{}",
        String::from_utf8_lossy(&checked.stderr)
    );

    // Secondary, and kept: it says WHY php-fpm is happy, which a bare exit
    // status does not.
    assert!(
        !pool.exists(),
        "deleting the account must take its pool at {pool:?} with it"
    );
}

#[test]
#[ignore = "writes a real vhost and a real private key: polygon only"]
fn deleting_a_site_takes_its_private_key_off_the_disk() {
    // The one claim about this that a fake cannot make. Every existing test of
    // the purge asserts against a `FakeSslHost`'s in-memory store, which is a
    // statement about the operation and not about the disk — and the operation
    // was correct the whole time. What was wrong was that nothing called it, and
    // what an operator found on a real host afterwards was
    // `/etc/maran/certificates/<domain>/privkey.pem`, still there, 1704 bytes,
    // after a deletion the panel reported as complete.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysitekey");
    let domain = Domain::parse("polysitekey.example.com").expect("a valid domain");
    let host = ProcessSslHost::new();
    let input = php_site(&account, &domain);

    create_site(
        &host,
        &ProcessPhpHost::new(),
        polygon_distro(),
        &input,
        POLYGON_WORKERS,
        &[],
    )
    .expect("the site must be created");

    // Real openssl, real key material, at the real path and the real mode. A
    // test that planted a file called `privkey.pem` would prove the unlink and
    // nothing about what the agent installs.
    generate_self_signed(&host, polygon_distro(), &input)
        .expect("the polygon image installs openssl");

    let material = SiteCertificate::for_domain(&domain);
    assert!(
        material.key_path().exists(),
        "the key must really be on disk before the deletion, or its absence \
         afterwards proves nothing"
    );
    assert_eq!(
        std::fs::metadata(material.key_path())
            .expect("the key is readable by root")
            .permissions()
            .mode()
            & 0o777,
        0o600,
        "the material this test is about must be the real thing: a root-only key"
    );

    delete_site_with_certificate(
        &host,
        &ProcessPhpHost::new(),
        polygon_distro(),
        &SiteIdentity {
            account: account.name().clone(),
            domain: domain.clone(),
        },
        Some(&PhpVersion::parse(POLYGON_PHP_VERSION).expect("a valid PHP version")),
    )
    .expect("the deletion must succeed");

    // The file, not a row and not a fake's counter.
    assert!(
        !material.key_path().exists(),
        "the private key of a deleted site must not be on the disk: {:?}",
        material.key_path()
    );
    assert!(
        !material.certificate_path().exists(),
        "the certificate of a deleted site must not be on the disk either"
    );

    // And the host is still serviceable. Unlinking material a running
    // configuration still names is the failure this ordering exists to avoid,
    // and it does not announce itself — it waits for the next reload, which may
    // belong to an unrelated site. So nginx is asked now, about its whole tree.
    assert_valid_nginx_tree("after a site and its certificate were deleted");
}

/// The body a suspended account's own page would serve if the stub were not
/// there.
const SUSPENDED_ACCOUNT_BODY: &str = "maran-should-not-be-served";

/// The fixed sentence the suspended vhost answers every other request with.
const SUSPENSION_NOTICE: &str = "This site has been suspended.";

/// The file name the ACME HTTP-01 challenge is fetched under.
const ACME_TOKEN: &str = "maran-acme-token";

/// The bytes a certificate authority would read back from that file.
const ACME_TOKEN_BODY: &str = "maran-acme-challenge-ok";

/// Writes `contents` into the account's own site directory, as the account.
///
/// The same file operation the panel drives, so the file's owner and mode are
/// a customer's and not root's — a root-written file would be readable by
/// nginx for a reason no customer's file has.
fn write_as_account(account: &PolygonAccount, path: &str, contents: &str) {
    let written = within("the page write", {
        let name = account.name().clone();
        let path = RelativePath::parse(path).expect("the page path must be valid");
        let contents = contents.as_bytes().to_vec();
        let length = contents.len() as u64;
        move || {
            (
                maran_ops::files::write_file(
                    &ProcessFilesHost::new(),
                    &WriteFileInput {
                        account: name,
                        path,
                        contents,
                        mode: FileMode::parse(0o644).expect("a plain permission mode"),
                    },
                ),
                length,
            )
        }
    });

    assert_eq!(written.0, Ok(written.1));
}

/// Creates a static site for `account` at `domain`, serving its own page.
fn served_polygon_site(account: &PolygonAccount, domain: &Domain) -> CreateSiteInput {
    let input = CreateSiteInput {
        account: account.name().clone(),
        domain: domain.clone(),
        aliases: Vec::new(),
        kind: SiteKind::Static,
        certificate: None,
    };
    create_polygon_site(&ProcessSiteHost::new(), &input)
        .unwrap_or_else(|error| panic!("creating a static site must succeed: {error}"));

    write_as_account(
        account,
        &format!("sites/{}/index.html", domain.as_str()),
        SUSPENDED_ACCOUNT_BODY,
    );

    input
}

#[test]
#[ignore = "creates a real account and site and asks a real nginx for it: polygon only"]
fn a_suspended_accounts_site_answers_the_stub_and_not_the_customers_page() {
    // P1. Observed with an HTTP REQUEST and never by grepping the vhost: a
    // directive in the wrong block reads identically and does nothing, which is
    // the argument `sftp_on_a_real_host.rs` already makes for the chroot. The
    // reload is real here — the polygon's systemctl stand-in turns `reload
    // nginx` into `nginx -s reload` when a master is running, while `start` and
    // `stop` do nothing at all, so nothing in this suite may rest on those.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysuspone");
    let domain = Domain::parse("suspone.example.test").expect("a valid domain");
    let input = served_polygon_site(&account, &domain);
    let _vhost = PolygonConfigFile::at(&SitePaths::for_site(account.name(), &domain).config_path);

    reload_polygon_nginx();
    let before = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENDED_ACCOUNT_BODY)
    });
    // The inverse control. Without it this test would pass against a site that
    // was never served at all, which is the state that most resembles success.
    assert!(
        before.contains(SUSPENDED_ACCOUNT_BODY),
        "the site must be serving the customer's page BEFORE it is suspended:\n{before}"
    );

    maran_ops::sites::disable_site(&ProcessSiteHost::new(), polygon_distro(), &input)
        .unwrap_or_else(|error| panic!("disabling the site must succeed: {error}"));

    let after = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENSION_NOTICE)
    });

    assert!(
        after.starts_with("HTTP/1.1 403 "),
        "a suspended site must answer the refusal, not the account's page:\n{after}"
    );
    assert!(after.contains(SUSPENSION_NOTICE));
    assert!(
        !after.contains(SUSPENDED_ACCOUNT_BODY),
        "the customer's own page must not be reachable while suspended:\n{after}"
    );
}

#[test]
#[ignore = "creates a real account and site and asks a real nginx for it: polygon only"]
fn a_suspended_site_still_answers_the_acme_challenge_so_its_certificate_can_renew() {
    // P2. The promise the suspended template makes, and the one that costs the
    // customer a certificate if it is broken: a suspended account whose renewal
    // fails comes back weeks later with an expired certificate and nothing in a
    // log to say why. Asked over HTTP, because "the location block is in the
    // file" and "a request under that prefix is answered" are different claims.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysusptwo");
    let domain = Domain::parse("susptwo.example.test").expect("a valid domain");
    let input = served_polygon_site(&account, &domain);
    let _vhost = PolygonConfigFile::at(&SitePaths::for_site(account.name(), &domain).config_path);

    let challenge_directory = SitePaths::for_site(account.name(), &domain)
        .document_root
        .join(".well-known")
        .join("acme-challenge");
    ProcessSiteHost::new()
        .create_directories_as_account(account.name(), &[challenge_directory.as_path()])
        .expect("the challenge directory is created as the account");
    write_as_account(
        &account,
        &format!(
            "sites/{}/.well-known/acme-challenge/{ACME_TOKEN}",
            domain.as_str()
        ),
        ACME_TOKEN_BODY,
    );

    reload_polygon_nginx();
    // The inverse control, and it is load-bearing here for a second reason: a
    // static site answers a file under `/.well-known/` perfectly well WITHOUT
    // being suspended, so an assertion that only fetched the challenge would
    // pass against the site's own vhost and prove nothing about the stub. The
    // first version of this test did exactly that and was green while the root
    // was still serving the customer's page.
    let before = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENDED_ACCOUNT_BODY)
    });
    assert!(
        before.contains(SUSPENDED_ACCOUNT_BODY),
        "the site must be serving before it is suspended:\n{before}"
    );

    maran_ops::sites::disable_site(&ProcessSiteHost::new(), polygon_distro(), &input)
        .unwrap_or_else(|error| panic!("disabling the site must succeed: {error}"));
    reload_polygon_nginx();

    // The suspension has to be OBSERVED live before the challenge is asked
    // for, or the challenge is being asked of whichever vhost the workers
    // still hold — `nginx -s reload` returns when the master takes the signal,
    // not when the workers answering the next connection have been replaced.
    let root = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENSION_NOTICE)
    });
    assert!(
        root.starts_with("HTTP/1.1 403 "),
        "the stub must be live before the challenge proves anything:\n{root}"
    );

    let challenge = fetch(
        domain.as_str(),
        &format!("/.well-known/acme-challenge/{ACME_TOKEN}"),
    );

    assert!(
        challenge.starts_with("HTTP/1.1 200 "),
        "a suspended site must still answer the ACME challenge:\n{challenge}"
    );
    assert!(challenge.contains(ACME_TOKEN_BODY));
}

#[test]
#[ignore = "creates a real account and two real sites: polygon only"]
fn resuming_restores_the_site_that_was_resumed_and_leaves_the_other_one_stubbed() {
    // P3, and it is the reversal law: suspension MUST NOT overwrite state that
    // also expresses a customer choice. The panel re-enables only the sites
    // whose status it recorded as Enabled, so the agent must put back exactly
    // the site it is asked for and no other. This is the assertion that fails
    // if anyone "simplifies" the resume into enable-everything-for-the-account.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysuspthree");
    let resumed_domain = Domain::parse("resumed.example.test").expect("a valid domain");
    let chosen_domain = Domain::parse("chosen.example.test").expect("a valid domain");
    let resumed = served_polygon_site(&account, &resumed_domain);
    let chosen = served_polygon_site(&account, &chosen_domain);
    let _resumed_vhost =
        PolygonConfigFile::at(&SitePaths::for_site(account.name(), &resumed_domain).config_path);
    let _chosen_vhost =
        PolygonConfigFile::at(&SitePaths::for_site(account.name(), &chosen_domain).config_path);

    let host = ProcessSiteHost::new();
    for input in [&resumed, &chosen] {
        maran_ops::sites::disable_site(&host, polygon_distro(), input)
            .unwrap_or_else(|error| panic!("disabling must succeed: {error}"));
    }

    // Only one of them is put back — the one the panel would have recorded as
    // Enabled. The other was disabled by the customer and stays that way.
    maran_ops::sites::enable_site(&host, polygon_distro(), &resumed)
        .unwrap_or_else(|error| panic!("enabling must succeed: {error}"));
    reload_polygon_nginx();

    let back = fetch_until(resumed_domain.as_str(), "/", |body| {
        body.contains(SUSPENDED_ACCOUNT_BODY)
    });
    assert!(
        back.starts_with("HTTP/1.1 200 ") && back.contains(SUSPENDED_ACCOUNT_BODY),
        "the resumed site must serve its own page again:\n{back}"
    );

    // Polled, like every other read of a freshly reloaded nginx here: asking
    // once measures the worker-replacement race and not the site — measured,
    // not assumed, this assertion failed against a worker that predated the
    // second site's vhost entirely. Polling for the stub cannot tell "stayed
    // stubbed" from "became stubbed", and does not need to: the failure this
    // test exists to catch is a resume that put the site BACK, and a site
    // serving its own page never becomes the stub, so the poll runs out and
    // the assertion below reports the page it was served.
    let still_stubbed = fetch_until(chosen_domain.as_str(), "/", |body| {
        body.contains(SUSPENSION_NOTICE)
    });
    assert!(
        still_stubbed.starts_with("HTTP/1.1 403 "),
        "a site the customer had disabled must stay stubbed through a resume:\n{still_stubbed}"
    );
    assert!(!still_stubbed.contains(SUSPENDED_ACCOUNT_BODY));
}

#[test]
#[ignore = "creates a real account and two real sites: polygon only"]
fn the_suspension_state_answers_on_a_real_host_what_the_requests_above_observed() {
    // P6, narrowed to the site half. The attestation must be checked against
    // reality once, or it becomes a second thing that agrees with the code that
    // wrote it: `inspect_account_sites` renders the suspended vhost and compares
    // it with the file, and `disable_site` renders and writes the same text, so
    // a unit test of the pair can only ever prove they agree with each other.
    // What it cannot prove is that the text they agree on is the text a real
    // nginx reads out of the real include directory — which is what this asks.
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polysuspfour");
    let stubbed_domain = Domain::parse("stubbed.example.test").expect("a valid domain");
    let serving_domain = Domain::parse("serving.example.test").expect("a valid domain");
    let stubbed = served_polygon_site(&account, &stubbed_domain);
    let _serving = served_polygon_site(&account, &serving_domain);
    let _stubbed_vhost =
        PolygonConfigFile::at(&SitePaths::for_site(account.name(), &stubbed_domain).config_path);
    let _serving_vhost =
        PolygonConfigFile::at(&SitePaths::for_site(account.name(), &serving_domain).config_path);

    let host = ProcessSiteHost::new();
    maran_ops::sites::disable_site(&host, polygon_distro(), &stubbed)
        .unwrap_or_else(|error| panic!("disabling must succeed: {error}"));

    let observed = maran_ops::sites::inspect_account_sites(&host, account.name())
        .unwrap_or_else(|error| panic!("the sites must be observable: {error}"));

    assert!(
        observed.directory_readable,
        "the real include directory must be listable, or the answer is blind"
    );
    let fact = |domain: &Domain| {
        observed
            .sites
            .iter()
            .find(|fact| fact.domain == domain.as_str())
            .unwrap_or_else(|| {
                panic!(
                    "the account's own vhosts must be found: {:?}",
                    observed.sites
                )
            })
    };

    assert!(
        fact(&stubbed_domain).serving_stub,
        "the disabled site's real vhost IS the suspended render"
    );
    assert!(
        !fact(&serving_domain).serving_stub,
        "the untouched site is still serving its own content"
    );
}

// --------------------------------------------------------------------------
// F-1: the web server's logs, and the escalation that came of putting them in
// a directory the customer owns.
//
// These four tests are the attack, not a restatement of the fix. A site's
// access and error logs used to be `/home/<account>/logs/<domain>.access.log`,
// in a directory created by a child that had dropped to the account — so the
// CUSTOMER owned it. nginx's master process runs as root and opens every
// `access_log`/`error_log` target `O_WRONLY|O_APPEND|O_CREAT` with no
// `O_NOFOLLOW`, so a symbolic link planted there made root create and append to
// any path on the host, with content the customer chose (a log line embeds the
// request target). It was proved against `/etc/ld.so.preload`, after which the
// dynamic loader honoured an attacker-chosen path in every process started
// afterwards. See docs/superpowers/notes/2026-09-09-site-logs-threat-note.md.
// --------------------------------------------------------------------------

/// The file the attack tries to make root create.
///
/// `/etc/ld.so.preload` is what the original probe used and what a real attacker
/// would use, and it is deliberately NOT what these tests use. A run that fails
/// — which is the run this suite exists to produce against unfixed code — would
/// leave a live `ld.so.preload` behind in the polygon image, naming a path
/// inside a deleted account's home, and every process started in that container
/// afterwards would carry the loader's complaint. The escalation is proved by
/// root creating a file it had no business creating; WHICH file is irrelevant to
/// the proof and very relevant to the blast radius of a red test.
///
/// Under `/etc` because that is where the real targets live: it must be a
/// directory root can write and the account cannot, so that the file appearing
/// can only be root's doing.
const LOG_ESCALATION_WITNESS: &str = "/etc/maran-site-log-escalation-witness";

/// The witness for the second half of the attack: a link planted at the NEW
/// location, which only root can do.
const TAIL_ESCALATION_WITNESS: &str = "/etc/maran-site-log-tail-witness";

/// The mode every level of the site-log tree must carry.
const SITE_LOG_TREE_MODE: u32 = 0o750;

/// Removes a path when the test ends, whether it passed or panicked.
///
/// Not [`PolygonConfigFile`], which reports a removal failure and moves on:
/// these paths are the OUTCOME of an attack, and a test that left one behind
/// would hand the next test in this shared tree a file whose presence means
/// "root was tricked". A missing file is success here, so the removal is
/// allowed to find nothing.
struct PolygonPath {
    /// The path to take away again.
    path: std::path::PathBuf,
}

impl PolygonPath {
    /// Takes responsibility for `path`, and removes anything already there.
    ///
    /// The removal at construction is the point: every assertion below is "this
    /// path does not exist", which a leftover from a previous run would make
    /// FAIL rather than silently pass — the safe direction, but a confusing
    /// one. Starting from a known-absent path means the only thing that can
    /// create it is the run under way.
    ///
    /// # Panics
    ///
    /// Panics when a leftover exists and cannot be removed, since every later
    /// assertion in the test would then be meaningless.
    fn claim(path: impl AsRef<Path>) -> Self {
        let path = path.as_ref().to_path_buf();
        match std::fs::remove_file(&path) {
            Ok(()) => {}
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => panic!("a leftover at {path:?} must be removable: {error}"),
        }
        // `symlink_metadata`, so a symbolic link is seen as present rather than
        // followed to a target that may not exist.
        assert!(
            std::fs::symlink_metadata(&path).is_err(),
            "the witness {path:?} must not exist before the attack runs"
        );
        Self { path }
    }

    /// Takes responsibility for `path` WITHOUT removing what is there.
    ///
    /// The other constructor exists to guarantee a path is absent before a test
    /// asserts that nothing created it; this one exists for the opposite case —
    /// a path the test has just deliberately planted something at. Using
    /// [`Self::claim`] for that removes the plant, and the test then observes a
    /// tail finding no log at all: a silent pass on a `LogUnreadable`
    /// assertion, or — as it actually did here — a thirty-second timeout while
    /// the follow loop politely waited for a file that would never appear.
    fn guard(path: impl AsRef<Path>) -> Self {
        Self {
            path: path.as_ref().to_path_buf(),
        }
    }

    /// The path, for the assertions.
    fn path(&self) -> &Path {
        &self.path
    }
}

impl Drop for PolygonPath {
    /// Removes the path, tolerating its absence, which is the expected state.
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

/// Plants a symbolic link at `link` pointing to `target`, AS `account`.
///
/// Through `fork_as_account`, which is the agent's own privilege drop, so what
/// this proves is precisely what an unprivileged hosting customer can do with a
/// shell: nothing here runs as root after the fork. Planting the link as root
/// with `std::os::unix::fs::symlink` would prove nothing about the attack, only
/// about the reader.
///
/// # Panics
///
/// Panics when the drop or the `symlink` call fails.
fn symlink_as_account(account: &PolygonAccount, target: &str, link: std::path::PathBuf) {
    let ids = account.ids();
    let target = target.to_owned();
    let planted = within("the customer's symlink", move || {
        fork_as_account(&ids, || {
            // The customer's own directory, made by the customer: exactly the
            // state `create_site` used to leave behind, and the state a customer
            // can restore at any time with one `mkdir`.
            if let Some(parent) = link.parent() {
                std::fs::create_dir_all(parent).map_err(|_| PrivError::WorkFailed)?;
            }
            let _ = std::fs::remove_file(&link);
            std::os::unix::fs::symlink(&target, &link).map_err(|_| PrivError::WorkFailed)
        })
    });

    planted.expect("an unprivileged account can always symlink inside its own home");
}

/// Fails the test unless `path` is a real directory owned by root with
/// [`SITE_LOG_TREE_MODE`].
///
/// `symlink_metadata` and not `metadata`: a symbolic link to a root-owned `0750`
/// directory would satisfy every check made on the followed target while being
/// exactly the thing this whole change exists to refuse.
///
/// # Panics
///
/// Panics when the path is missing, is not a directory, or has the wrong owner
/// or mode.
fn assert_root_owned_directory(path: &Path) {
    let metadata = std::fs::symlink_metadata(path)
        .unwrap_or_else(|error| panic!("{path:?} must exist: {error}"));

    assert!(
        metadata.is_dir(),
        "{path:?} must be a real directory and not a symbolic link to one"
    );
    assert_eq!(
        std::os::unix::fs::MetadataExt::uid(&metadata),
        0,
        "{path:?} must be owned by root: the nginx master appends to files below it"
    );
    assert_eq!(
        std::os::unix::fs::MetadataExt::gid(&metadata),
        0,
        "{path:?} must be group root, so even the panel uid matches only `other`"
    );
    assert_eq!(
        metadata.permissions().mode() & 0o777,
        SITE_LOG_TREE_MODE,
        "{path:?} must be 0{SITE_LOG_TREE_MODE:o}"
    );
}

/// A sink that keeps what it was handed and always claims to be listening.
#[derive(Default)]
struct CollectingSink {
    /// Every line delivered.
    lines: Vec<String>,
}

impl maran_ops::sites::LogSink for CollectingSink {
    /// Keeps the line and accepts it.
    fn line(&mut self, line: &str, _historical: bool) -> Result<(), maran_ops::sites::TailEnd> {
        self.lines.push(line.to_owned());
        Ok(())
    }

    /// Always listening: nothing here decides to stop a tail.
    fn is_listening(&mut self) -> bool {
        true
    }
}

#[test]
#[ignore = "plants a real symlink as a real account and reloads a real nginx: polygon only"]
fn a_customers_symlink_where_the_logs_used_to_live_no_longer_reaches_root() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let witness = PolygonPath::claim(LOG_ESCALATION_WITNESS);
    let account = PolygonAccount::create("polysitelogone");
    let domain = Domain::parse("logattack.example.test").expect("a valid domain");

    let input = served_polygon_site(&account, &domain);
    let paths = SitePaths::for_site(account.name(), &domain);
    let _vhost = PolygonConfigFile::at(&paths.config_path);
    let old_directory = account.home().join("logs");

    // THE ATTACK. As the account, unprivileged, in the account's own home:
    // re-create the directory `create_site` used to make for it and point the
    // name the root nginx master used to open at a file only root can create.
    symlink_as_account(
        &account,
        witness.path().to_str().expect("the witness path is UTF-8"),
        old_directory.join(format!("{}.access.log", domain.as_str())),
    );

    // The trigger a customer actually has: any site operation, on any tenant,
    // ends in a reload of the one nginx that serves them all. A second site is
    // the most ordinary one there is.
    let second = Domain::parse("logattack-two.example.test").expect("a valid domain");
    let second_paths = SitePaths::for_site(account.name(), &second);
    let _second_vhost = PolygonConfigFile::at(&second_paths.config_path);
    create_polygon_site(
        &ProcessSiteHost::new(),
        &served_site_input(&account, &second),
    )
    .unwrap_or_else(|error| panic!("the second site must be created: {error}"));
    reload_polygon_nginx();

    // A request, so the master has something to write. This is also half of the
    // positive control: a 200 carrying the page proves the RUNNING master is
    // serving the vhost under test, which is the only way to know the reload
    // above was a reload and not a no-op. `nginx -T` would not do — it starts a
    // NEW nginx to dump the configuration and says nothing about what the
    // master already running has loaded.
    let served = fetch_until(domain.as_str(), "/", |response| {
        response.contains(SUSPENDED_ACCOUNT_BODY)
    });
    assert!(
        served.contains(SUSPENDED_ACCOUNT_BODY),
        "the running nginx must be serving this vhost, or nothing below is observing a reload; \
         nginx answered:\n{served}"
    );

    // THE CLAIM, and it is asserted FIRST among the three that follow.
    //
    // The order is deliberate and was arrived at by getting it wrong. The vhost
    // assertions below used to come before the attack, on the reasoning that a
    // failure there reads as "the paths did not move" rather than leaving the
    // reader to infer it. What that actually bought was a test that, against
    // unfixed code, panicked on a rendered string and NEVER PERFORMED THE
    // ATTACK — so the run that was supposed to demonstrate the escalation
    // demonstrated a text mismatch instead. A regression test for a privilege
    // escalation must fail by exhibiting the escalation; anything else is a
    // check on a template.
    assert!(
        std::fs::symlink_metadata(witness.path()).is_err(),
        "root created {:?} by following a symbolic link a hosting customer planted in their own \
         home. This is the F-1 escalation: the customer chooses the target and, because a log \
         line embeds the request target, its contents.",
        witness.path()
    );

    // The other half of the positive control: root DID write a log, at the new
    // location. Without this, "the witness was not created" is equally
    // explained by nginx never writing a log line at all — which is exactly how
    // a test that proves nothing passes. It comes AFTER the claim so that a
    // failing run names the escalation first, and it is not weakened by the
    // order: both assertions run in every passing run.
    let written = read_until_non_empty(&paths.access_log);
    assert!(
        written.contains("GET /"),
        "the access log at {:?} must carry the request that was just served, has:\n{written}",
        paths.access_log
    );

    // The fix as the vhost states it, and the ancestor chain the containment
    // actually rests on.
    let rendered = std::fs::read_to_string(&paths.config_path).expect("the vhost must be readable");
    assert!(
        !rendered.contains(&old_directory.display().to_string()),
        "no directive may name a log directory inside the account's home:\n{rendered}"
    );
    assert!(
        rendered.contains(&format!("access_log {};", paths.access_log.display())),
        "the vhost must write its access log under the root-owned tree:\n{rendered}"
    );
    assert!(
        paths.access_log.starts_with(AgentPaths::SITE_LOG_ROOT),
        "the access log must be under {}, is {:?}",
        AgentPaths::SITE_LOG_ROOT,
        paths.access_log
    );
    assert_root_owned_directory(Path::new(AgentPaths::SITE_LOG_ROOT));
    assert_root_owned_directory(&paths.log_directory);
    drop(input);
}

/// The input for a static site that serves its own page, without creating it.
///
/// Split out of [`served_polygon_site`] so a test can name a second site's input
/// and create it itself; that function both builds and creates, which is what
/// most callers want and not what the attack above needs.
fn served_site_input(account: &PolygonAccount, domain: &Domain) -> CreateSiteInput {
    CreateSiteInput {
        account: account.name().clone(),
        domain: domain.clone(),
        aliases: Vec::new(),
        kind: SiteKind::Static,
        certificate: None,
    }
}

/// How long to wait for nginx to flush a log line before giving up.
const LOG_DEADLINE: Duration = Duration::from_secs(10);

/// Reads `path` until it has content or the deadline passes, returning whatever
/// it last saw.
///
/// Bounded and non-panicking, for the reason [`fetch_until`] is: an empty result
/// must produce the assertion at the call site, with the path named, rather than
/// a timeout message that says nothing about what was or was not written.
fn read_until_non_empty(path: &Path) -> String {
    let deadline = std::time::Instant::now() + LOG_DEADLINE;
    let mut last = std::fs::read_to_string(path).unwrap_or_default();

    while last.is_empty() && std::time::Instant::now() < deadline {
        std::thread::sleep(SERVE_POLL_INTERVAL);
        last = std::fs::read_to_string(path).unwrap_or_default();
    }

    last
}

#[test]
#[ignore = "creates a real account and inspects the real log tree: polygon only"]
fn every_directory_the_site_log_tree_is_made_of_belongs_to_root() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysitelogtwo");
    let domain = Domain::parse("logmodes.example.test").expect("a valid domain");

    let paths = SitePaths::for_site(account.name(), &domain);
    let _vhost = PolygonConfigFile::at(&paths.config_path);
    create_polygon_site(
        &ProcessSiteHost::new(),
        &served_site_input(&account, &domain),
    )
    .unwrap_or_else(|error| panic!("creating the site must succeed: {error}"));

    // Both levels the fix creates, and the mode is asserted rather than assumed
    // from the umask the daemon happened to inherit — which is the whole reason
    // the implementation uses `DirBuilder::mode` and not `create_dir_all`.
    assert_root_owned_directory(Path::new(AgentPaths::SITE_LOG_ROOT));
    assert_root_owned_directory(&paths.log_directory);

    // The account, which is what the containment is against, owns nothing here.
    let owned = std::fs::symlink_metadata(&paths.log_directory).expect("the directory must exist");
    assert_ne!(
        std::os::unix::fs::MetadataExt::uid(&owned),
        account.ids().uid(),
        "the account must not own the directory the root nginx master writes into"
    );
    assert_ne!(
        std::os::unix::fs::MetadataExt::gid(&owned),
        account.ids().gid(),
        "nor may its group reach it"
    );

    // And the document root is still the account's: the fix moved the logs and
    // must not have moved anything else out of the home.
    let root = std::fs::metadata(&paths.document_root).expect("the document root must exist");
    assert_eq!(
        std::os::unix::fs::MetadataExt::uid(&root),
        account.ids().uid(),
        "the document root is still a customer path, created as the customer"
    );
    assert!(
        paths.document_root.starts_with(account.home()),
        "the document root must still be inside the account's home"
    );
}

#[test]
#[ignore = "plants a real symlink at the root-owned log path: polygon only"]
fn a_symlink_planted_at_the_new_log_location_is_refused_by_the_tail() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let witness = PolygonPath::claim(TAIL_ESCALATION_WITNESS);
    let account = PolygonAccount::create("polysitelogthree");
    let domain = Domain::parse("logtail.example.test").expect("a valid domain");

    let paths = SitePaths::for_site(account.name(), &domain);
    let _vhost = PolygonConfigFile::at(&paths.config_path);
    create_polygon_site(
        &ProcessSiteHost::new(),
        &served_site_input(&account, &domain),
    )
    .unwrap_or_else(|error| panic!("creating the site must succeed: {error}"));

    // Planted as ROOT, and the fact that it has to be is the point: the tree is
    // `root:root 0750`, so no hosting account can reach this name at all. What
    // is under test is the tail's own `O_NOFOLLOW`, which is now defence in
    // depth rather than the containment — and defence in depth that no test can
    // observe is decoration, so the test manufactures the only state that
    // exercises it.
    let _ = std::fs::remove_file(&paths.access_log);
    std::os::unix::fs::symlink(witness.path(), &paths.access_log)
        .expect("root may plant a link in its own directory");
    // `guard`, NOT `claim`: `claim` would remove the link that was just planted
    // and leave the tail with no log at all, which is not a refusal — it is the
    // ordinary state of a site that has served no request, and the follow loop
    // would wait for one.
    let _planted = PolygonPath::guard(&paths.access_log);

    let name = account.name().clone();
    let tailed = domain.clone();
    // Bounded, for the reason the privileges suite bounds its tail: a refusal
    // that stopped happening would make this call wait out the follow loop's
    // five-minute idle ceiling instead of failing.
    let (refusal, delivered) = within("the tail of a planted symlink", move || {
        let mut sink = CollectingSink::default();
        let outcome = maran_ops::sites::tail_site_log(
            &ProcessSiteHost::new(),
            &name,
            &tailed,
            maran_ops::sites::SiteLogKind::Access,
            10,
            &mut sink,
        );
        (outcome, sink.lines.len())
    });

    assert!(
        matches!(refusal, Err(SitesOpError::LogUnreadable { .. })),
        "a symbolic link at the log's own name must be refused, got {refusal:?}"
    );
    assert_eq!(delivered, 0, "not one line may come back through a symlink");
    assert!(
        std::fs::symlink_metadata(witness.path()).is_err(),
        "the tail must not have created {:?} by following the link",
        witness.path()
    );
}

#[test]
#[ignore = "writes a real suspended vhost: polygon only"]
fn a_suspended_sites_logs_move_with_the_serving_ones() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysitelogfour");
    let domain = Domain::parse("logsuspend.example.test").expect("a valid domain");

    let paths = SitePaths::for_site(account.name(), &domain);
    let _vhost = PolygonConfigFile::at(&paths.config_path);
    let input = served_polygon_site(&account, &domain);

    maran_ops::sites::disable_site(&ProcessSiteHost::new(), polygon_distro(), &input)
        .unwrap_or_else(|error| panic!("suspending the site must succeed: {error}"));

    // The suspended render is a DIFFERENT template with the same two directives,
    // and the only thing keeping the two in step is that both take their paths
    // from one `SitePaths`. That is the property worth asserting: a future
    // change that duplicated the derivation would leave this vhost pointing into
    // the home while every serving vhost had moved.
    let rendered = std::fs::read_to_string(&paths.config_path).expect("the vhost must be readable");
    assert!(
        rendered.contains(SUSPENSION_NOTICE),
        "this must be the suspended render:\n{rendered}"
    );
    assert!(
        !rendered.contains(&account.home().join("logs").display().to_string()),
        "a suspended site must not log into the account's home either:\n{rendered}"
    );
    assert!(
        rendered.contains(&format!("access_log {};", paths.access_log.display())),
        "the suspended vhost must log where the serving one did:\n{rendered}"
    );
    assert_valid_nginx_tree("the suspended vhost with the moved logs");
    assert_nginx_loads(&paths.config_path);
}

/// A [`SiteHost`] that does exactly what the real one does, with one hook
/// around the vhost write so two operations can be made to overlap on purpose.
///
/// Every method delegates to [`ProcessSiteHost`]; the only addition is
/// `before_write`, run immediately before the config-write protocol is
/// entered. That is the one point where the two racing threads have to be
/// lined up: it is after everything an operation does on its own (the document
/// root, the log directory, the render) and before the moment the protocol
/// makes the tree everyone's.
struct RacingSiteHost {
    inner: ProcessSiteHost,
    before_write: Box<dyn Fn() + Send + Sync>,
}

impl SiteHost for RacingSiteHost {
    fn read_config(&self, path: &Path) -> Result<Option<String>, SitesOpError> {
        self.inner.read_config(path)
    }

    fn list_config_paths(&self) -> Result<Vec<std::path::PathBuf>, SitesOpError> {
        self.inner.list_config_paths()
    }

    fn create_site_log_directory(
        &self,
        account: &maran_agent_core::validation::system::name::AccountName,
    ) -> Result<(), SitesOpError> {
        self.inner.create_site_log_directory(account)
    }

    fn create_directories_as_account(
        &self,
        account: &maran_agent_core::validation::system::name::AccountName,
        directories: &[&Path],
    ) -> Result<(), SitesOpError> {
        self.inner
            .create_directories_as_account(account, directories)
    }

    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &maran_ops::safe_write::model::Validator<'_>,
        reload: &maran_ops::safe_write::model::Reload<'_>,
    ) -> Result<(), SitesOpError> {
        (self.before_write)();
        self.inner.write_config(target, contents, validator, reload)
    }

    fn create_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &maran_ops::safe_write::model::Validator<'_>,
        reload: &maran_ops::safe_write::model::Reload<'_>,
        refused_warnings: &[String],
    ) -> Result<(), SitesOpError> {
        (self.before_write)();
        self.inner
            .create_config(target, contents, validator, reload, refused_warnings)
    }

    fn remove_config(
        &self,
        target: &Path,
        validator: &maran_ops::safe_write::model::Validator<'_>,
        reload: &maran_ops::safe_write::model::Reload<'_>,
    ) -> Result<(), SitesOpError> {
        self.inner.remove_config(target, validator, reload)
    }

    fn resolve_in_account_home(
        &self,
        account: &maran_agent_core::validation::system::name::AccountName,
        relative: &Path,
    ) -> Result<std::path::PathBuf, SitesOpError> {
        self.inner.resolve_in_account_home(account, relative)
    }
}

/// How long the victim thread waits to be told the interfering write has
/// entered the protocol.
///
/// Generous, because it is not a timing assertion: it is the budget for one
/// thread to reach one function call. The test fails loudly if it expires,
/// rather than proceeding with two operations that never overlapped.
const RACE_BUDGET: Duration = Duration::from_secs(30);

/// A static site owned by `account` at `domain`.
///
/// Static and not PHP on purpose: a PHP site writes its pool through the same
/// protocol, so it enters the config-write protocol twice, and the hook this
/// race lines the threads up on would fire on the wrong one.
fn static_site(account: &PolygonAccount, domain: &Domain) -> CreateSiteInput {
    CreateSiteInput {
        account: account.name().clone(),
        domain: domain.clone(),
        aliases: Vec::new(),
        kind: SiteKind::Static,
        certificate: None,
    }
}

/// Creates a static site through `host` — the real php host is still handed
/// over, because a static site must not touch it and this proves it does not.
fn create_racing_site(
    host: &RacingSiteHost,
    input: &CreateSiteInput,
) -> Result<maran_ops::sites::CreatedSite, SitesOpError> {
    create_site(
        host,
        &ProcessPhpHost::new(),
        polygon_distro(),
        input,
        POLYGON_WORKERS,
        &[],
    )
}

#[test]
#[ignore = "runs two real vhost writes against one real nginx at the same time: polygon only"]
fn a_neighbours_rejected_vhost_cannot_fail_this_tenants_valid_write() {
    // The first of the three interleavings `ops::safe_write::config_tree_lock`
    // documents, driven rather than described. `nginx -t` carries no
    // file argument, so it answers about the WHOLE host: while one tenant's
    // rejected vhost is renamed in and not yet rolled back, an unrelated
    // tenant's valid write used to validate at exit 1, be rolled back, and be
    // reported to its owner as invalid.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();

    let neighbour = PolygonAccount::create("polyracenbr");
    let tenant = PolygonAccount::create("polyracetnt");

    // The interferer's domain is entirely legitimate to the panel — 253
    // characters in labels of up to 63 — and the real nginx refuses it,
    // because a `server_name` longer than its `server_names_hash_bucket_size`
    // cannot be hashed. So its content really is live and really is invalid
    // for the length of one real `nginx -t`.
    let refused_domain = Domain::parse(&format!("{}.{}.race.test", "a".repeat(63), "b".repeat(63)))
        .expect("a long domain is still a valid domain");
    let refused_vhost = SitePaths::for_site(neighbour.name(), &refused_domain).config_path;

    let good_domain = Domain::parse("racewinner.example.test").expect("a valid domain");
    let good_vhost = SitePaths::for_site(tenant.name(), &good_domain).config_path;
    let _good = PolygonConfigFile::at(&good_vhost);

    let (entered, entry) = mpsc::channel();
    let entry = std::sync::Mutex::new(entry);
    let overlapped = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false));

    let neighbour_input = static_site(&neighbour, &refused_domain);
    let neighbour_thread = std::thread::spawn(move || {
        let host = RacingSiteHost {
            inner: ProcessSiteHost::new(),
            before_write: Box::new(move || {
                entered
                    .send(())
                    .expect("the victim thread is still listening");
            }),
        };
        create_racing_site(&host, &neighbour_input)
    });

    let tenant_input = static_site(&tenant, &good_domain);
    let tenant_thread = {
        let overlapped = std::sync::Arc::clone(&overlapped);
        std::thread::spawn(move || {
            let host = RacingSiteHost {
                inner: ProcessSiteHost::new(),
                before_write: Box::new(move || {
                    // Behind a mutex only because the hook is a `Fn` shared
                    // across threads by its type; one thread ever calls it.
                    let heard = entry
                        .lock()
                        .expect("the fixture lock is never poisoned")
                        .recv_timeout(RACE_BUDGET)
                        .is_ok();
                    overlapped.store(heard, std::sync::atomic::Ordering::SeqCst);
                }),
            };
            create_racing_site(&host, &tenant_input)
        })
    };

    let neighbour_result = neighbour_thread.join().expect("the neighbour must finish");
    let tenant_result = tenant_thread.join().expect("the tenant must finish");

    // The positive control, and it is two claims, not one. First: the
    // interferer really ran and really produced the state this test is about —
    // the real nginx really refused its vhost. A run in which that write
    // succeeded, or never happened, would make every assertion below vacuous.
    assert!(
        matches!(neighbour_result, Err(SitesOpError::NginxValidation { .. })),
        "the interfering write must be refused by the real nginx, got {neighbour_result:?}"
    );
    // Second: the two really overlapped. The tenant's write entered the
    // protocol only AFTER the neighbour's had, which is the interleaving; a
    // test where the second operation never started proves nothing.
    assert!(
        overlapped.load(std::sync::atomic::Ordering::SeqCst),
        "the tenant's write must have started after the neighbour's entered the protocol"
    );

    // The finding itself: the valid write is accepted, on a host where an
    // invalid vhost belonging to a different customer was in flight.
    let created = tenant_result
        .unwrap_or_else(|error| panic!("a valid write must not be failed by a neighbour: {error}"));
    assert_eq!(
        std::path::Path::new(&created.config_path),
        good_vhost.as_path()
    );
    assert!(good_vhost.exists(), "the tenant's vhost must be on disk");

    // And the neighbour's rejected content is gone, so the tree it briefly
    // made invalid is valid again and nginx really reads the tenant's file.
    assert!(
        !refused_vhost.exists(),
        "a refused site must leave no vhost behind at {refused_vhost:?}"
    );
    assert_valid_nginx_tree("the tree after the race");
    assert_nginx_loads(&good_vhost);
}

#[test]
#[ignore = "writes a real vhost and real certificate material: polygon only"]
fn installing_a_certificate_on_a_suspended_site_leaves_the_real_nginx_serving_the_stub() {
    // C-1 interleaving 2's harm, asked of a real nginx over HTTP. The renewal
    // scheduler runs unattended, so a certificate installation that re-rendered
    // the site's own vhost put a suspended customer's page back on the air with
    // nothing to say so — and "the vhost file says stub" and "a request is
    // answered by the stub" are different claims.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polysuspcert");
    let domain = Domain::parse("suspcert.example.test").expect("a valid domain");
    let input = served_polygon_site(&account, &domain);
    let vhost = SitePaths::for_site(account.name(), &domain).config_path;
    let _vhost = PolygonConfigFile::at(&vhost);

    // The inverse control: the site really is serving the customer's page
    // before any of this, so the stub below is a change and not a starting
    // state.
    reload_polygon_nginx();
    let before = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENDED_ACCOUNT_BODY)
    });
    assert!(
        before.contains(SUSPENDED_ACCOUNT_BODY),
        "the site must be serving the customer's page before it is suspended:\n{before}"
    );

    maran_ops::sites::disable_site(&ProcessSiteHost::new(), polygon_distro(), &input)
        .unwrap_or_else(|error| panic!("suspending the site must succeed: {error}"));
    let suspended = std::fs::read_to_string(&vhost).expect("the stub must be readable");

    // Real openssl, real material, through the same operation the renewal
    // scheduler drives.
    let ssl_host = ProcessSslHost::new();
    generate_self_signed(&ssl_host, polygon_distro(), &input).unwrap_or_else(|error| {
        panic!("issuing material for a suspended site must succeed: {error}")
    });

    let material = SiteCertificate::for_domain(&domain);
    assert!(
        material.certificate_path().exists(),
        "the material must still be placed, so a suspended site can renew"
    );
    assert_eq!(
        std::fs::read_to_string(&vhost).expect("the stub must still be readable"),
        suspended,
        "a certificate installation must not rewrite a suspended site's vhost"
    );

    assert_valid_nginx_tree("the tree after a certificate landed on a suspended site");
    let after = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENSION_NOTICE)
    });
    assert!(
        after.starts_with("HTTP/1.1 403 "),
        "a suspended site must still answer the refusal after a renewal:\n{after}"
    );
    assert!(
        !after.contains(SUSPENDED_ACCOUNT_BODY),
        "a certificate renewal must not put a suspended customer's page back on the air:\n{after}"
    );

    maran_ops::ssl::purge_certificate(&ssl_host, polygon_distro(), &domain);
}

// ---------------------------------------------------------------------------
// Symbolic links a customer plants in a site's own tree (#84).
//
// The web server can enter every account's home and a customer can link to any
// path, so without `disable_symlinks if_not_owner;` a link in one site's root
// served another account's files. Every refusal below runs three controls in
// the same run, so a refusal cannot be mistaken for a site that was never
// served, or for a secret nobody could read:
//   (c1) the attacking account's own file is served;
//   (c2) the victim's file IS served on the victim's own site, so the web
//        server can read it and a refusal comes from the link rule;
//   (c3) nginx's own error log names the refused path with ELOOP.
//
// Stated blind spot: these requests are plain HTTP on port 80. The 443 block of
// a site with a certificate is held by `php_site_ssl.conf` and the other
// goldens, not over the wire: the suite has no TLS client.
// ---------------------------------------------------------------------------

/// The bytes of the victim's private file. A refusal must never contain them.
const LINK_SECRET: &str = "maran-bob-private-credentials";

/// The bytes of the attacking account's own file, the positive control.
const LINK_OWN_BODY: &str = "maran-alice-own-page";

/// What nginx's error log says about a path it refused because of a link.
const REFUSED_LINK_LOG: &str = "(40: Too many levels of symbolic links)";

/// Two accounts, each with a site: `alice` the attacker and `bob` the victim.
///
/// Bob's document root holds `wp-config.php` with [`LINK_SECRET`], written as
/// bob and readable by the web server, which is the reproduction of the issue.
/// Alice's root holds `alice.txt`, a file any kind of site serves. Fields drop
/// in declaration order, so the config files go before the accounts do.
struct LinkFixture {
    /// Takes alice's vhost away again.
    _alice_vhost: PolygonConfigFile,
    /// Takes bob's vhost away again.
    _bob_vhost: PolygonConfigFile,
    /// Takes alice's php-fpm pool away again, when her site is a PHP one.
    _alice_pool: Option<PolygonConfigFile>,
    /// The account that plants links.
    alice: PolygonAccount,
    /// The account whose files a link must not reach.
    bob: PolygonAccount,
    /// Alice's site name.
    alice_domain: Domain,
    /// Bob's site name.
    bob_domain: Domain,
    /// The input alice's site was created from.
    alice_input: CreateSiteInput,
}

impl LinkFixture {
    /// Creates both accounts and both sites, named by `tag`, which must be
    /// unique per test and at most a few letters.
    ///
    /// `php` makes alice's site a PHP one; bob's is always static, because a
    /// static site serves his file as plain bytes.
    ///
    /// # Panics
    ///
    /// Panics outside a polygon, or when an account or a site cannot be made.
    fn new(tag: &str, php: bool) -> Self {
        PolygonAccount::require_polygon();
        ensure_nginx_is_running();
        let alice = PolygonAccount::create(&format!("polylka{tag}"));
        let bob = PolygonAccount::create(&format!("polylkb{tag}"));
        let alice_domain =
            Domain::parse(&format!("alice{tag}.example.test")).expect("a valid domain");
        let bob_domain = Domain::parse(&format!("bob{tag}.example.test")).expect("a valid domain");

        let alice_input = if php {
            php_site(&alice, &alice_domain)
        } else {
            served_site_input(&alice, &alice_domain)
        };
        let alice_pool = php.then(|| pool_guard(&alice));
        create_polygon_site(&ProcessSiteHost::new(), &alice_input)
            .unwrap_or_else(|error| panic!("creating alice's site must succeed: {error}"));
        let alice_vhost =
            PolygonConfigFile::at(&SitePaths::for_site(alice.name(), &alice_domain).config_path);

        create_polygon_site(
            &ProcessSiteHost::new(),
            &served_site_input(&bob, &bob_domain),
        )
        .unwrap_or_else(|error| panic!("creating bob's site must succeed: {error}"));
        let bob_vhost =
            PolygonConfigFile::at(&SitePaths::for_site(bob.name(), &bob_domain).config_path);

        write_as_account(
            &alice,
            &format!("sites/{}/alice.txt", alice_domain.as_str()),
            LINK_OWN_BODY,
        );
        write_as_account(
            &bob,
            &format!("sites/{}/wp-config.php", bob_domain.as_str()),
            LINK_SECRET,
        );

        Self {
            _alice_vhost: alice_vhost,
            _bob_vhost: bob_vhost,
            _alice_pool: alice_pool,
            alice,
            bob,
            alice_domain,
            bob_domain,
            alice_input,
        }
    }

    /// Alice's document root.
    fn alice_root(&self) -> std::path::PathBuf {
        SitePaths::for_site(self.alice.name(), &self.alice_domain).document_root
    }

    /// Bob's document root.
    fn bob_root(&self) -> std::path::PathBuf {
        SitePaths::for_site(self.bob.name(), &self.bob_domain).document_root
    }

    /// The absolute path of bob's private file.
    fn secret_path(&self) -> String {
        self.bob_root()
            .join("wp-config.php")
            .to_str()
            .expect("the path is UTF-8")
            .to_owned()
    }

    /// Alice's site error log.
    fn alice_error_log(&self) -> std::path::PathBuf {
        SitePaths::for_site(self.alice.name(), &self.alice_domain).error_log
    }

    /// Reloads nginx and proves controls (c1) and (c2): alice's own file is
    /// served, and so is bob's secret on bob's own site.
    ///
    /// # Panics
    ///
    /// Panics when either control fails, which means the setup is wrong and a
    /// refusal measured afterwards would measure nothing.
    fn assert_controls(&self) {
        reload_polygon_nginx();

        let own = fetch_until(self.alice_domain.as_str(), "/alice.txt", |body| {
            body.contains(LINK_OWN_BODY)
        });
        assert!(
            own.starts_with("HTTP/1.1 200 ") && own.contains(LINK_OWN_BODY),
            "control c1: the attacker's own file must be served, or no refusal means anything:\n{own}"
        );

        let secret = fetch_until(self.bob_domain.as_str(), "/wp-config.php", |body| {
            body.contains(LINK_SECRET)
        });
        assert!(
            secret.starts_with("HTTP/1.1 200 ") && secret.contains(LINK_SECRET),
            "control c2: the web server must be able to read the victim's file on the \
             victim's own site, or a refusal could be a permission bit:\n{secret}"
        );
    }

    /// Asks alice's site for `path` and fails the test unless nginx answered
    /// `status` without the victim's bytes.
    ///
    /// # Panics
    ///
    /// Panics on a different status or when the secret is in the response.
    fn assert_refused(&self, path: &str, status: &str) {
        let response = fetch(self.alice_domain.as_str(), path);

        assert!(
            response.starts_with(&format!("HTTP/1.1 {status} ")),
            "a link out of alice's tree must answer {status} for {path}:\n{response}"
        );
        assert!(
            !response.contains(LINK_SECRET),
            "a link out of alice's tree served bob's private file for {path}:\n{response}"
        );
    }
}

/// Renames `from` to `to` as the account, through the agent's own privilege
/// drop, like [`symlink_as_account`]: what this proves is what an unprivileged
/// customer can do with a shell, since the directories above a document root
/// belong to the customer.
///
/// # Panics
///
/// Panics when the drop or the `rename` call fails.
fn rename_as_account(account: &PolygonAccount, from: std::path::PathBuf, to: std::path::PathBuf) {
    let ids = account.ids();
    let renamed = within("the customer's rename", move || {
        fork_as_account(&ids, || {
            std::fs::rename(&from, &to).map_err(|_| PrivError::WorkFailed)
        })
    });

    renamed.expect("an unprivileged account can always rename inside its own home");
}

/// Reads `path` until it contains `needle` or the deadline passes, returning
/// whatever it last saw.
///
/// Bounded and non-panicking for the reason [`read_until_non_empty`] is: a
/// missing line must produce the assertion at the call site with the log
/// printed, not a timeout message.
fn read_until_contains(path: &Path, needle: &str) -> String {
    let deadline = std::time::Instant::now() + LOG_DEADLINE;
    let mut last = std::fs::read_to_string(path).unwrap_or_default();

    while !last.contains(needle) && std::time::Instant::now() < deadline {
        std::thread::sleep(SERVE_POLL_INTERVAL);
        last = std::fs::read_to_string(path).unwrap_or_default();
    }

    last
}

#[test]
#[ignore = "plants real symlinks as real accounts and asks a real nginx for them: polygon only"]
fn a_link_from_a_static_site_to_another_accounts_file_is_not_served() {
    // The issue's reproduction: the secret is 0644 in bob's own root, alice
    // links to it from hers. The 404 is what `try_files` makes of a refused
    // open; it is not the 403 the issue named, and the body check is what says
    // the bytes did not leave.
    let fixture = LinkFixture::new("one", false);
    symlink_as_account(
        &fixture.alice,
        &fixture.secret_path(),
        fixture.alice_root().join("leak.txt"),
    );
    fixture.assert_controls();

    fixture.assert_refused("/leak.txt", "404");

    // (c3) the server's own word, which is also what tells a refused link from
    // a permission error: EACCES here would mean the setup is wrong.
    let log = read_until_contains(&fixture.alice_error_log(), REFUSED_LINK_LOG);
    assert!(
        log.contains(REFUSED_LINK_LOG) && log.contains("leak.txt"),
        "control c3: the error log must name the refused link with ELOOP:\n{log}"
    );
}

#[test]
#[ignore = "plants a real symlink as a real account and asks a real nginx for it: polygon only"]
fn a_link_from_a_static_site_to_a_host_file_is_not_served() {
    let fixture = LinkFixture::new("two", false);
    symlink_as_account(
        &fixture.alice,
        "/etc/passwd",
        fixture.alice_root().join("passwd.txt"),
    );
    fixture.assert_controls();

    let response = fetch(fixture.alice_domain.as_str(), "/passwd.txt");

    assert!(
        response.starts_with("HTTP/1.1 404 "),
        "a link to a host file must answer 404:\n{response}"
    );
    assert!(
        !response.contains("root:x:0:0"),
        "a customer's link served the host's /etc/passwd:\n{response}"
    );
}

#[test]
#[ignore = "plants a real symlink as a real account and asks a real nginx for it: polygon only"]
fn a_directory_link_into_another_home_is_not_followed() {
    // A directory link, not a file link: the check walks every component of the
    // request path, so a link in the middle of it is refused too.
    let fixture = LinkFixture::new("three", false);
    symlink_as_account(
        &fixture.alice,
        fixture.bob.home().to_str().expect("the path is UTF-8"),
        fixture.alice_root().join("bobhome"),
    );
    fixture.assert_controls();

    fixture.assert_refused(
        &format!(
            "/bobhome/sites/{}/wp-config.php",
            fixture.bob_domain.as_str()
        ),
        "404",
    );
}

#[test]
#[ignore = "plants real symlinks as a real account and asks a real nginx for them: polygon only"]
fn a_customers_links_to_their_own_files_are_still_served() {
    // The inverse of the refusals above, and what keeps normal sites working:
    // a link whose owner is the owner of its target is followed, wherever the
    // target lives in the customer's own home.
    let fixture = LinkFixture::new("four", false);
    write_as_account(&fixture.alice, "notes.txt", LINK_OWN_BODY);
    write_as_account(
        &fixture.alice,
        &format!("sites/{}/real.txt", fixture.alice_domain.as_str()),
        LINK_OWN_BODY,
    );
    symlink_as_account(
        &fixture.alice,
        "real.txt",
        fixture.alice_root().join("mine.txt"),
    );
    symlink_as_account(
        &fixture.alice,
        fixture
            .alice
            .home()
            .join("notes.txt")
            .to_str()
            .expect("the path is UTF-8"),
        fixture.alice_root().join("outside.txt"),
    );
    fixture.assert_controls();

    for path in ["/mine.txt", "/outside.txt"] {
        let response = fetch_until(fixture.alice_domain.as_str(), path, |body| {
            body.contains(LINK_OWN_BODY)
        });
        assert!(
            response.starts_with("HTTP/1.1 200 ") && response.contains(LINK_OWN_BODY),
            "a customer's link to their own file must still be served, {path}:\n{response}"
        );
    }
}

#[test]
#[ignore = "renames and links as a real account and asks a real nginx: polygon only"]
fn a_document_root_swapped_for_a_link_to_another_accounts_root_is_not_served() {
    // Why the rule has no `from=`. The customer owns `sites/` and everything
    // above it, so they can move their document root aside and put a link to
    // another account's root in its place. A rule that starts checking at the
    // root would open that link without looking at it.
    let fixture = LinkFixture::new("five", false);
    let root = fixture.alice_root();
    let aside = root.with_file_name(format!("{}.old", fixture.alice_domain.as_str()));
    rename_as_account(&fixture.alice, root.clone(), aside);
    symlink_as_account(
        &fixture.alice,
        fixture.bob_root().to_str().expect("the path is UTF-8"),
        root,
    );
    reload_polygon_nginx();

    // Controls c1 and c2 cannot use alice's own file here, because her root is
    // now the swapped link; c2 and the refusal itself are what this asks.
    let secret = fetch_until(fixture.bob_domain.as_str(), "/wp-config.php", |body| {
        body.contains(LINK_SECRET)
    });
    assert!(
        secret.starts_with("HTTP/1.1 200 ") && secret.contains(LINK_SECRET),
        "control c2: bob's own site must serve his file:\n{secret}"
    );

    fixture.assert_refused("/wp-config.php", "404");

    // The inverse control: a root swapped for a link to ANOTHER DIRECTORY OF
    // HER OWN is followed, because the link and its target share an owner.
    let own_target = fixture.alice.home().join("sites").join("own-target");
    write_as_account(&fixture.alice, "sites/own-target/alice.txt", LINK_OWN_BODY);
    let second_domain = Domain::parse("alicefive-two.example.test").expect("a valid domain");
    let second_paths = SitePaths::for_site(fixture.alice.name(), &second_domain);
    create_polygon_site(
        &ProcessSiteHost::new(),
        &served_site_input(&fixture.alice, &second_domain),
    )
    .unwrap_or_else(|error| panic!("the second site must be created: {error}"));
    let _second_vhost = PolygonConfigFile::at(&second_paths.config_path);
    rename_as_account(
        &fixture.alice,
        second_paths.document_root.clone(),
        second_paths
            .document_root
            .with_file_name("alicefive-two.example.test.old"),
    );
    symlink_as_account(
        &fixture.alice,
        own_target.to_str().expect("the path is UTF-8"),
        second_paths.document_root,
    );
    reload_polygon_nginx();

    let own = fetch_until(second_domain.as_str(), "/alice.txt", |body| {
        body.contains(LINK_OWN_BODY)
    });
    assert!(
        own.starts_with("HTTP/1.1 200 ") && own.contains(LINK_OWN_BODY),
        "a root swapped for a link to the customer's own directory must still be served:\n{own}"
    );
}

#[test]
#[ignore = "plants a real symlink as a real account and asks a real nginx for it: polygon only"]
fn a_link_in_the_challenge_directory_of_a_site_with_a_certificate_is_refused() {
    // With a certificate the port-80 block holds only the challenge location
    // and the redirect, so that location's own rule is the only one there. The
    // challenge location has no `try_files`, so a refusal is nginx's 403.
    let fixture = LinkFixture::new("six", false);
    // Controls c1 and c2 are asked BEFORE the certificate lands: afterwards
    // port 80 redirects everything but the challenge directory.
    fixture.assert_controls();
    let ssl_host = ProcessSslHost::new();
    generate_self_signed(&ssl_host, polygon_distro(), &fixture.alice_input)
        .unwrap_or_else(|error| panic!("issuing material for alice's site must succeed: {error}"));

    write_as_account(
        &fixture.alice,
        &format!(
            "sites/{}/.well-known/acme-challenge/{ACME_TOKEN}",
            fixture.alice_domain.as_str()
        ),
        ACME_TOKEN_BODY,
    );
    symlink_as_account(
        &fixture.alice,
        &fixture.secret_path(),
        fixture
            .alice_root()
            .join(".well-known")
            .join("acme-challenge")
            .join("leak"),
    );
    reload_polygon_nginx();

    // The control that this block really is the certificate one: the site root
    // redirects to HTTPS.
    let redirect = fetch_until(fixture.alice_domain.as_str(), "/", |body| {
        body.starts_with("HTTP/1.1 301 ")
    });
    assert!(
        redirect.starts_with("HTTP/1.1 301 "),
        "port 80 of a site with a certificate must redirect, or this probe is not on that block:\n{redirect}"
    );

    let token = fetch(
        fixture.alice_domain.as_str(),
        &format!("/.well-known/acme-challenge/{ACME_TOKEN}"),
    );
    assert!(
        token.starts_with("HTTP/1.1 200 ") && token.contains(ACME_TOKEN_BODY),
        "the real challenge file must still be answered:\n{token}"
    );

    fixture.assert_refused("/.well-known/acme-challenge/leak", "403");

    maran_ops::ssl::purge_certificate(&ssl_host, polygon_distro(), &fixture.alice_domain);
}

#[test]
#[ignore = "plants a real symlink as a real account and asks a real nginx for it: polygon only"]
fn a_link_in_a_suspended_sites_challenge_directory_is_refused() {
    // The stub's challenge location serves a customer's tree while the rest of
    // the site answers the refusal, so a link there is as open as anywhere.
    let fixture = LinkFixture::new("seven", false);
    write_as_account(
        &fixture.alice,
        &format!(
            "sites/{}/.well-known/acme-challenge/{ACME_TOKEN}",
            fixture.alice_domain.as_str()
        ),
        ACME_TOKEN_BODY,
    );
    symlink_as_account(
        &fixture.alice,
        &fixture.secret_path(),
        fixture
            .alice_root()
            .join(".well-known")
            .join("acme-challenge")
            .join("leak"),
    );
    fixture.assert_controls();

    maran_ops::sites::disable_site(
        &ProcessSiteHost::new(),
        polygon_distro(),
        &fixture.alice_input,
    )
    .unwrap_or_else(|error| panic!("suspending alice's site must succeed: {error}"));
    reload_polygon_nginx();

    // The suspension is observed live first, so what follows is asked of the
    // stub and not of the vhost the workers still held.
    let root = fetch_until(fixture.alice_domain.as_str(), "/", |body| {
        body.contains(SUSPENSION_NOTICE)
    });
    assert!(
        root.starts_with("HTTP/1.1 403 ") && root.contains(SUSPENSION_NOTICE),
        "the stub must be live before the challenge proves anything:\n{root}"
    );

    let token = fetch(
        fixture.alice_domain.as_str(),
        &format!("/.well-known/acme-challenge/{ACME_TOKEN}"),
    );
    assert!(
        token.starts_with("HTTP/1.1 200 ") && token.contains(ACME_TOKEN_BODY),
        "a suspended site must still answer its own challenge file:\n{token}"
    );

    fixture.assert_refused("/.well-known/acme-challenge/leak", "403");
}

#[test]
#[ignore = "plants a real symlink as a real account and asks a real nginx for it: polygon only"]
fn a_link_in_a_proxy_sites_challenge_directory_is_refused() {
    // A proxied site opens no file except in its challenge location, which is
    // therefore the only link rule that site has.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let alice = PolygonAccount::create("polylkaeight");
    let bob = PolygonAccount::create("polylkbeight");
    let proxy_domain = Domain::parse("proxyeight.example.test").expect("a valid domain");
    let bob_domain = Domain::parse("bobeight.example.test").expect("a valid domain");

    // The upstream is a port nothing listens on: only the challenge location is
    // asked for, and a request to `/` would be a 502 that proves nothing.
    let input = CreateSiteInput {
        account: alice.name().clone(),
        domain: proxy_domain.clone(),
        aliases: Vec::new(),
        kind: SiteKind::ReverseProxy {
            upstream: Upstream::parse("127.0.0.1:9").expect("a valid upstream"),
        },
        certificate: None,
    };
    create_polygon_site(&ProcessSiteHost::new(), &input)
        .unwrap_or_else(|error| panic!("creating a proxy site must succeed: {error}"));
    let _proxy_vhost =
        PolygonConfigFile::at(&SitePaths::for_site(alice.name(), &proxy_domain).config_path);
    create_polygon_site(
        &ProcessSiteHost::new(),
        &served_site_input(&bob, &bob_domain),
    )
    .unwrap_or_else(|error| panic!("creating bob's site must succeed: {error}"));
    let _bob_vhost =
        PolygonConfigFile::at(&SitePaths::for_site(bob.name(), &bob_domain).config_path);

    write_as_account(
        &bob,
        &format!("sites/{}/wp-config.php", bob_domain.as_str()),
        LINK_SECRET,
    );
    write_as_account(
        &alice,
        &format!(
            "sites/{}/.well-known/acme-challenge/{ACME_TOKEN}",
            proxy_domain.as_str()
        ),
        ACME_TOKEN_BODY,
    );
    let secret_path = SitePaths::for_site(bob.name(), &bob_domain)
        .document_root
        .join("wp-config.php");
    symlink_as_account(
        &alice,
        secret_path.to_str().expect("the path is UTF-8"),
        SitePaths::for_site(alice.name(), &proxy_domain)
            .document_root
            .join(".well-known")
            .join("acme-challenge")
            .join("leak"),
    );
    reload_polygon_nginx();

    let secret = fetch_until(bob_domain.as_str(), "/wp-config.php", |body| {
        body.contains(LINK_SECRET)
    });
    assert!(
        secret.starts_with("HTTP/1.1 200 ") && secret.contains(LINK_SECRET),
        "control c2: the web server must be able to read the victim's file:\n{secret}"
    );
    let token = fetch_until(
        proxy_domain.as_str(),
        &format!("/.well-known/acme-challenge/{ACME_TOKEN}"),
        |body| body.contains(ACME_TOKEN_BODY),
    );
    assert!(
        token.starts_with("HTTP/1.1 200 ") && token.contains(ACME_TOKEN_BODY),
        "control c1: the proxied site's own challenge file must be answered:\n{token}"
    );

    let response = fetch(proxy_domain.as_str(), "/.well-known/acme-challenge/leak");
    assert!(
        response.starts_with("HTTP/1.1 403 "),
        "a link in a proxied site's challenge directory must answer 403:\n{response}"
    );
    assert!(
        !response.contains(LINK_SECRET),
        "a proxied site's challenge location served another account's file:\n{response}"
    );
}

#[test]
#[ignore = "plants real symlinks as a real account and asks a real php site: polygon only"]
fn a_link_from_a_php_site_into_another_home_is_not_served() {
    // No `index.php` in the root, so nothing the fall-through reaches can run:
    // a refused link ends at the `.php` location's `try_files $uri =404`, and a
    // refused plain file at the fall-through to the same 404. php-fpm need not
    // be reachable for either.
    let fixture = LinkFixture::new("nine", true);
    symlink_as_account(
        &fixture.alice,
        &fixture.secret_path(),
        fixture.alice_root().join("leak.txt"),
    );
    symlink_as_account(
        &fixture.alice,
        &fixture.secret_path(),
        fixture.alice_root().join("leak.php"),
    );
    fixture.assert_controls();

    fixture.assert_refused("/leak.txt", "404");
    fixture.assert_refused("/leak.php", "404");
}

#[test]
#[ignore = "writes a real suspended vhost and real certificate material: polygon only"]
fn a_site_suspended_before_the_upgrade_stays_suspended_through_a_certificate_install() {
    // The stub an agent wrote before the challenge location carried its link
    // rule is recognised by comparing bytes, so adding the rule to the stub
    // must not make every stub already on disk read as an ordinary site: the
    // next unattended renewal would put the suspended customer's page back on
    // the air. The previous stub is the current one with that one line taken
    // out, which is exactly what the released agents rendered.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polylkupgrade");
    let domain = Domain::parse("upgrade.example.test").expect("a valid domain");
    let input = served_polygon_site(&account, &domain);
    let vhost = SitePaths::for_site(account.name(), &domain).config_path;
    let _vhost = PolygonConfigFile::at(&vhost);

    maran_ops::sites::disable_site(&ProcessSiteHost::new(), polygon_distro(), &input)
        .unwrap_or_else(|error| panic!("suspending the site must succeed: {error}"));
    let current = std::fs::read_to_string(&vhost).expect("the stub must be readable");
    let rule_line = "        disable_symlinks if_not_owner;\n";
    assert!(
        current.contains(rule_line),
        "the current stub must carry the rule, or there is no previous stub to build:\n{current}"
    );
    let previous = current.replacen(rule_line, "", 1);
    assert_ne!(previous, current);
    std::fs::write(&vhost, &previous).expect("the previous stub can be written");

    reload_polygon_nginx();
    let before = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENSION_NOTICE)
    });
    assert!(
        before.starts_with("HTTP/1.1 403 "),
        "the previous stub must be live before the install proves anything:\n{before}"
    );

    let ssl_host = ProcessSslHost::new();
    generate_self_signed(&ssl_host, polygon_distro(), &input).unwrap_or_else(|error| {
        panic!("issuing material for a suspended site must succeed: {error}")
    });

    assert_eq!(
        std::fs::read_to_string(&vhost).expect("the stub must still be readable"),
        previous,
        "a certificate install must not rewrite a stub an earlier agent wrote"
    );
    reload_polygon_nginx();
    let after = fetch_until(domain.as_str(), "/", |body| {
        body.contains(SUSPENSION_NOTICE)
    });
    assert!(
        after.starts_with("HTTP/1.1 403 "),
        "a site suspended before the upgrade must stay suspended:\n{after}"
    );
    assert!(
        !after.contains(SUSPENDED_ACCOUNT_BODY),
        "the install put a suspended customer's page back on the air:\n{after}"
    );

    maran_ops::ssl::purge_certificate(&ssl_host, polygon_distro(), &domain);
}

// ---------------------------------------------------------------------------
// Host names another vhost already serves (issue #85).
//
// `nginx -t` answers a host name two server blocks share with a warning and
// exit status 0, and then serves the name from whichever file the include glob
// sorts first. Only a real nginx can say whether the creation protocol turns
// that warning into a refusal, and whether the refused vhost is gone again.
// ---------------------------------------------------------------------------

/// The words nginx prints for a host name two server blocks share.
///
/// Spelled again here, as [`VALIDATE_ARGUMENT`] is: the production constant is
/// `pub(crate)`, and a test that took its expectation from the code under test
/// would pass if that code stopped matching what nginx says.
const NGINX_CONFLICT_WORDS: &str = "conflicting server name";

/// The page the first owner of a contested host name serves.
const OWNER_BODY: &str = "maran-hostname-owner";

/// Parses a host name the test wrote by hand.
///
/// # Panics
///
/// Panics when `text` is not a valid domain, which is a defect of the test.
fn host_name(text: &str) -> Domain {
    Domain::parse(text).expect("a valid domain")
}

/// A static site owned by `account` at `domain`, also serving `aliases`.
///
/// Static for the reason [`static_site`] is: the config-write protocol is
/// entered once, and no pool or php-fpm reload is mixed into the answer.
fn site_with_aliases(
    account: &PolygonAccount,
    domain: &Domain,
    aliases: &[&str],
) -> CreateSiteInput {
    CreateSiteInput {
        aliases: aliases.iter().map(|alias| host_name(alias)).collect(),
        ..static_site(account, domain)
    }
}

/// Creates a site that must succeed and makes its vhost go away with the test.
///
/// # Panics
///
/// Panics when the creation is refused.
fn create_guarded_site(
    input: &CreateSiteInput,
) -> (maran_ops::sites::CreatedSite, PolygonConfigFile) {
    let created = create_polygon_site(&ProcessSiteHost::new(), input).unwrap_or_else(|error| {
        panic!(
            "creating {} must succeed in the polygon: {error}",
            input.domain.as_str()
        )
    });
    let guard = PolygonConfigFile::at(&created.config_path);
    (created, guard)
}

/// Fails the test unless `result` is the refusal for a host name already
/// served, and returns the detail it carried.
///
/// # Panics
///
/// Panics with `what` and the actual result when it is anything else.
fn taken_detail(result: Result<maran_ops::sites::CreatedSite, SitesOpError>, what: &str) -> String {
    match result {
        Err(SitesOpError::ServerNameTaken { detail, .. }) => detail,
        other => panic!("{what}: expected ServerNameTaken, got {other:?}"),
    }
}

/// Runs the real `nginx -t`, requires it to succeed, and returns what it wrote
/// to standard error — where nginx puts its warnings.
///
/// # Panics
///
/// Panics when nginx cannot be run or rejects the tree.
fn nginx_t_stderr() -> String {
    let output = std::process::Command::new(polygon_distro().nginx_binary())
        .arg(VALIDATE_ARGUMENT)
        .output()
        .expect("the polygon image installs nginx");
    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();
    assert!(
        output.status.success(),
        "nginx -t must accept the tree:\n{stderr}"
    );
    stderr
}

/// Fails the test when `stderr` carries nginx's conflict warning about `name`,
/// bare or with a leading dot.
///
/// Named and not general on purpose: the polygon's tree is shared by every
/// test in the file, and another test may have planted a conflict of its own on
/// purpose.
///
/// # Panics
///
/// Panics when the warning about `name` is present.
fn assert_no_warning_about(stderr: &str, name: &str) {
    for quoted in [format!("\"{name}\""), format!("\".{name}\"")] {
        assert!(
            !stderr.contains(&format!("{NGINX_CONFLICT_WORDS} {quoted}")),
            "nginx must not warn that {name} is served twice:\n{stderr}"
        );
    }
}

/// Writes a vhost file the way an operator would, beside the agent's own, and
/// makes it go away with the test.
///
/// It listens where the agent's vhosts listen, so a name it shares with one of
/// them is a conflict on a real address. Written as root: it stands for a file
/// the agent did not make and does not own.
///
/// # Panics
///
/// Panics when the file cannot be written.
fn plant_operator_vhost(file_name: &str, server_names: &str) -> PolygonConfigFile {
    let path = Path::new(AgentPaths::NGINX_INCLUDE_DIRECTORY).join(file_name);
    std::fs::write(
        &path,
        format!(
            "server {{\n    listen 80;\n    listen [::]:80;\n    server_name {server_names};\n    return 204;\n}}\n"
        ),
    )
    .expect("an operator's vhost can be written in the polygon");
    PolygonConfigFile::at(path)
}

/// How many `server_name` lines of nginx's own dump of the loaded
/// configuration list `host`.
///
/// Asked of `nginx -T`, the server's own answer to "what do you read", so a
/// file nginx loads is counted whatever it is called and wherever it sits.
///
/// # Panics
///
/// Panics when nginx cannot be run or rejects the tree.
fn server_blocks_naming(host: &str) -> usize {
    let output = std::process::Command::new(polygon_distro().nginx_binary())
        .arg("-T")
        .output()
        .expect("the polygon image installs nginx");
    assert!(
        output.status.success(),
        "nginx -T must succeed:\n{}",
        String::from_utf8_lossy(&output.stderr)
    );
    String::from_utf8_lossy(&output.stdout)
        .lines()
        .filter(|line| {
            line.trim()
                .strip_prefix("server_name ")
                .is_some_and(|names| {
                    names
                        .trim_end_matches(';')
                        .split_whitespace()
                        .any(|name| name == host)
                })
        })
        .count()
}

/// What two creations that were lined up inside the protocol answered.
struct Race {
    /// What the first creation answered.
    first: Result<maran_ops::sites::CreatedSite, SitesOpError>,
    /// What the second creation answered.
    second: Result<maran_ops::sites::CreatedSite, SitesOpError>,
    /// Whether each creation really waited for the other to arrive.
    overlapped: bool,
}

/// Runs two creations on two threads against the one real nginx, each held
/// just before it enters the config-write protocol until the other has
/// arrived there too.
///
/// Symmetric on purpose. The hook runs after everything a creation does by
/// itself, the existence check included, so both creations have already been
/// told "no vhost for my domain yet" when they meet; which of them takes the
/// config-tree lock first is left to the scheduler, and the assertions are
/// written for either order.
fn race_two_creations(first: CreateSiteInput, second: CreateSiteInput) -> Race {
    let (to_second, second_hears) = mpsc::channel::<()>();
    let (to_first, first_hears) = mpsc::channel::<()>();
    let arrivals = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));

    let spawn =
        |input: CreateSiteInput, tell_other: mpsc::Sender<()>, hear_other: mpsc::Receiver<()>| {
            let arrivals = std::sync::Arc::clone(&arrivals);
            std::thread::spawn(move || {
                // Behind a mutex only because the hook is a `Fn` shared across
                // threads by its type; one thread ever calls it.
                let hear_other = std::sync::Mutex::new(hear_other);
                let host = RacingSiteHost {
                    inner: ProcessSiteHost::new(),
                    before_write: Box::new(move || {
                        let _ = tell_other.send(());
                        let heard = hear_other
                            .lock()
                            .expect("the fixture lock is never poisoned")
                            .recv_timeout(RACE_BUDGET)
                            .is_ok();
                        if heard {
                            arrivals.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                        }
                    }),
                };
                create_racing_site(&host, &input)
            })
        };

    let first_thread = spawn(first, to_second, first_hears);
    let second_thread = spawn(second, to_first, second_hears);

    Race {
        first: first_thread.join().expect("the first creation must finish"),
        second: second_thread
            .join()
            .expect("the second creation must finish"),
        overlapped: arrivals.load(std::sync::atomic::Ordering::SeqCst) == 2,
    }
}

#[test]
#[ignore = "writes real vhosts and runs the real nginx -t over both: polygon only"]
fn a_site_naming_a_host_another_vhost_serves_is_refused_by_the_real_nginx_and_leaves_the_tree_as_it_was()
 {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let owner = PolygonAccount::create("polyhnownera");
    let newcomer = PolygonAccount::create("polyhnnewcma");
    let shop = host_name("shop.hn1.test");
    // `aaa` sorts before `shop`: the newcomer's file is the one nginx reads
    // FIRST, so without the refusal the name would move to the newcomer.
    let newcomer_domain = host_name("aaa.hn1.test");

    let (owned, _owned_vhost) = create_guarded_site(&site_with_aliases(&owner, &shop, &[]));
    write_as_account(&owner, "sites/shop.hn1.test/index.html", OWNER_BODY);
    let owned_before = std::fs::read_to_string(&owned.config_path).expect("the owner's vhost");

    let refused_vhost = SitePaths::for_site(newcomer.name(), &newcomer_domain).config_path;
    let refusal = create_polygon_site(
        &ProcessSiteHost::new(),
        &site_with_aliases(&newcomer, &newcomer_domain, &["shop.hn1.test"]),
    );

    let detail = taken_detail(refusal, "the real nginx warned about an alias");
    assert!(
        detail.contains("conflicting server name \"shop.hn1.test\""),
        "the refusal must carry nginx's own words about the host, got {detail:?}"
    );
    assert!(
        !refused_vhost.exists(),
        "a refused site must leave no vhost behind at {refused_vhost:?}"
    );
    assert_eq!(
        std::fs::read_to_string(&owned.config_path).expect("the owner's vhost"),
        owned_before,
        "the owner's vhost must be untouched"
    );
    assert_no_warning_about(&nginx_t_stderr(), "shop.hn1.test");
    assert_nginx_loads(Path::new(&owned.config_path));
    let response = fetch_until("shop.hn1.test", "/", |body| body.contains(OWNER_BODY));
    assert!(
        response.contains(OWNER_BODY),
        "the host name must still be answered by its owner:\n{response}"
    );
}

#[test]
#[ignore = "writes real vhosts and runs the real nginx -t over both: polygon only"]
fn a_site_whose_domain_another_vhost_already_serves_as_an_alias_is_refused() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let owner = PolygonAccount::create("polyhnownerb");
    let newcomer = PolygonAccount::create("polyhnnewcmb");
    // The owner's file sorts first and the newcomer's last — the reverse of the
    // order above — and the contested name is the owner's ALIAS and the
    // newcomer's DOMAIN.
    let owner_domain = host_name("aaa.hn2.test");
    let contested = host_name("zzz.hn2.test");

    let (owned, _owned_vhost) =
        create_guarded_site(&site_with_aliases(&owner, &owner_domain, &["zzz.hn2.test"]));
    let owned_before = std::fs::read_to_string(&owned.config_path).expect("the owner's vhost");

    let refused_vhost = SitePaths::for_site(newcomer.name(), &contested).config_path;
    let refusal = create_polygon_site(
        &ProcessSiteHost::new(),
        &site_with_aliases(&newcomer, &contested, &[]),
    );

    let detail = taken_detail(refusal, "the real nginx warned about the domain");
    assert!(
        detail.contains("conflicting server name \"zzz.hn2.test\""),
        "the refusal must carry nginx's own words about the host, got {detail:?}"
    );
    assert!(
        !refused_vhost.exists(),
        "a refused site must leave no vhost behind at {refused_vhost:?}"
    );
    assert_eq!(
        std::fs::read_to_string(&owned.config_path).expect("the owner's vhost"),
        owned_before,
        "the owner's vhost must be untouched"
    );
    assert_no_warning_about(&nginx_t_stderr(), "zzz.hn2.test");
    assert_nginx_loads(Path::new(&owned.config_path));
}

#[test]
#[ignore = "plants a real operator vhost and runs the real nginx -t: polygon only"]
fn a_name_another_file_serves_with_a_leading_dot_after_this_one_is_refused() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polyhndotlate");
    let domain = host_name("dot.hn3.test");
    // `.dot.hn3.test` is `dot.hn3.test` and every subdomain of it. The file
    // sorts AFTER the agent's, so nginx reads this site's name first and
    // quotes the later key, WITH its dot, in the warning.
    let _operator = plant_operator_vhost("zzzz-wild-hn3.conf", ".dot.hn3.test");
    assert_valid_nginx_tree("the tree with the operator's wildcard file alone");

    let vhost = SitePaths::for_site(account.name(), &domain).config_path;
    let refusal = create_polygon_site(
        &ProcessSiteHost::new(),
        &site_with_aliases(&account, &domain, &[]),
    );

    let detail = taken_detail(refusal, "the operator's wildcard file serves the name");
    assert!(
        detail.contains("conflicting server name \".dot.hn3.test\""),
        "the refusal must carry nginx's own words, dot included, got {detail:?}"
    );
    assert!(!vhost.exists(), "a refused site must leave no vhost behind");
    assert_no_warning_about(&nginx_t_stderr(), "dot.hn3.test");
}

#[test]
#[ignore = "plants a real operator vhost and runs the real nginx -t: polygon only"]
fn a_name_another_file_serves_with_a_leading_dot_before_this_one_is_refused() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polyhndotearl");
    let domain = host_name("dot.hn3b.test");
    // The file sorts BEFORE the agent's, so nginx reads the wildcard first and
    // the warning quotes this site's own name, bare.
    let _operator = plant_operator_vhost("aaaa-wild-hn3b.conf", ".dot.hn3b.test");
    assert_valid_nginx_tree("the tree with the operator's wildcard file alone");

    let vhost = SitePaths::for_site(account.name(), &domain).config_path;
    let refusal = create_polygon_site(
        &ProcessSiteHost::new(),
        &site_with_aliases(&account, &domain, &[]),
    );

    let detail = taken_detail(refusal, "the operator's wildcard file serves the name");
    assert!(
        detail.contains("conflicting server name \"dot.hn3b.test\""),
        "the refusal must carry nginx's own words about the host, got {detail:?}"
    );
    assert!(!vhost.exists(), "a refused site must leave no vhost behind");
    assert_no_warning_about(&nginx_t_stderr(), "dot.hn3b.test");
}

#[test]
#[ignore = "writes real vhosts and runs the real nginx -t over both: polygon only"]
fn a_site_whose_names_collide_with_nothing_is_created_beside_an_existing_one() {
    // The inverse control of the refusals above: a check that refused every
    // creation beside another vhost would pass all of them.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let owner = PolygonAccount::create("polyhnownerd");
    let neighbour = PolygonAccount::create("polyhnnewcmd");
    let shop = host_name("shop.hn4.test");
    let other = host_name("other.hn4.test");

    let (owned, _owned_vhost) = create_guarded_site(&site_with_aliases(&owner, &shop, &[]));
    // One alias is `shop.hn4.test` with more on the end: a different host that a
    // loose match on the bare text would confuse with the owner's.
    let (created, _created_vhost) = create_guarded_site(&site_with_aliases(
        &neighbour,
        &other,
        &["www.other.hn4.test", "shop.hn4.test.example"],
    ));

    assert!(Path::new(&created.config_path).exists());
    assert!(Path::new(&owned.config_path).exists());
    let stderr = nginx_t_stderr();
    assert_no_warning_about(&stderr, "shop.hn4.test");
    assert_no_warning_about(&stderr, "other.hn4.test");
    assert_nginx_loads(Path::new(&owned.config_path));
    assert_nginx_loads(Path::new(&created.config_path));
}

#[test]
#[ignore = "plants two real operator vhosts and runs the real nginx -t: polygon only"]
fn a_conflict_between_two_other_files_does_not_block_an_unrelated_site() {
    // One bad file of someone else's must not turn into a host on which no
    // site can be created: only the creating site's own names refuse.
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let account = PolygonAccount::create("polyhnunrelat");
    let _first = plant_operator_vhost("zzzz-a-hn5.conf", "other.hn5.test");
    let _second = plant_operator_vhost("zzzz-b-hn5.conf", "other.hn5.test");
    // The positive control: the conflict really is in the tree, and `nginx -t`
    // really does say so while exiting 0. Without this the test would pass on a
    // host where the planted files conflicted with nothing.
    assert!(
        nginx_t_stderr().contains(&format!("{NGINX_CONFLICT_WORDS} \"other.hn5.test\"")),
        "the two planted files must conflict in the real nginx"
    );

    let (created, _vhost) = create_guarded_site(&site_with_aliases(
        &account,
        &host_name("unrelated.hn5.test"),
        &[],
    ));

    assert_nginx_loads(Path::new(&created.config_path));
}

#[test]
#[ignore = "runs two real vhost writes against one real nginx at the same time: polygon only"]
fn two_creations_racing_for_one_host_name_leave_exactly_one_vhost_answering_for_it() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let first = PolygonAccount::create("polyhnracea");
    let second = PolygonAccount::create("polyhnraceb");
    // Different domains, so each creation's existence check passes and only the
    // shared alias can stop the second; `zero` and `shop` sort in either order
    // depending on which thread is the winner.
    let first_domain = host_name("shop.hn6.test");
    let second_domain = host_name("zero.hn6.test");
    let first_vhost = SitePaths::for_site(first.name(), &first_domain).config_path;
    let second_vhost = SitePaths::for_site(second.name(), &second_domain).config_path;
    let _first_guard = PolygonConfigFile::at(&first_vhost);
    let _second_guard = PolygonConfigFile::at(&second_vhost);

    let race = race_two_creations(
        site_with_aliases(&first, &first_domain, &[]),
        site_with_aliases(&second, &second_domain, &["shop.hn6.test"]),
    );

    // The positive control: both creations really stood at the protocol's door
    // together, with their existence checks already behind them.
    assert!(
        race.overlapped,
        "each creation must have arrived at the protocol while the other was there"
    );
    let (winner, loser_vhost, loser) = match (race.first, race.second) {
        (Ok(created), Err(error)) => (created, second_vhost, error),
        (Err(error), Ok(created)) => (created, first_vhost, error),
        (first, second) => {
            panic!("exactly one creation must win, got {first:?} and {second:?}")
        }
    };
    assert!(
        matches!(loser, SitesOpError::ServerNameTaken { .. }),
        "the loser must be told the name is taken, got {loser:?}"
    );
    assert!(
        Path::new(&winner.config_path).exists(),
        "the winner's vhost must be on disk"
    );
    assert!(
        !loser_vhost.exists(),
        "the loser must leave no vhost behind at {loser_vhost:?}"
    );
    assert_eq!(
        server_blocks_naming("shop.hn6.test"),
        1,
        "exactly one vhost may answer for the contested name"
    );
    assert_no_warning_about(&nginx_t_stderr(), "shop.hn6.test");
    assert_nginx_loads(Path::new(&winner.config_path));
}

#[test]
#[ignore = "runs two real vhost writes against one real nginx at the same time: polygon only"]
fn two_creations_of_the_same_domain_leave_one_vhost_and_never_overwrite_the_first() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let first = PolygonAccount::create("polyhnsamea");
    let second = PolygonAccount::create("polyhnsameb");
    let domain = host_name("same.hn7.test");
    let vhost = SitePaths::for_site(first.name(), &domain).config_path;
    let _guard = PolygonConfigFile::at(&vhost);

    // The same domain from two accounts: one file name, so both creations pass
    // the existence check (neither file is there yet) and meet at the write.
    let race = race_two_creations(
        site_with_aliases(&first, &domain, &[]),
        site_with_aliases(&second, &domain, &[]),
    );

    assert!(
        race.overlapped,
        "each creation must have arrived at the protocol while the other was there"
    );
    let (winner, winner_home, loser_home, loser) = match (race.first, race.second) {
        (Ok(created), Err(error)) => (created, first.home(), second.home(), error),
        (Err(error), Ok(created)) => (created, second.home(), first.home(), error),
        (first, second) => {
            panic!("exactly one creation must win, got {first:?} and {second:?}")
        }
    };
    assert!(
        matches!(loser, SitesOpError::ServerNameTaken { .. }),
        "the loser must be told the name is taken, got {loser:?}"
    );
    let surviving = std::fs::read_to_string(&vhost).expect("the winner's vhost must be on disk");
    assert!(
        surviving.contains(&winner.document_root),
        "the surviving vhost must be the winner's, got:\n{surviving}"
    );
    assert!(
        !surviving.contains(&loser_home.display().to_string()),
        "the loser's vhost must not have replaced the winner's, got:\n{surviving}"
    );
    assert!(
        winner
            .document_root
            .starts_with(&winner_home.display().to_string())
    );
    assert_eq!(server_blocks_naming("same.hn7.test"), 1);
    assert_nginx_loads(&vhost);
}

/// The name of the include the hung-validator tests plant: sorted after every
/// vhost the agent writes, and matched by the `*.conf` include.
const HANG_FILE_NAME: &str = "zz-maran-hang.conf";

/// What a killed program is given, on top of its deadline, to be reaped and
/// for the rollback to finish, before a hung-validator test declares the bound
/// broken.
const REAP_AND_ROLLBACK_BUDGET: Duration = Duration::from_secs(30);

/// How long a write for another account may take once the validator can run
/// again. A real `nginx -t` plus reload takes a fraction of a second; a lock
/// still held by the timed-out write would keep this waiting for good.
const NEXT_WRITE_BUDGET: Duration = Duration::from_secs(15);

/// Plants a first-in-first-out file where nginx's `include` glob finds it.
///
/// `nginx -t` opens every file the glob names, and `open(2)` on a FIFO with no
/// writer blocks for ever: the validator hangs the way a validator stuck on an
/// unreachable upstream name does, with no dependence on the network. Created
/// with `mkfifo`, which both families ship in coreutils. The returned guard
/// removes it again, on a panic too.
///
/// # Panics
///
/// Panics when the FIFO cannot be made.
fn plant_hanging_include() -> PolygonConfigFile {
    let path = Path::new(AgentPaths::NGINX_INCLUDE_DIRECTORY).join(HANG_FILE_NAME);
    let made = std::process::Command::new("/usr/bin/mkfifo")
        .arg("--")
        .arg(&path)
        .status()
        .expect("mkfifo can be started");
    assert!(made.success(), "mkfifo failed to make {path:?}");

    PolygonConfigFile::at(path)
}

/// Like [`within`], with the budget given by the caller.
///
/// # Panics
///
/// Panics, naming `what`, when `body` outlasts `budget`.
fn within_budget<T: Send + 'static>(
    what: &str,
    budget: Duration,
    body: impl FnOnce() -> T + Send + 'static,
) -> T {
    let (sender, receiver) = mpsc::channel();
    std::thread::spawn(move || {
        let _ = sender.send(body());
    });

    receiver
        .recv_timeout(budget)
        .unwrap_or_else(|_| panic!("{what} did not finish within {budget:?}"))
}

/// What a bounded `create_site` came back with, reduced to what crosses a
/// thread boundary.
struct TimedCreation {
    /// Whether the call ended in the typed deadline error.
    timed_out: bool,
    /// The call's result, for a failure message.
    summary: String,
    /// How long the call took.
    elapsed: Duration,
}

/// Creates a static site for `account` at `domain` with the real hosts on a
/// thread of its own and reports how it ended and how long it took.
fn create_static_site_timed(
    account: &PolygonAccount,
    domain: &Domain,
    what: &str,
    budget: Duration,
) -> TimedCreation {
    let input = static_site(account, domain);

    within_budget(what, budget, move || {
        let started = std::time::Instant::now();
        let result = create_polygon_site(&ProcessSiteHost::new(), &input);
        let elapsed = started.elapsed();

        TimedCreation {
            timed_out: matches!(
                result,
                Err(SitesOpError::Bound(BoundExceeded::TimedOut { .. }))
            ),
            summary: format!("{result:?}"),
            elapsed,
        }
    })
}

/// How long the test waits for something the host does on its own: a probe to
/// show up in `/proc`, or a killed validator to be gone from it.
const PROCESS_TABLE_BUDGET: Duration = Duration::from_secs(10);

/// Gap between two looks at the process table.
const PROCESS_TABLE_POLL: Duration = Duration::from_millis(50);

/// Polls `condition` until it holds or `budget` is spent, and answers whether it
/// held. A poll with a timeout and not a sleep: the answer comes the moment the
/// condition is true.
fn wait_until(budget: Duration, mut condition: impl FnMut() -> bool) -> bool {
    let deadline = std::time::Instant::now() + budget;
    loop {
        if condition() {
            return true;
        }
        if std::time::Instant::now() >= deadline {
            return false;
        }
        std::thread::sleep(PROCESS_TABLE_POLL);
    }
}

/// How many live processes are this family's `nginx -t`, found by their exact
/// argument vector in `/proc`.
///
/// Read from `cmdline` and not from a process name, so an `nginx` master or
/// worker is not counted, and a zombie (whose `cmdline` is empty) is not counted
/// either: a killed validator that has been reaped and one that is merely waiting
/// to be reaped are both "gone" for this question.
///
/// # Panics
///
/// Panics when `/proc` cannot be listed.
fn running_validators() -> usize {
    let wanted = format!("{}\0{VALIDATE_ARGUMENT}\0", polygon_distro().nginx_binary());

    std::fs::read_dir("/proc")
        .expect("/proc can be listed")
        .filter_map(Result::ok)
        .filter(|entry| {
            entry
                .file_name()
                .to_string_lossy()
                .bytes()
                .all(|byte| byte.is_ascii_digit())
        })
        .filter(|entry| {
            std::fs::read(entry.path().join("cmdline"))
                .is_ok_and(|cmdline| cmdline == wanted.as_bytes())
        })
        .count()
}

/// Proves, with the FIFO in place, that this family's `nginx -t` really blocks
/// on it and that [`running_validators`] can see such a process.
///
/// The positive control of the "the killed validator is gone" assertion: a scan
/// that matched nothing, because of how `cmdline` is spelled on this family,
/// would report every validator gone whether or not the agent had killed it.
/// The probe is a validator this test starts itself, so it is also the answer to
/// "does the hang need anything this family's nginx does not do".
///
/// # Panics
///
/// Panics when the probe cannot be started, does not hang, or is still counted
/// after it has been killed and reaped.
fn prove_the_fifo_hangs_a_validator_the_scan_can_see() {
    let mut probe = std::process::Command::new(polygon_distro().nginx_binary())
        .arg(VALIDATE_ARGUMENT)
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .spawn()
        .expect("the polygon image installs nginx");

    let seen = wait_until(PROCESS_TABLE_BUDGET, || running_validators() == 1);
    probe.kill().expect("the probe can be killed");
    probe.wait().expect("the probe can be reaped");

    assert!(
        seen,
        "with a FIFO in the include directory, `nginx -t` must stay running and be seen once in \
         /proc; it was not, so the hang this test relies on does not happen on this family"
    );
    assert_eq!(
        running_validators(),
        0,
        "the probe was killed and reaped and must no longer be counted"
    );
}

#[test]
#[ignore = "hangs the real nginx -t on purpose and waits out its deadline: polygon only"]
fn a_validator_that_hangs_on_a_real_host_is_killed_at_its_deadline_the_vhost_is_restored_and_the_next_write_proceeds()
 {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let hung = PolygonAccount::create("polyhangone");
    let next = PolygonAccount::create("polyhangtwo");
    let hung_domain = Domain::parse("hung.example.test").expect("a valid domain");
    let next_domain = Domain::parse("next.example.test").expect("a valid domain");
    let hung_vhost = SitePaths::for_site(hung.name(), &hung_domain).config_path;
    let next_vhost = SitePaths::for_site(next.name(), &next_domain).config_path;
    let _next_vhost = PolygonConfigFile::at(&next_vhost);

    let fifo = plant_hanging_include();
    prove_the_fifo_hangs_a_validator_the_scan_can_see();
    let deadline = CallClass::Validate.deadline();
    let outcome = create_static_site_timed(
        &hung,
        &hung_domain,
        "a create_site whose nginx -t hangs",
        deadline + REAP_AND_ROLLBACK_BUDGET,
    );

    assert!(
        outcome.timed_out,
        "a hung validator must end the write with the typed deadline error, got {}",
        outcome.summary
    );
    assert!(
        outcome.elapsed >= deadline,
        "the validator was stopped after {:?}, before its deadline of {deadline:?}",
        outcome.elapsed
    );
    assert!(
        !hung_vhost.exists(),
        "a write whose validator was killed must leave no vhost behind at {hung_vhost:?}"
    );
    assert!(
        wait_until(PROCESS_TABLE_BUDGET, || running_validators() == 0),
        "the validator the agent started must be gone once the write has returned, not left \
         blocked on the FIFO"
    );

    // The lock the hung write held is released and the next tenant is not
    // starved: with the FIFO gone, another account's write completes quickly.
    drop(fifo);
    let following = create_static_site_timed(
        &next,
        &next_domain,
        "the create_site that followed the timed-out one",
        NEXT_WRITE_BUDGET,
    );
    assert!(
        following.summary.starts_with("Ok("),
        "the write after a timed-out one must succeed, got {}",
        following.summary
    );
    assert!(
        next_vhost.exists(),
        "the following write must have written its vhost at {next_vhost:?}"
    );
    assert_valid_nginx_tree("the tree after a hung validator was killed and the next write ran");
}

#[test]
#[ignore = "hangs the real nginx -t on purpose and waits out its deadline: polygon only"]
fn a_hung_validator_reaches_the_panel_as_system_failure_over_the_real_rpc() {
    PolygonAccount::require_polygon();
    ensure_nginx_is_running();
    let hung = PolygonAccount::create("polyhangrpc");
    let next = PolygonAccount::create("polyhangrpctwo");
    let next_vhost = SitePaths::for_site(
        next.name(),
        &Domain::parse("rpcnext.example.test").expect("a valid domain"),
    )
    .config_path;
    let _next_vhost = PolygonConfigFile::at(&next_vhost);

    let deadline = CallClass::Validate.deadline();
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .expect("a tokio runtime");

    let request = |account: &PolygonAccount, domain: &str| CreateSiteRequest {
        account_username: account.name().as_str().to_owned(),
        domain: domain.to_owned(),
        backend_type: SiteBackendType::Static as i32,
        ..CreateSiteRequest::default()
    };
    let hung_request = request(&hung, "rpchung.example.test");
    let next_request = request(&next, "rpcnext.example.test");

    runtime.block_on(async move {
        let agent = InProcessAgent::start().await;

        // Planted only now. `serve` validates the web tree once before it binds
        // (`reconcile_web_tree`), so a FIFO already in place would hang the
        // agent's STARTUP for a whole deadline and the test would blame the
        // bind, not the request it is about.
        let fifo = plant_hanging_include();

        let refused = tokio::time::timeout(
            deadline + REAP_AND_ROLLBACK_BUDGET,
            SitesServiceClient::new(agent.channel()).create_site(hung_request),
        )
        .await
        .expect("the rpc answered within the validator's deadline plus the reap budget")
        .expect("the rpc itself succeeds: the failure is a typed payload")
        .into_inner();

        match refused.result {
            Some(create_site_response::Result::Error(error)) => {
                assert_eq!(
                    error.code,
                    ErrorCode::SystemFailure as i32,
                    "a deadline is a fault of the host: {}",
                    error.message
                );
                assert!(
                    error.tool_output.is_empty(),
                    "nothing the killed program printed may be forwarded, got {:?}",
                    error.tool_output
                );
                assert!(
                    error.message.contains("nginx"),
                    "the message must name the program that was stopped: {}",
                    error.message
                );
            }
            other => panic!("a hung validator must answer with a typed error, got {other:?}"),
        }

        drop(fifo);
        let following = tokio::time::timeout(
            NEXT_WRITE_BUDGET,
            SitesServiceClient::new(agent.channel()).create_site(next_request),
        )
        .await
        .expect("the rpc after a timed-out one answers promptly")
        .expect("the rpc itself succeeds")
        .into_inner();

        assert!(
            matches!(following.result, Some(create_site_response::Result::Ok(_))),
            "the write after a timed-out one must succeed, got {following:?}"
        );
    });
}
