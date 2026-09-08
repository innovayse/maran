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

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_config_file.rs"]
mod polygon_config_file;

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;
use std::sync::mpsc;
use std::time::Duration;

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::files::{ProcessFilesHost, WriteFileInput};
use maran_ops::php::ProcessPhpHost;
use maran_ops::sites::{
    CreateSiteInput, ProcessSiteHost, SiteCertificate, SiteHost, SiteIdentity, SiteKind, SitePaths,
    SitesOpError, create_site,
};
use maran_ops::ssl::{ProcessSslHost, delete_site_with_certificate, generate_self_signed};

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
        contents.contains("fastcgi_pass unix:/run/maran/php/"),
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
    let checked = std::process::Command::new(polygon_distro().php_fpm_binary(POLYGON_PHP_VERSION))
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
