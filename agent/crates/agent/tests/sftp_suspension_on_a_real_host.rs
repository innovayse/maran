//! SFTP logins across a real suspension, against the real OpenSSH daemon.
//!
//! Split from `sftp_on_a_real_host.rs` (which keeps the jail, the credential and
//! the key cases) when that file passed the test-file size trigger: everything
//! here is about what a suspension does to a login that worked before it — the
//! password lock, a password changed while locked, the resume, the attestation
//! the panel reads, and the session already open when the lock lands.
//!
//! These tests need `docker run --privileged`, for the reason the sibling suite
//! states: the bind mount is a real mount.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
// This suite drives password sessions only, so the key and exec probes go
// unused here; the allow is on the module, as in the sibling suites.
#[allow(dead_code)]
#[path = "fixtures/polygon_sshd.rs"]
mod polygon_sshd;
// Shared by both SFTP suites; each uses only part of it, so the allow sits on
// the module and an item no suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_sftp_login.rs"]
mod polygon_sftp_login;
#[path = "fixtures/sftp_control_session.rs"]
mod sftp_control_session;

use std::io::Write as _;
use std::process::Command;

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_ops::accounts::{AccountOperations, ProcessSystemHost, StoredPassword};
use maran_ops::cron::ProcessCronHost;
use maran_ops::logins::{ProcessLoginsHost, account_logins, set_account_logins_locked};
use maran_ops::sftp::{ProcessSftpHost, set_sftp_password};
use maran_ops::sites::ProcessSiteHost;

use polygon_account::PolygonAccount;
use polygon_sftp_login::PolygonSftpLogin;
use polygon_sshd::PolygonSshd;
use sftp_control_session::SftpControlSession;

#[test]
#[ignore = "authenticates against a real sshd after a real suspension: polygon only"]
fn suspending_the_account_refuses_the_sftp_login_that_worked_before_it() {
    // P4. The proposition the design names, and the defect it exists to close:
    // an SFTP login is its OWN passwd entry — `<account>_<name>`, created with
    // `useradd --non-unique --uid <account uid>` — so `usermod --lock <account>`
    // prefixes the ACCOUNT's hash and touches none of them. A suspended customer
    // therefore kept a working WRITE credential into their own home.
    //
    // It is asserted as a real refusal in a real session, never as a `!` read
    // out of `/etc/shadow`: a locked hash is not the only thing that decides an
    // authentication, and the only question worth answering is what the daemon
    // does.
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftpsusp");
    PolygonSftpLogin::plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let operations = AccountOperations::new(
        ProcessSystemHost::new(PolygonSftpLogin::distro()),
        PolygonSftpLogin::distro(),
        maran_agent_core::utils::free_space_floor::FreeSpaceFloor::default_floor(),
    );

    // The inverse control, and it is not optional here: an assertion that a
    // login is refused is satisfied by a login that never worked — a wrong
    // password, a jail that was never mounted, an sshd that is not listening.
    let before = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        before.status.success(),
        "the login must work BEFORE the suspension, or the refusal below proves nothing:\n{}",
        PolygonSftpLogin::said(&before)
    );

    // The two halves of a suspension, in the order the panel drives them: the
    // account's own passwd entry, and then every SFTP login it holds. The
    // second call is the whole of this test's subject — `usermod --lock` on the
    // account reaches none of these entries, because each is its own.
    operations
        .suspend(account.name())
        .unwrap_or_else(|error| panic!("suspending the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        true,
    )
    .unwrap_or_else(|error| panic!("locking the account's logins must succeed: {error}"));

    let after = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !after.status.success(),
        "a suspended account's SFTP login must be refused, and it is a live write \
         credential into the customer's home for as long as it is not:\n{}",
        PolygonSftpLogin::said(&after)
    );

    // The agent's own answer about the same login, checked against what the
    // daemon just did rather than against the code that wrote it. This is the
    // observation the panel refuses a suspension on.
    let observed = account_logins(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
    )
    .expect("the password database must be readable");
    assert_eq!(
        observed.logins.len(),
        1,
        "the account holds exactly its one login"
    );
    assert!(
        observed.logins.iter().all(|login| login.locked),
        "the attestation must report as locked the login sshd just refused: {observed:?}"
    );
    assert_eq!(
        observed.unmanaged, 0,
        "this account holds no login outside its jails, and a count that said otherwise \
         would mean the attestation is measuring against the wrong uid: {observed:?}"
    );

    // And the reversal, driven in FULL: both halves of the resume, in the order
    // the panel drives them, on a real account whose login a real daemon just
    // refused.
    //
    // The account half used to be left out of this test, because
    // `AccountOperations::unsuspend` could not complete on the RHEL family at
    // all: a hosting account is created by `useradd` and is never given a
    // password by this agent, `usermod --unlock` refuses such a login with
    // "unlocking the user's password would result in a passwordless account",
    // and the two families spell that refusal as exit 0 and exit 1
    // respectively. `unsuspend` now asks what the account's shadow field
    // actually holds and runs the unlock only where there is a password behind
    // the lock, so the call belongs here — and it is what makes this
    // proposition a reversal rather than half of one. If that fix regressed,
    // this line is what would see it, on the RHEL image, as a failed resume.
    operations
        .unsuspend(account.name())
        .unwrap_or_else(|error| panic!("reactivating the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        false,
    )
    .unwrap_or_else(|error| panic!("unlocking the account's logins must succeed: {error}"));

    let resumed = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        resumed.status.success(),
        "resuming must give the login back:\n{}",
        PolygonSftpLogin::said(&resumed)
    );

    drop(login);
}

