//! One UTC day's deltas.

use super::traffic_date::TrafficDate;
use super::traffic_path_count::TrafficPathCount;
use super::traffic_status_count::TrafficStatusCount;

/// What one call read about one UTC day.
///
/// A delta and not a total: the panel adds it to what it already holds, which
/// is what makes a day split across two calls (a rotation, a byte budget)
/// come out right.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrafficDay {
    /// The day.
    pub date: TrafficDate,
    /// Requests counted.
    pub requests: u64,
    /// Response body bytes sent, summed.
    pub body_bytes: u64,
    /// Requests per status code, at most 64 distinct codes, ordered by code.
    pub statuses: Vec<TrafficStatusCount>,
    /// The most requested paths, highest first, ties by path.
    pub paths: Vec<TrafficPathCount>,
    /// Requests whose path is not in `paths`.
    pub other_path_requests: u64,
}
