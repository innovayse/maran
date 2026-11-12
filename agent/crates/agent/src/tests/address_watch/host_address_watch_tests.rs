//! Tests for [`HostAddressWatch`]: both layers follow one reading, nothing is
//! written when nothing changed, and a failure is retried alone.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::host_addresses_error::HostAddressesError;
use maran_ops::mail::MailError;

use super::HostAddressWatch;
use crate::address_watch::host_address_layer::HostAddressLayer;
use crate::address_watch::host_address_layer_error::HostAddressLayerError;
use crate::address_watch::host_address_layer_outcome::HostAddressLayerOutcome;
use crate::address_watch::host_address_source::HostAddressSource;
use crate::address_watch::watch_tick::WatchTick;

/// A source whose answer a test changes between ticks.
#[derive(Clone)]
struct FakeSource(Arc<Mutex<Option<HostAddresses>>>);

impl FakeSource {
    fn reading(addresses: &[&str]) -> Self {
        Self(Arc::new(Mutex::new(Some(host(addresses)))))
    }

    fn set(&self, addresses: &[&str]) {
        *self.0.lock().unwrap() = Some(host(addresses));
    }

    fn become_unreadable(&self) {
        *self.0.lock().unwrap() = None;
    }
}

impl HostAddressSource for FakeSource {
    fn read(&self) -> Result<HostAddresses, HostAddressesError> {
        self.0
            .lock()
            .unwrap()
            .clone()
            .ok_or(HostAddressesError::Ipv4Missing)
    }
}

/// A layer that records what it was brought to, and fails while told to.
#[derive(Clone)]
struct FakeLayer {
    name: &'static str,
    followed: Arc<Mutex<Vec<HostAddresses>>>,
    failing: Arc<Mutex<bool>>,
    pending: Arc<Mutex<bool>>,
}

impl FakeLayer {
    fn named(name: &'static str) -> Self {
        Self {
            name,
            followed: Arc::new(Mutex::new(Vec::new())),
            failing: Arc::new(Mutex::new(false)),
            pending: Arc::new(Mutex::new(false)),
        }
    }

    /// While set, the layer has nothing it can follow (no firewall loaded, Postfix
    /// stopped) and says so.
    fn be_pending(&self, pending: bool) {
        *self.pending.lock().unwrap() = pending;
    }

    fn fail(&self, failing: bool) {
        *self.failing.lock().unwrap() = failing;
    }

    fn followed(&self) -> Vec<HostAddresses> {
        self.followed.lock().unwrap().clone()
    }
}

impl HostAddressLayer for FakeLayer {
    fn name(&self) -> &'static str {
        self.name
    }

    fn follow(
        &self,
        addresses: &HostAddresses,
    ) -> Result<HostAddressLayerOutcome, HostAddressLayerError> {
        if *self.pending.lock().unwrap() {
            return Ok(HostAddressLayerOutcome::Pending);
        }
        if *self.failing.lock().unwrap() {
            return Err(HostAddressLayerError::Mail(MailError::Render {
                reason: String::new(),
            }));
        }
        self.followed.lock().unwrap().push(addresses.clone());

        Ok(HostAddressLayerOutcome::Followed)
    }
}

fn host(addresses: &[&str]) -> HostAddresses {
    HostAddresses {
        addresses: addresses.iter().map(|text| text.parse().unwrap()).collect(),
        local_prefixes: Vec::new(),
    }
}

fn watch_over(source: &FakeSource, firewall: &FakeLayer, mail: &FakeLayer) -> HostAddressWatch {
    HostAddressWatch::new(
        Box::new(source.clone()),
        vec![Box::new(firewall.clone()), Box::new(mail.clone())],
    )
}

/// Startup: the first tick brings both layers to the present addresses.
#[test]
fn the_first_tick_brings_both_layers_to_the_present_addresses() {
    let source = FakeSource::reading(&["192.0.2.10"]);
    let (firewall, mail) = (FakeLayer::named("firewall"), FakeLayer::named("mail"));
    let mut watch = watch_over(&source, &firewall, &mail);

    assert_eq!(
        watch.tick(),
        WatchTick::Followed {
            asked: 2,
            failed: 0,
            pending: 0
        }
    );
    assert_eq!(firewall.followed().len(), 1);
    assert_eq!(mail.followed().len(), 1);
}

/// The issue: an address added later reaches BOTH layers, from one reading.
#[test]
fn an_address_added_later_reaches_both_layers_with_the_same_reading() {
    let source = FakeSource::reading(&["192.0.2.10"]);
    let (firewall, mail) = (FakeLayer::named("firewall"), FakeLayer::named("mail"));
    let mut watch = watch_over(&source, &firewall, &mail);
    watch.tick();

    source.set(&["192.0.2.10", "198.51.100.7"]);
    let tick = watch.tick();

    assert_eq!(
        tick,
        WatchTick::Followed {
            asked: 2,
            failed: 0,
            pending: 0
        }
    );
    assert_eq!(firewall.followed().len(), 2);
    assert_eq!(firewall.followed().last(), mail.followed().last());
    assert!(
        firewall
            .followed()
            .last()
            .unwrap()
            .addresses
            .contains(&"198.51.100.7".parse().unwrap())
    );
}

