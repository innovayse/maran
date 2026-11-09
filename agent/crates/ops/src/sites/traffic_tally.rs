//! The capped accumulator a traffic summary counts into.

use std::collections::{BTreeMap, HashMap};

use crate::sites::model::parsed_access_line::ParsedAccessLine;
use crate::sites::model::traffic_date::TrafficDate;
use crate::sites::model::traffic_day::TrafficDay;
use crate::sites::model::traffic_path_count::TrafficPathCount;
use crate::sites::model::traffic_status_count::TrafficStatusCount;
use crate::sites::traffic_budget::{
    MAXIMUM_DAYS, MAXIMUM_DISTINCT_PATHS, MAXIMUM_STATUS_CODES, RETURNED_PATHS,
};

/// One UTC day's running counters.
#[derive(Debug, Default)]
struct DayTally {
    /// Requests counted, whatever became of their path or status.
    requests: u64,
    /// Body bytes summed, saturating.
    body_bytes: u64,
    /// Requests per status code; stops gaining codes at the cap.
    statuses: BTreeMap<u16, u64>,
    /// Requests per path; stops gaining paths at the cap.
    paths: HashMap<String, u64>,
}

/// Counts parsed lines into per-day figures that cannot grow past the bounds in
/// `traffic_budget`, however many distinct values the log holds.
///
/// **Memory is bounded by construction, not by trust.** A scanner asking for a
/// million unique URLs fills a day's path map to its cap and every further new
/// path adds to that day's request total only; the totals stay exact and the
/// map stays the size it reached. The same holds for status codes and for the
/// number of days. Requests for a path that already has a counter keep adding
/// to it after the cap, so the busiest paths remain right.
#[derive(Debug, Default)]
pub(crate) struct TrafficTally {
    /// The days seen, ordered oldest first.
    days: BTreeMap<TrafficDate, DayTally>,
    /// Lines that contributed nothing: refused by the tokenizer, over the line
    /// cap, or belonging to a day past [`MAXIMUM_DAYS`].
    unparsed: u32,
}

impl TrafficTally {
    /// An empty tally.
    pub(crate) fn new() -> Self {
        Self::default()
    }

    /// Counts one parsed line. A line whose day would be the one past
    /// [`MAXIMUM_DAYS`] is counted as unparsed instead, so the number of days
    /// held is a fact and not an assumption about nginx's clock.
    pub(crate) fn count(&mut self, line: &ParsedAccessLine) {
        if !self.days.contains_key(&line.date) && self.days.len() >= MAXIMUM_DAYS {
            self.count_unparsed();
            return;
        }

        let day = self.days.entry(line.date).or_default();
        day.requests = day.requests.saturating_add(1);
        day.body_bytes = day.body_bytes.saturating_add(line.body_bytes);

        if let Some(requests) = day.statuses.get_mut(&line.status) {
            *requests = requests.saturating_add(1);
        } else if day.statuses.len() < MAXIMUM_STATUS_CODES {
            day.statuses.insert(line.status, 1);
        }

        if let Some(path) = &line.path {
            if let Some(requests) = day.paths.get_mut(path) {
                *requests = requests.saturating_add(1);
            } else if day.paths.len() < MAXIMUM_DISTINCT_PATHS {
                day.paths.insert(path.clone(), 1);
            }
        }
    }

    /// Counts one line that contributed to no figure.
    pub(crate) fn count_unparsed(&mut self) {
        self.unparsed = self.unparsed.saturating_add(1);
    }

    /// How many lines contributed nothing.
    pub(crate) fn unparsed_lines(&self) -> u32 {
        self.unparsed
    }

    /// The days as deltas, oldest first, each with its status counts by code
    /// and its [`RETURNED_PATHS`] most requested paths (ties broken by path,
    /// so the answer is the same on every run).
    ///
    /// `other_path_requests` is what the returned paths do not account for:
    /// requests with no usable path, paths past the counter cap, and paths
    /// that were counted but ranked below the returned ones.
    pub(crate) fn into_days(self) -> Vec<TrafficDay> {
        self.days
            .into_iter()
            .map(|(date, tally)| {
                let mut ranked: Vec<TrafficPathCount> = tally
                    .paths
                    .into_iter()
                    .map(|(path, requests)| TrafficPathCount { path, requests })
                    .collect();
                ranked.sort_by(|left, right| {
                    right
                        .requests
                        .cmp(&left.requests)
                        .then_with(|| left.path.cmp(&right.path))
                });
                ranked.truncate(RETURNED_PATHS);

                let listed: u64 = ranked.iter().map(|path| path.requests).sum();

                TrafficDay {
                    date,
                    requests: tally.requests,
                    body_bytes: tally.body_bytes,
                    statuses: tally
                        .statuses
                        .into_iter()
                        .map(|(code, requests)| TrafficStatusCount { code, requests })
                        .collect(),
                    paths: ranked,
                    other_path_requests: tally.requests.saturating_sub(listed),
                }
            })
            .collect()
    }
}

#[cfg(test)]
#[path = "../tests/sites/traffic_tally_tests.rs"]
mod tests;