#[test]
#[ignore = "changes a real login's password under a real suspension: polygon only"]
fn changing_an_sftp_password_while_suspended_does_not_hand_the_login_back() {
    // P9. M-4, the half that is not a race, on the instrument that can see it.
    //
    // `chpasswd` REPLACES the shadow password field; a suspension is a `!`
    // written in front of that same field. So a suspended customer who changed
    // their own SFTP password through the panel got a fresh unmarked hash and an
    // authenticating login, with the panel still reporting the account as
    // suspended. No race, no attacker, no second actor.
    //
    // **The assertions are on the RAW shadow field**, read by a separate
    // `getent` process. An assertion that the account "is still suspended"
    // according to the panel's own record would pass against this very defect,
    // because the panel's record is exactly what stays wrong. The field is the
    // byte string PAM consults, so an assertion on it is an assertion about
    // whether a password can authenticate — and the daemon is asked as well,
    // because a locked hash is not the only thing that decides an authentication.
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftppwsus");
    PolygonSftpLogin::plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let user = SftpUserName::for_account(account.name(), "web").expect("a valid login name");
    let operations = AccountOperations::new(
        ProcessSystemHost::new(PolygonSftpLogin::distro()),
        PolygonSftpLogin::distro(),
        maran_agent_core::utils::free_space_floor::FreeSpaceFloor::default_floor(),
    );

    // The inverse control. Everything below is about a credential being taken
    // away and kept away, and every one of those assertions is satisfied by a
    // login that never worked at all.
    let working = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        working.status.success(),
        "the login must work BEFORE the suspension, or nothing below proves anything:\n{}",
        PolygonSftpLogin::said(&working)
    );
    let credited = shadow_password_field(login.name());
    assert!(
        !credited.starts_with('!') && credited.len() > 1,
        "a working login must hold a usable hash before the suspension: {credited}"
    );

    // The suspension, both halves, in the order the panel drives them.
    operations
        .suspend(account.name())
        .unwrap_or_else(|error| panic!("suspending the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        true,
    )
    .unwrap_or_else(|error| panic!("locking the account's logins must succeed: {error}"));

    let suspended = shadow_password_field(login.name());
    assert!(
        suspended.starts_with('!'),
        "the suspension must really lock the login's field, or the change below has \
         nothing to undo: {suspended}"
    );

    // The defect, driven exactly as a suspended customer drives it: one call to
    // the operation the panel offers them.
    set_sftp_password(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        &user,
        &Password::parse(PolygonSftpLogin::SECOND_PASSWORD).expect("a valid password"),
    )
    .unwrap_or_else(|error| panic!("changing the password must still succeed: {error}"));

    // THE assertion. The lock character is what the defect removes, and this is
    // the value it must hold — not "the field changed", not "the panel still
    // says suspended".
    let after = shadow_password_field(login.name());
    assert!(
        after.starts_with('!'),
        "the lock character is gone from the shadow field after a password change: a \
         suspended account has just unlocked its own login. The field is {after}, and it \
         was {suspended} while suspended"
    );
    // And the password really was set underneath the lock — the fix restores a
    // suspension, it does not refuse the operation. Without this, "never run
    // chpasswd at all" would pass the assertion above.
    assert!(
        after.len() > 1 && after != suspended,
        "the new password must have been written under the lock: the field is still {after}"
    );

    // The daemon's own answer about the same login, for both credentials: the
    // one that was replaced and the one that just replaced it.
    let with_the_old = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !with_the_old.status.success(),
        "the replaced password must not authenticate a suspended login:\n{}",
        PolygonSftpLogin::said(&with_the_old)
    );
    let with_the_new = sshd.sftp(login.name(), PolygonSftpLogin::SECOND_PASSWORD, "pwd\n");
    assert!(
        !with_the_new.status.success(),
        "a suspended account's SFTP login must stay refused after its own password \
         change, and it is a live write credential into the customer's home for as long \
         as it is not:\n{}",
        PolygonSftpLogin::said(&with_the_new)
    );

    // And the resume gives back the password that was set while suspended,
    // which is the promise the fix makes: the customer's new credential was
    // stored, not thrown away.
    operations
        .unsuspend(account.name())
        .unwrap_or_else(|error| panic!("reactivating the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        false,
    )
    .unwrap_or_else(|error| panic!("unlocking the account's logins must succeed: {error}"));

    let resumed = sshd.sftp(login.name(), PolygonSftpLogin::SECOND_PASSWORD, "pwd\n");
    assert!(
        resumed.status.success(),
        "the resume must hand back the password the customer set while suspended:\n{}",
        PolygonSftpLogin::said(&resumed)
    );

    drop(login);
}

