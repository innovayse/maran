//! Tests for [`DeadlineSender`]: a closed channel and a stalled one are different
//! endings, a slow reader is waited for, and the deadline is observed on a
//! virtual clock without a single sleep.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use tokio::sync::mpsc;

use super::{DeadlineSender, RETRY_INTERVAL};
use crate::services::wire::send_end::SendEnd;
use crate::services::wire::sink_clock::SinkClock;

/// The deadline the stall tests run against; nothing waits it out in real time.
const PATIENCE: Duration = Duration::from_secs(30);

/// How much virtual time the clock has been asked to wait, shared with the test.
type Waited = Arc<Mutex<Duration>>;

/// A clock that answers with virtual time and never sleeps.
struct VirtualClock {
    /// The instant virtual time started from.
    start: Instant,
    /// How far virtual time has advanced.
    waited: Waited,
}

impl SinkClock for VirtualClock {
    fn now(&mut self) -> Instant {
        self.start + *self.waited.lock().unwrap()
    }

    fn wait(&mut self, interval: Duration) {
        *self.waited.lock().unwrap() += interval;
    }
}

/// A sender on a fresh virtual clock, with the handle the test reads time from.
fn sender_on_virtual_time(channel: mpsc::Sender<u32>) -> (DeadlineSender<u32>, Waited) {
    let waited: Waited = Arc::new(Mutex::new(Duration::ZERO));
    let clock = VirtualClock {
        start: Instant::now(),
        waited: Arc::clone(&waited),
    };
    (
        DeadlineSender::with_patience_on_clock(channel, PATIENCE, Box::new(clock)),
        waited,
    )
}

#[tokio::test]
async fn a_message_with_room_in_the_channel_is_delivered_without_waiting() {
    let (channel, mut receiver) = mpsc::channel(2);
    let (mut sender, waited) = sender_on_virtual_time(channel);

    assert_eq!(sender.send(7), Ok(()));

    assert_eq!(receiver.recv().await, Some(7));
    assert_eq!(*waited.lock().unwrap(), Duration::ZERO);
    assert!(sender.is_open());
}

#[tokio::test]
async fn a_closed_channel_is_reported_as_closed() {
    let (channel, receiver) = mpsc::channel(1);
    drop(receiver);
    let (mut sender, _waited) = sender_on_virtual_time(channel);

    assert_eq!(sender.send(1), Err(SendEnd::Closed));
    assert!(!sender.is_open());
}

#[tokio::test]
async fn a_channel_nobody_drains_is_stalled_exactly_at_the_deadline() {
    let (channel, _receiver) = mpsc::channel(1);
    let (mut sender, waited) = sender_on_virtual_time(channel);
    assert_eq!(sender.send(1), Ok(()));

    assert_eq!(sender.send(2), Err(SendEnd::Stalled));

    let waited = *waited.lock().unwrap();
    assert!(waited >= PATIENCE, "gave up early: {waited:?}");
    assert!(
        waited < PATIENCE + RETRY_INTERVAL,
        "gave up late: {waited:?}"
    );
}