/// Inverse control: an unchanged host writes nothing, and the kernel listing the
/// same addresses in another order is not a change.
#[test]
fn an_unchanged_host_asks_no_layer_to_write() {
    let source = FakeSource::reading(&["192.0.2.10", "198.51.100.7"]);
    let (firewall, mail) = (FakeLayer::named("firewall"), FakeLayer::named("mail"));
    let mut watch = watch_over(&source, &firewall, &mail);
    watch.tick();

    assert_eq!(watch.tick(), WatchTick::NothingChanged);
    source.set(&["198.51.100.7", "192.0.2.10"]);
    assert_eq!(watch.tick(), WatchTick::NothingChanged);

    assert_eq!(firewall.followed().len(), 1);
    assert_eq!(mail.followed().len(), 1);
}

/// Unreadable addresses are not an empty host: nothing is written, and once the
/// tables read again the layers are not re-written if nothing changed meanwhile.
#[test]
fn unreadable_addresses_write_nothing_and_change_nothing() {
    let source = FakeSource::reading(&["192.0.2.10"]);
    let (firewall, mail) = (FakeLayer::named("firewall"), FakeLayer::named("mail"));
    let mut watch = watch_over(&source, &firewall, &mail);
    watch.tick();

    source.become_unreadable();
    assert_eq!(watch.tick(), WatchTick::SourceUnreadable);
    assert_eq!(firewall.followed().len(), 1);
    assert_eq!(mail.followed().len(), 1);

    source.set(&["192.0.2.10"]);
    assert_eq!(watch.tick(), WatchTick::NothingChanged);
}

/// A source that is unreadable from the start leaves both layers untouched.
#[test]
fn a_host_that_has_never_been_readable_is_never_written() {
    let source = FakeSource::reading(&[]);
    source.become_unreadable();
    let (firewall, mail) = (FakeLayer::named("firewall"), FakeLayer::named("mail"));
    let mut watch = watch_over(&source, &firewall, &mail);

    assert_eq!(watch.tick(), WatchTick::SourceUnreadable);
    assert!(firewall.followed().is_empty());
    assert!(mail.followed().is_empty());
}

/// A layer that fails is asked again at the next tick and alone; the one that
/// succeeded is not written twice.
#[test]
fn a_failing_layer_is_retried_alone() {
    let source = FakeSource::reading(&["192.0.2.10"]);
    let (firewall, mail) = (FakeLayer::named("firewall"), FakeLayer::named("mail"));
    mail.fail(true);
    let mut watch = watch_over(&source, &firewall, &mail);

    assert_eq!(
        watch.tick(),
        WatchTick::Followed {
            asked: 2,
            failed: 1,
            pending: 0
        }
    );
    assert_eq!(
        watch.tick(),
        WatchTick::Followed {
            asked: 1,
            failed: 1,
            pending: 0
        }
    );
    mail.fail(false);
    assert_eq!(
        watch.tick(),
        WatchTick::Followed {
            asked: 1,
            failed: 0,
            pending: 0
        }
    );
    assert_eq!(watch.tick(), WatchTick::NothingChanged);

    assert_eq!(firewall.followed().len(), 1);
    assert_eq!(mail.followed().len(), 1);
}

/// D1: a layer that had nothing it could follow is NOT recorded as current. It is asked
/// again at every tick, and once it can follow it does and is then left alone.
#[test]
fn a_pending_layer_is_asked_again_until_it_really_follows() {
    let source = FakeSource::reading(&["192.0.2.10"]);
    let (firewall, mail) = (FakeLayer::named("firewall"), FakeLayer::named("mail"));
    mail.be_pending(true);
    let mut watch = watch_over(&source, &firewall, &mail);

    assert_eq!(
        watch.tick(),
        WatchTick::Followed {
            asked: 2,
            failed: 0,
            pending: 1
        }
    );
    // The host did not change, and still the pending layer is asked; the one that
    // followed is not.
    assert_eq!(
        watch.tick(),
        WatchTick::Followed {
            asked: 1,
            failed: 0,
            pending: 1
        }
    );
    mail.be_pending(false);
    assert_eq!(
        watch.tick(),
        WatchTick::Followed {
            asked: 1,
            failed: 0,
            pending: 0
        }
    );
    assert_eq!(watch.tick(), WatchTick::NothingChanged);

    assert_eq!(firewall.followed().len(), 1);
    assert_eq!(mail.followed().len(), 1);
}