#[test]
#[ignore = "locks and unlocks a real account's own password entry: polygon only"]
fn reactivating_an_account_leaves_a_hand_set_password_working_and_a_passwordless_one_alone() {
    // P7. The inverse control for the conditional unlock in
    // `AccountOperations::unsuspend`, and the reason the repair is not "skip the
    // unlock".
    //
    // Two accounts, because one can only ever show half of it:
    //
    // - the ordinary hosting account, which this agent never gives a password.
    //   Its shadow field is markers only (`!` on the Debian family, `!!` on the
    //   RHEL one, both measured), `usermod --lock` on it is a measured no-op,
    //   and `usermod --unlock` refuses it — exit 0 on Debian, **exit 1 on
    //   RHEL**. The resume must SUCCEED on both, which is the whole defect.
    // - an account somebody set a password on by hand, which is the only reason
    //   the account-level lock is worth applying at all. That password must
    //   really stop working while suspended and must come back afterwards. Had
    //   the fix been "never unlock", this half would fail and the customer would
    //   be locked out for good — which is why it is here.
    //
    // The assertions are on the raw shadow field, which is the byte string PAM
    // consults, read by a separate `getent` process rather than through the code
    // under test.
    let operations = AccountOperations::new(
        ProcessSystemHost::new(PolygonSftpLogin::distro()),
        PolygonSftpLogin::distro(),
        maran_agent_core::utils::free_space_floor::FreeSpaceFloor::default_floor(),
    );

    let plain = PolygonAccount::create("polyunlockplain");
    let plain_before = shadow_password_field(plain.name().as_str());
    assert!(
        !plain_before.is_empty(),
        "a fresh account must not have an EMPTY password field: that is a login that \
         authenticates with no password at all"
    );
    operations
        .suspend(plain.name())
        .unwrap_or_else(|error| panic!("suspending the passwordless account: {error}"));
    operations.unsuspend(plain.name()).unwrap_or_else(|error| {
        panic!(
            "reactivating a passwordless account must succeed on BOTH families, and the \
             call this used to make exits 1 on the RHEL one: {error}"
        )
    });
    assert_eq!(
        shadow_password_field(plain.name().as_str()),
        plain_before,
        "a resume must not invent a credential for an account that never had one"
    );

    let credited = PolygonAccount::create("polyunlockcred");
    set_account_password(
        credited.name().as_str(),
        PolygonSftpLogin::CUSTOMER_PASSWORD,
    );
    let credited_before = shadow_password_field(credited.name().as_str());
    assert!(
        !credited_before.starts_with('!'),
        "the hand-set password must be USABLE before the suspension, or the lock below \
         proves nothing: {credited_before}"
    );

    operations
        .suspend(credited.name())
        .unwrap_or_else(|error| panic!("suspending the credited account: {error}"));
    let while_suspended = shadow_password_field(credited.name().as_str());
    assert!(
        while_suspended.starts_with('!'),
        "a suspension must really lock a password that really worked: {while_suspended}"
    );

    operations
        .unsuspend(credited.name())
        .unwrap_or_else(|error| panic!("reactivating the credited account: {error}"));
    assert_eq!(
        shadow_password_field(credited.name().as_str()),
        credited_before,
        "the resume must give back exactly the password the suspension took away: a lock \
         that cannot be lifted is a customer locked out for good"
    );
}

