//! The sessions of an SFTP login whose keys are revoked, against the real
//! OpenSSH daemon (issues #135 and #339).
//!
//! Split from `sftp_on_a_real_host.rs`, which proves the other half — a revoked
//! key is refused at the NEXT login — because this half needs sessions held open
//! across the revocation and that file was at the test-file size trigger.
//!
//! sshd reads the key file when a connection authenticates and never again for a
//! session it has admitted, so replacing a login's key set cannot end a session
//! by itself. `end_sftp_sessions` is what does, and only a real daemon and a real
//! `pkill` can say whether it ends the right processes.
//!
//! These tests need `docker run --privileged`, for the reason the sibling suites
//! state: the bind mount is a real mount.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
// This suite opens sessions with keys only, so the password probes go unused.
#[allow(dead_code)]
#[path = "fixtures/polygon_sshd.rs"]
mod polygon_sshd;
// Shared by the SFTP suites; each uses only part of it.
#[allow(dead_code)]
#[path = "fixtures/polygon_sftp_login.rs"]
mod polygon_sftp_login;
// The password login is used by the sibling suite; this one opens key sessions.
#[allow(dead_code)]
#[path = "fixtures/sftp_control_session.rs"]
mod sftp_control_session;

use maran_ops::sftp::{AuthorizedKeySet, ProcessSftpHost, SftpError, end_sftp_sessions};

use polygon_account::PolygonAccount;
use polygon_sftp_login::PolygonSftpLogin;
use polygon_sshd::PolygonSshd;
use sftp_control_session::SftpControlSession;

#[test]
#[ignore = "holds two real key-authenticated SFTP sessions open across a real revocation: polygon only"]
fn ending_a_logins_sessions_cuts_its_key_session_and_leaves_another_accounts_alone() {
    // What is measured, and why each part is here rather than assumed:
    //
    // * both sessions authenticate with a KEY and transfer before anything
    //   happens — without that, a failure afterwards would be about this client;
    // * replacing the key set with an empty one does NOT end the open session —
    //   the finding that makes `end_sftp_sessions` necessary at all;
    // * the same call made for ANOTHER account is refused as `NotFound` and cuts
    //   nothing — the cross-tenant control, which also proves the session
    //   survives a call that was not meant for it;
    // * the call for the owning account ends the session: the transfer lands
    //   nothing, and a count of at least one is reported;
    // * the NEIGHBOUR's session goes on transferring through the same daemon at
    //   the same moment, which is both the inverse control (the signal is
    //   scoped to a login) and the proof that the daemon was still serving when
    //   the first session died;
    // * a second call finds nothing to end and answers zero — idempotence.
    let _sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polykeysend");
    PolygonSftpLogin::plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let neighbour = PolygonAccount::create("polykeysnear");
    PolygonSftpLogin::plant_file(&neighbour);
    let neighbour_login = PolygonSftpLogin::create(&neighbour);

    let scratch = tempfile::tempdir().expect("a scratch directory");
    let own_directory = scratch.path().join("own");
    let neighbour_directory = scratch.path().join("neighbour");
    std::fs::create_dir(&own_directory).expect("a directory for the login's key pair");
    std::fs::create_dir(&neighbour_directory).expect("a directory for the neighbour's key pair");
    let (own_identity, own_keys) = PolygonSftpLogin::key_pair(&own_directory);
    let (neighbour_identity, neighbour_keys) = PolygonSftpLogin::key_pair(&neighbour_directory);
    login.set_keys(&own_keys);
    neighbour_login.set_keys(&neighbour_keys);

    let (mut session, greeting) = SftpControlSession::login_with_key(login.name(), &own_identity);
    assert!(
        greeting.contains("Remote working directory:"),
        "the session must be authenticated by its key BEFORE the revocation: {greeting}"
    );
    let (mut neighbour_session, neighbour_greeting) =
        SftpControlSession::login_with_key(neighbour_login.name(), &neighbour_identity);
    assert!(
        neighbour_greeting.contains("Remote working directory:"),
        "the neighbour's key session must be open too, or it cannot survive anything: \
         {neighbour_greeting}"
    );
    assert_eq!(
        session.fetch_through("keys-before"),
        PolygonSftpLogin::CUSTOMER_CONTENT,
        "the open session must transfer before the revocation, or a failure \
         afterwards would be about this client"
    );

    // THE REASON THE OPERATION EXISTS: revoking the key refuses the next login
    // and does nothing to the session already admitted.
    login.set_keys(&AuthorizedKeySet::new(Vec::new()).expect("an empty set"));
    assert_eq!(
        session.fetch_through("keys-revoked"),
        PolygonSftpLogin::CUSTOMER_CONTENT,
        "MEASURED: replacing the key set leaves an admitted session transferring. \
         THIS LINE GOING RED means sshd now re-reads the key file for open sessions, \
         and `end_sftp_sessions` may no longer be needed"
    );

    // The cross-tenant control: the neighbour's account does not hold this login.
    let refused = end_sftp_sessions(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        neighbour.name(),
        &login.user,
    );
    assert!(
        matches!(refused, Err(SftpError::NotFound)),
        "a request authorised for another account must be refused as NotFound: {refused:?}"
    );
    assert_eq!(
        session.fetch_through("keys-after-refusal"),
        PolygonSftpLogin::CUSTOMER_CONTENT,
        "a refused request must have signalled nothing"
    );

    let ended = end_sftp_sessions(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        &login.account,
        &login.user,
    )
    .unwrap_or_else(|error| panic!("ending the login's sessions must succeed: {error}"));
    assert!(
        ended >= 1,
        "the call must report the session this test held open: got {ended}"
    );

    // THE FINDING, asked by moving bytes: `pwd` is answered from the client's own
    // state after the daemon has closed the connection (see
    // `fixtures/sftp_control_session.rs`).
    let (after, landed) = session.fetch_attempt("keys-after");
    assert!(
        landed.is_none(),
        "MEASURED: ending a login's sessions cuts the key session it had open. \
         THIS LINE GOING RED means the revoked key's session still moved data — check \
         that `pkill` is installed on this image and that the sshd title pattern still \
         names this login. The session said: {after}"
    );

    // The inverse control and the harness's own liveness proof.
    assert_eq!(
        neighbour_session.fetch_through("keys-neighbour-after"),
        PolygonSftpLogin::CUSTOMER_CONTENT,
        "a signal that reached another login would be a cross-tenant denial of \
         service, and a daemon that had stopped serving every session would make \
         the assertion above vacuous"
    );

    // Idempotence: a second call finds no session and says so.
    let again = end_sftp_sessions(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        &login.account,
        &login.user,
    )
    .unwrap_or_else(|error| panic!("repeating the call must succeed: {error}"));
    assert_eq!(again, 0, "nothing was left to end");

    drop(session);
    drop(neighbour_session);
    drop(login);
    drop(neighbour_login);
}
