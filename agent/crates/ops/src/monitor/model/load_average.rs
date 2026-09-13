//! The kernel's run-queue averages.

/// How many averages the line begins with, and how many this reads.
const AVERAGE_COUNT: usize = 3;

/// The three load averages the kernel keeps.
///
/// A load average is not a percentage and does not divide by the processor
/// count: on a machine with sixteen processors a load of 4 is quiet and on a
/// machine with two it is not. The agent reports the raw numbers and leaves
/// that comparison to the panel, which is where the host's processor count is
/// already known.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct LoadAverage {
    /// The average over the last minute.
    pub one_minute: f64,
    /// The average over the last five minutes.
    pub five_minutes: f64,
    /// The average over the last fifteen minutes.
    pub fifteen_minutes: f64,
}

impl LoadAverage {
    /// Reads the kernel's load-average line.
    ///
    /// The line is `<1m> <5m> <15m> <running>/<total> <last pid>`; only the
    /// three averages are read. The running/total field is a snapshot of the
    /// process table rather than a load, and the last pid is not a measurement
    /// at all.
    ///
    /// Returns `None` when the first three fields are not three loads.
    /// Reporting zeroes instead would be indistinguishable from an idle host,
    /// which is the one reading nobody would look into.
    ///
    /// "Not a load" is stricter than "not parseable as a float", and
    /// deliberately: Rust's float parser accepts `nan`, `inf` and `-5`, so a
    /// bare `parse::<f64>()` here would answer `Some(NaN)` for text this
    /// function's own documentation promises to refuse. A `NaN` is the worst
    /// possible answer to give a chart — it survives every comparison the panel
    /// makes and reaches the page as a hole — which is the same hazard
    /// [`crate::monitor::model::cpu_times::CpuTimes::busy_percent_since`] goes
    /// out of its way to avoid, so this parser is held to the same standard. A
    /// negative load is refused for the same reason: a run queue cannot be
    /// shorter than empty. The Linux kernel prints this file in fixed point and
    /// emits none of them, so nothing about a real host reaches these
    /// branches — which is exactly why they must be a check rather than an
    /// assumption.
    #[must_use]
    pub fn parse(loadavg: &str) -> Option<Self> {
        let averages: Vec<f64> = loadavg
            .split_whitespace()
            .take(AVERAGE_COUNT)
            .map(|field| {
                field
                    .parse::<f64>()
                    .ok()
                    .filter(|average| average.is_finite() && *average >= 0.0)
            })
            .collect::<Option<Vec<f64>>>()?;
        if averages.len() < AVERAGE_COUNT {
            return None;
        }

        Some(Self {
            one_minute: *averages.first()?,
            five_minutes: *averages.get(1)?,
            fifteen_minutes: *averages.get(2)?,
        })
    }
}

#[cfg(test)]
#[path = "../../tests/monitor/load_average_tests.rs"]
mod tests;