#[test]
#[ignore = "creates two real accounts and reads the real shadow database: polygon only"]
fn the_attestation_separates_a_hosting_login_from_one_locked_over_a_password() {
    // P8. The premise the PANEL's two handlers now rest on, measured on a real
    // host instead of argued from a fixture.
    //
    // The attestation carries two facts about one login. `login_locked` comes
    // from `passwd -S` and answers "can a password authenticate this", which is
    // what a suspension must know. `login_password` comes from the raw shadow
    // field and answers "is anything of the customer's held down", which is what
    // a reactivation must know. For an account with a hand-set password the two
    // agree; for an ordinary hosting account — which this agent never gives a
    // password — they do NOT, and the panel refused every reactivation there
    // was for as long as it decided from the first one.
    //
    // So both accounts must report `login_locked == true` here. If they ever
    // stop doing so together, this test is no longer observing the collapse it
    // exists for and says so in its own message.
    let operations = AccountOperations::new(
        ProcessSystemHost::new(PolygonSftpLogin::distro()),
        PolygonSftpLogin::distro(),
        maran_agent_core::utils::free_space_floor::FreeSpaceFloor::default_floor(),
    );
    let sites = ProcessSiteHost::new();
    let cron = ProcessCronHost::new(PolygonSftpLogin::distro());
    let logins = ProcessLoginsHost::new();

    let plain = PolygonAccount::create("polyattestplain");
    operations
        .suspend(plain.name())
        .unwrap_or_else(|error| panic!("suspending the passwordless account: {error}"));
    let plain_state = operations
        .suspension_state(&sites, &cron, &logins, plain.name())
        .unwrap_or_else(|error| panic!("the state must be readable: {error}"));

    let credited = PolygonAccount::create("polyattestcred");
    set_account_password(
        credited.name().as_str(),
        PolygonSftpLogin::CUSTOMER_PASSWORD,
    );
    operations
        .suspend(credited.name())
        .unwrap_or_else(|error| panic!("suspending the credited account: {error}"));
    let credited_state = operations
        .suspension_state(&sites, &cron, &logins, credited.name())
        .unwrap_or_else(|error| panic!("the state must be readable: {error}"));

    assert!(
        plain_state.login_locked && credited_state.login_locked,
        "`passwd -S` must report BOTH as locked, or this host does not collapse the two          states and this test is no longer observing what it exists for"
    );
    assert_eq!(
        plain_state.login_password,
        StoredPassword::Absent,
        "an ordinary hosting account holds markers and no hash: there is nothing to unlock,          and a panel refusing its reactivation refuses every account on the host. The raw          field was {:?}",
        shadow_password_field(plain.name().as_str()),
    );
    assert_eq!(
        credited_state.login_password,
        StoredPassword::Locked,
        "a suspended hand-set password IS held down, and a reactivation must keep refusing          until it comes back"
    );
}

/// Gives `username` a real password, the way an operator would.
///
/// Not through the agent: the point of the account it credits is that the agent
/// did NOT create the credential, and a test that set it through the code under
/// test would be asking that code to agree with itself.
fn set_account_password(username: &str, password: &str) {
    let mut child = Command::new(PolygonSftpLogin::distro().chpasswd_binary())
        .stdin(std::process::Stdio::piped())
        .spawn()
        .expect("chpasswd must run in the polygon");
    child
        .stdin
        .as_mut()
        .expect("the pipe was just opened")
        .write_all(format!("{username}:{password}\n").as_bytes())
        .expect("chpasswd must accept the line");
    let status = child.wait().expect("chpasswd must finish");
    assert!(status.success(), "chpasswd must set the password: {status}");
}

