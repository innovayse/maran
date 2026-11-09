//! A traffic summary onto its wire payload.

use maran_ops::sites::TrafficSummary;

use crate::proto::{SummariseSiteTrafficOk, TrafficCursor, TrafficDay, TrafficPath, TrafficStatus};

/// Builds the ok payload of `SummariseSiteTraffic`.
///
/// A site with no access log yet has no cursor, and the payload then carries
/// none: an absent message is how the contract says "start from the beginning",
/// and an empty one would be read as a file named `""` at offset zero.
#[must_use]
pub fn to_wire_traffic_summary(summary: TrafficSummary) -> SummariseSiteTrafficOk {
    SummariseSiteTrafficOk {
        days: summary
            .days
            .into_iter()
            .map(|day| TrafficDay {
                date: day.date.to_iso(),
                requests: day.requests,
                body_bytes: day.body_bytes,
                statuses: day
                    .statuses
                    .into_iter()
                    .map(|status| TrafficStatus {
                        code: u32::from(status.code),
                        requests: status.requests,
                    })
                    .collect(),
                paths: day
                    .paths
                    .into_iter()
                    .map(|path| TrafficPath {
                        path: path.path,
                        requests: path.requests,
                    })
                    .collect(),
                other_path_requests: day.other_path_requests,
            })
            .collect(),
        cursor: summary.cursor.map(|cursor| TrafficCursor {
            file_id: cursor.file_id,
            offset: cursor.offset,
        }),
        caught_up: summary.caught_up,
        gap: summary.gap,
        unparsed_lines: summary.unparsed_lines,
    }
}

#[cfg(test)]
#[path = "../../tests/services/sites/to_wire_traffic_summary_tests.rs"]
mod tests;
