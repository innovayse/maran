//! How a traffic summary is put on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::sites::{
    TrafficCursor, TrafficDate, TrafficDay, TrafficPathCount, TrafficStatusCount, TrafficSummary,
};

use super::to_wire_traffic_summary;

fn summary(cursor: Option<TrafficCursor>) -> TrafficSummary {
    TrafficSummary {
        days: vec![TrafficDay {
            date: TrafficDate::from_local(2026, 11, 7, 0, 0).unwrap(),
            requests: 5,
            body_bytes: 900,
            statuses: vec![TrafficStatusCount {
                code: 404,
                requests: 2,
            }],
            paths: vec![TrafficPathCount {
                path: "/a".to_owned(),
                requests: 3,
            }],
            other_path_requests: 2,
        }],
        cursor,
        caught_up: false,
        gap: true,
        unparsed_lines: 4,
    }
}

#[test]
fn every_figure_reaches_the_wire() {
    let wire = to_wire_traffic_summary(summary(Some(TrafficCursor {
        file_id: "7:1:00".to_owned(),
        offset: 77,
    })));

    assert_eq!(wire.days.len(), 1);
    assert_eq!(wire.days[0].date, "2026-11-07");
    assert_eq!(wire.days[0].requests, 5);
    assert_eq!(wire.days[0].body_bytes, 900);
    assert_eq!(wire.days[0].statuses[0].code, 404);
    assert_eq!(wire.days[0].statuses[0].requests, 2);
    assert_eq!(wire.days[0].paths[0].path, "/a");
    assert_eq!(wire.days[0].paths[0].requests, 3);
    assert_eq!(wire.days[0].other_path_requests, 2);
    let cursor = wire.cursor.unwrap();
    assert_eq!((cursor.file_id.as_str(), cursor.offset), ("7:1:00", 77));
    assert!(!wire.caught_up);
    assert!(wire.gap);
    assert_eq!(wire.unparsed_lines, 4);
}

#[test]
fn a_site_with_no_cursor_sends_none_and_not_an_empty_one() {
    assert!(to_wire_traffic_summary(summary(None)).cursor.is_none());
}