/// The raw shadow password field the host holds for `username`.
///
/// Read with `getent`, the same instrument the operation uses, but through a
/// separate process here rather than through the code under test: the question
/// is what the HOST ended up holding, and asking `AccountOperations` would be
/// asking the change to confirm itself.
///
/// This is the byte string PAM consults, so an assertion on it is an assertion
/// about whether a password can authenticate — not a proxy for one.
fn shadow_password_field(username: &str) -> String {
    let entry = Command::new(PolygonSftpLogin::distro().getent_binary())
        .arg("shadow")
        .arg(username)
        .output()
        .expect("getent must run in the polygon");
    assert!(
        entry.status.success(),
        "the account must have a shadow entry: {}",
        String::from_utf8_lossy(&entry.stderr)
    );
    String::from_utf8_lossy(&entry.stdout)
        .lines()
        .next()
        .unwrap_or_default()
        .split(':')
        .nth(1)
        .unwrap_or_default()
        .to_owned()
}

#[test]
#[ignore = "holds two real SFTP sessions open across a real suspension: polygon only"]
fn an_authenticated_sftp_session_is_ended_when_the_account_is_suspended() {
    // THIS TEST WAS RE-PINNED, DELIBERATELY, AND ITS PREVIOUS NAME SAID THE
    // OPPOSITE: `an_authenticated_sftp_session_keeps_transferring_after_the_
    // account_is_suspended`. That test measured today's behaviour and said in its
    // own message what a red line would mean — "the agent culls the account's
    // processes ... re-pin this test to the NEW behaviour instead of deleting it".
    // This is that re-pin. The owner asked for option 3 of
    // `docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md`
    // § "The options, their costs, and a recommendation", and the cull is argued
    // in `docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md`.
    //
    // What is measured, and why each part is here rather than assumed:
    //
    // * the session authenticates and TRANSFERS before the suspension — without
    //   that, a failure afterwards would be about this client;
    // * a NEW login is refused at the same moment — the old half of the promise,
    //   which the cull must not be allowed to replace;
    // * the OPEN session stops being served — the finding;
    // * a SECOND ACCOUNT'S open session goes on transferring after this
    //   account's suspension. That is the inverse control the threat note's
    //   reviewer list demands ("the cull cannot reach a uid other than the
    //   account's") AND the proof that the daemon was still serving sessions at
    //   the moment the first one died, so the death is the suspension's doing and
    //   not the harness's.
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftplive");
    PolygonSftpLogin::plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let neighbour = PolygonAccount::create("polysftpnear");
    PolygonSftpLogin::plant_file(&neighbour);
    let neighbour_login = PolygonSftpLogin::create(&neighbour);
    let operations = AccountOperations::new(
        ProcessSystemHost::new(PolygonSftpLogin::distro()),
        PolygonSftpLogin::distro(),
        maran_agent_core::utils::free_space_floor::FreeSpaceFloor::default_floor(),
    );

    let (mut session, greeting) =
        SftpControlSession::login(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD);
    assert!(
        greeting.contains("Remote working directory:"),
        "the session must be authenticated BEFORE the suspension, or nothing below \
         is about a session that outlived one: {greeting}"
    );
    let (mut neighbour_session, neighbour_greeting) =
        SftpControlSession::login(neighbour_login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD);
    assert!(
        neighbour_greeting.contains("Remote working directory:"),
        "the neighbour's session must be authenticated too, or it cannot survive \
         anything: {neighbour_greeting}"
    );

    // The control that makes a later failure mean something: this client can
    // transfer through this session while the account is in good standing.
    let before = fetch_through(&mut session, "before");
    assert_eq!(
        before,
        PolygonSftpLogin::CUSTOMER_CONTENT,
        "the open session must be able to transfer before the suspension, or a \
         failure afterwards would be about this client and not about the suspension"
    );

    // The two halves of a suspension, in the order the panel drives them. The
    // cull is inside the second one: locking a shadow field is read at
    // AUTHENTICATION and reaches no session that is already open.
    operations
        .suspend(account.name())
        .unwrap_or_else(|error| panic!("suspending the account must succeed: {error}"));
    let culled = set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        true,
    )
    .unwrap_or_else(|error| panic!("locking the account's logins must succeed: {error}"));

    // The count, measured against a real `pkill` on a real host rather than
    // against a fake's arithmetic. It is what the operator's attestation states,
    // so it has to be a number this account's own processes could produce: at
    // least the session asserted open above, and never the neighbour's, whose
    // session is asserted to go on transferring below.
    let ended = culled
        .sessions_ended
        .expect("the locking direction must answer WITH a count, not merely succeed");
    assert!(
        ended >= 1,
        "the cull must report the session this test held open: got {ended}"
    );

    // The control that makes the rest of this test mean something: the
    // suspension REALLY landed on this host at this moment.
    assert!(
        shadow_password_field(login.name()).starts_with('!'),
        "the suspension must have marked the shadow field before this test can \
         claim anything about the session behind it"
    );
    let new_session = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !new_session.status.success(),
        "a NEW session with the same credential must be refused while the account \
         is suspended, or the suspension is not in force at all:\n{}",
        PolygonSftpLogin::said(&new_session)
    );

    // THE FINDING. The daemon no longer serves the session that was open: the
    // process carrying it ran as the account's uid and the suspension ended it,
    // so the client's channel is gone and the `pwd` this type terminates every
    // reply with can never come back.
    // The question is asked by MOVING BYTES and not by `pwd`: the openssh client
    // answers `pwd` out of its own cached state, so it keeps answering after the
    // daemon has closed the connection (measured — see
    // `fixtures/sftp_control_session.rs`). A transfer cannot be faked that way:
    // either the file lands or the session is gone.
    let (after, landed) = fetch_attempt(&mut session, "after");
    assert!(
        landed.is_none(),
        "MEASURED: suspending an account ends the SFTP session it already had \
         open. THIS LINE GOING RED means a suspended customer's live session \
         still moved their data — check that `end_account_sessions` ran, that it \
         named this account's uid, and that `pkill` is installed on this image. \
         The session said: {after}"
    );

    // The cross-tenant inverse control, and the harness's own liveness proof: a
    // session belonging to a DIFFERENT account is untouched and still moves
    // bytes, at the same moment, through the same daemon.
    let neighbour_after = fetch_through(&mut neighbour_session, "neighbour-after");
    assert_eq!(
        neighbour_after,
        PolygonSftpLogin::CUSTOMER_CONTENT,
        "a cull that reached another account's uid would be a cross-tenant \
         denial of service, and a daemon that had stopped serving every session \
         would make the assertion above vacuous"
    );

    // And the reversal, so the account is not left suspended for a later test.
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        false,
    )
    .unwrap_or_else(|error| panic!("unlocking the account's logins must succeed: {error}"));
    operations
        .unsuspend(account.name())
        .unwrap_or_else(|error| panic!("reactivating the account must succeed: {error}"));

    drop(session);
    drop(neighbour_session);
    drop(login);
    drop(neighbour_login);
}

