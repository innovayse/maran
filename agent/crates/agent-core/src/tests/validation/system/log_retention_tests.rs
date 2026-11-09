//! What a log retention accepts and refuses.

use super::{LogRetention, MAXIMUM};
use crate::validation::system::log_retention_error::LogRetentionError;

#[test]
fn every_figure_from_one_to_the_maximum_is_accepted() {
    for days in 1..=MAXIMUM {
        assert_eq!(LogRetention::parse(days).map(|r| r.value()), Ok(days));
    }
}

#[test]
fn zero_is_refused_because_an_absent_field_decodes_to_it() {
    assert_eq!(LogRetention::parse(0), Err(LogRetentionError::Zero));
}

#[test]
fn a_figure_above_the_installers_policy_is_refused() {
    assert_eq!(
        LogRetention::parse(MAXIMUM + 1),
        Err(LogRetentionError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM
        })
    );
    assert!(LogRetention::parse(u32::MAX).is_err());
}

#[test]
fn the_maximum_is_the_rotate_count_of_the_shipped_policy() {
    let policy = include_str!("../../../../../../../installer/logrotate/maran-sites");
    assert!(
        policy.contains(&format!("rotate {MAXIMUM}")),
        "installer/logrotate/maran-sites must say `rotate {MAXIMUM}`"
    );
}
