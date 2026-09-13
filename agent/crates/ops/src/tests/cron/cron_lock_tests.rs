//! Two cron mutations that overlap are serialised, and the account stays
//! suspended.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;
use std::thread;

use maran_agent_core::validation::system::name::AccountName;

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::model::crontab_document::CrontabDocument;
use crate::cron::recording_cron_host::{RecordingCronHost, command, distro, every_five_minutes};
use crate::cron::set_account_cron_suspended::set_account_cron_suspended;

/// A validated account name, for a test that needs one of its own.
///
/// The lock registry is a `static`, so it is shared by every test in this
/// binary and cargo runs them in parallel. A test that used the folder's shared
/// `alice` would contend with whatever else was mid-operation on `alice`, and
/// the two timing assertions below would then be reading another test's lock
/// rather than their own. Each test here names accounts nothing else uses.
fn named(name: &str) -> AccountName {
    AccountName::parse(name).expect("a valid account name")
}

/// A suspension that overlaps a creation survives it, and both operations ran.
///
/// **This is the race the audit named, driven rather than argued.** Every
/// mutating operation in this area reads the whole crontab, changes one thing
/// in memory and installs a freshly rendered whole table. The render normalises
/// EVERY managed line to the document's suspension flag, so a creation that
/// read the table before the suspension installed writes `suspended = false`
/// back over the entire account — while the suspension has already answered
/// `Ok` and the panel has already recorded the account as suspended. There is
/// no second copy of that fact anywhere: the panel keeps no cron rows, so
/// nothing would ever notice, and the customer's jobs keep firing.
///
/// The fake parks the first caller between its read and its install
/// (`with_arrival_gate`), so the interleaving happens on every run rather than
/// on the runs where the scheduler cooperates. Without the per-account lock the
/// partner arrives immediately, both hold the same pre-suspension document, and
/// whichever installs last decides the account's whole state.
///
/// # The positive control
///
/// An assertion that the account is suspended is worth nothing if the second
/// operation never started, so three things are asserted beside it, and each of
/// them is false for a test that measured nothing:
///
/// - **Both operations answered `Ok`** — the creation returned an id and the
///   suspension returned `Ok(())`.
/// - **Both reached the critical section**, which the gate counts: `arrivals`
///   is `2` only if two callers really got as far as reading the crontab
///   inside their operation.
/// - **The rendezvous timed out**, which is what says the second was held
///   OUTSIDE that section while the first was in it. The count alone cannot
///   say so — a caller parked on the lock still arrives, one release later —
///   so this is the axis that would go blind if the lock were removed, and it
///   is asserted rather than assumed.
/// - **The created entry is in the final table**, so the creation was not
///   merely started but landed. A lock that made the creation lose its write
///   would satisfy "still suspended" and be a different defect.
#[test]
fn a_creation_that_overlaps_a_suspension_cannot_unsuspend_the_account() {
    let host = Arc::new(RecordingCronHost::new().with_arrival_gate());
    let account = named("cronlockrace");

    let creating = {
        let host = Arc::clone(&host);

        let account = account.clone();

        thread::spawn(move || {
            create_cron_entry(
                host.as_ref(),
                distro(),
                &account,
                &every_five_minutes(),
                &command("echo racing"),
                None,
            )
        })
    };
    let suspending = {
        let host = Arc::clone(&host);

        thread::spawn(move || set_account_cron_suspended(host.as_ref(), distro(), &account, true))
    };

    let created = creating
        .join()
        .expect("the creating thread finished")
        .expect("the creation must succeed");
    suspending
        .join()
        .expect("the suspending thread finished")
        .expect("the suspension must succeed");

    assert_eq!(
        host.arrivals(),
        2,
        "both operations must have entered the crontab read-modify-write; \
         one arrival means the race never happened and nothing was measured"
    );

    let table = host.crontab().expect("a table must be installed");
    let document = CrontabDocument::parse(&table);

    assert!(
        document.is_suspended(),
        "the suspension answered Ok, so the installed table must say the \
         account is suspended:\n{table}"
    );
    assert_eq!(
        document.entries().len(),
        1,
        "the creation answered Ok with an id, so its entry must be in the \
         table:\n{table}"
    );
    assert_eq!(document.entries()[0].id, created);
    assert!(
        document.entries()[0].suspended,
        "the entry created during the suspension must carry the suspension \
         marker, or cron can still see its schedule:\n{table}"
    );

    assert!(
        host.rendezvous_timed_out(),
        "the second operation must have been held outside the read-modify-write \
         while the first was inside it; a completed rendezvous means both were \
         in it at once, which is the race itself"
    );
}

/// Two accounts' cron mutations do not wait for each other.
///
/// The lock is per account, and this is what says so rather than the doc
/// comment saying it. The two callers MEET at the gate — the rendezvous does
/// not time out — which can only happen while both are inside their own
/// `read_crontab` at the same time. A process-wide lock would hold the second
/// outside it until the first had finished, the first would wait out its budget
/// alone, and this assertion would fail.
///
/// The arrival count is asserted too, as the control: a rendezvous that did not
/// time out is also what a test where neither caller ever arrived would report,
/// and `arrivals == 2` is what rules that out.
///
/// The fake keeps one crontab whatever account it is asked about, which is
/// deliberate here: what is under test is the LOCK's reach, not the fake's
/// bookkeeping, and one store makes the two callers contend for everything
/// except the lock.
#[test]
fn two_accounts_do_not_wait_for_each_other() {
    let host = Arc::new(RecordingCronHost::new().with_arrival_gate());
    let names = ["cronlockone", "cronlocktwo"];

    let racers: Vec<_> = names
        .iter()
        .map(|name| {
            let host = Arc::clone(&host);
            let account = named(name);

            thread::spawn(move || {
                set_account_cron_suspended(host.as_ref(), distro(), &account, true)
            })
        })
        .collect();
    for racer in racers {
        racer
            .join()
            .expect("the thread finished")
            .expect("the suspension must succeed");
    }

    assert_eq!(
        host.arrivals(),
        2,
        "both suspensions must have reached the crontab read-modify-write"
    );
    assert!(
        !host.rendezvous_timed_out(),
        "two different accounts must be able to be inside the read-modify-write \
         at the same time; a timed-out rendezvous means one waited for the other"
    );
}