/// Fetches the planted file through an already-open `session` and returns the
/// bytes that landed.
///
/// The assertion is on the RETRIEVED FILE and not on what the client printed: a
/// client that reported `Fetching` and wrote nothing would satisfy a check on
/// its output, and the question is whether a suspended customer's data still
/// moves.
///
/// # Panics
///
/// Panics when the download landed nowhere, quoting what the session said —
/// which is the outcome that would mean the daemon HAD closed the session.
fn fetch_through(session: &mut SftpControlSession, tag: &str) -> String {
    let (said, landed) = fetch_attempt(session, tag);

    landed.unwrap_or_else(|| {
        panic!(
            "the transfer through the open session landed nothing; \
             the session said: {said}"
        )
    })
}

/// Tries to fetch the planted file through `session`, and reports BOTH what the
/// session said and whether any bytes landed.
///
/// The fallible half of [`fetch_through`], and the two are one function because
/// the cull test needs the same transfer to come back EMPTY while every other
/// caller needs it to come back full — the rule of two applied to a measurement
/// whose two directions must not be allowed to drift apart. A test asserting the
/// session is gone and a test asserting it works therefore ask the daemon exactly
/// the same question.
///
/// `probe_command` rather than `command`, because after the cull the client
/// process is dead and the WRITE is what fails; `command` panics there by design.
fn fetch_attempt(session: &mut SftpControlSession, tag: &str) -> (String, Option<String>) {
    let landing = std::env::temp_dir().join(format!("maran-sftp-live-{tag}.txt"));
    let _ = std::fs::remove_file(&landing);

    let said = session.probe_command(&format!(
        "get /home/{} {}",
        PolygonSftpLogin::CUSTOMER_FILE,
        landing.to_str().expect("a utf-8 path")
    ));

    (said, std::fs::read_to_string(&landing).ok())
}
