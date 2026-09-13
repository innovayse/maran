//! The kernel's processor time accounting, as one sample.

/// The name of the line that sums every processor.
const AGGREGATE_LINE: &str = "cpu";

/// Field index, within the aggregate line, of the time spent idle.
const IDLE_FIELD: usize = 3;

/// Field index of the time spent waiting for I/O.
const IOWAIT_FIELD: usize = 4;

/// How many of the line's numbers are summed.
///
/// Eight: `user nice system idle iowait irq softirq steal`. The two that follow
/// on a modern kernel — `guest` and `guest_nice` — are deliberately NOT added,
/// because the kernel has ALREADY counted them inside `user` and `nice`
/// respectively. Adding them again inflates the total, which understates every
/// percentage derived from it. Both polygon images print ten fields and the
/// last two are zero, so the mistake would have been invisible there and wrong
/// on the first customer host that runs a virtual machine.
const SUMMED_FIELDS: usize = 8;

/// A percentage's upper bound, and the clamp's ceiling.
const FULL_PERCENT: f64 = 100.0;

/// One reading of how the processors have spent their time since boot.
///
/// Counters, never rates: the kernel counts upwards and a rate only exists
/// between two readings. Splitting the reading from the arithmetic is what lets
/// the arithmetic be tested against numbers no live machine would produce —
/// a counter that went backwards, or two readings taken in the same tick.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CpuTimes {
    /// Time the processors spent doing work.
    pub busy: u64,
    /// Time the processors spent doing nothing, waiting on I/O included.
    ///
    /// `iowait` counts as idle and not as busy, deliberately. A processor
    /// blocked on a disk is not doing work, and counting that time as
    /// utilisation makes a nightly backup read as a CPU emergency on a panel
    /// whose whole job is to tell those apart.
    pub idle: u64,
}

impl CpuTimes {
    /// Reads the aggregate `cpu` line of the kernel's processor accounting.
    ///
    /// The aggregate line only. The per-processor `cpu0`, `cpu1` … lines that
    /// follow it are skipped, because the panel reports one number for the host
    /// and summing the per-processor lines would count every tick twice.
    ///
    /// Returns `None` when the aggregate line is absent or holds fewer than the
    /// eight fields this reads — a monitor that cannot understand its own input
    /// says so, rather than reporting an arithmetic result derived from zeroes.
    #[must_use]
    pub fn parse(stat: &str) -> Option<Self> {
        let line = stat
            .lines()
            .find(|line| line.split_whitespace().next() == Some(AGGREGATE_LINE))?;

        let fields: Vec<u64> = line
            .split_whitespace()
            .skip(1)
            .take(SUMMED_FIELDS)
            .map(|field| field.parse::<u64>().ok())
            .collect::<Option<Vec<u64>>>()?;
        if fields.len() < SUMMED_FIELDS {
            return None;
        }

        let idle = fields
            .get(IDLE_FIELD)?
            .saturating_add(*fields.get(IOWAIT_FIELD)?);
        let total: u64 = fields
            .iter()
            .fold(0, |sum, field| sum.saturating_add(*field));

        Some(Self {
            busy: total.saturating_sub(idle),
            idle,
        })
    }

    /// Utilisation between `earlier` and this reading, as a percentage.
    ///
    /// Zero when no time passed between the two readings, which is what two
    /// samples taken inside one kernel tick produce: dividing there would give
    /// a `NaN` that survives every comparison and reaches a chart as a hole.
    ///
    /// The result is clamped to 0–100, and the clamp is load-bearing rather
    /// than decorative — a test drives it. Counters really do go backwards on a
    /// real host: a processor returning from an offline state brings its own
    /// accounting back with it, so the idle counter can fall while the busy one
    /// rises. The busy delta then exceeds the total delta and their ratio is
    /// not a percentage of anything. Clamping reports the edge of the range
    /// instead of the several hundred percent the panel would otherwise draw
    /// off the top of its chart.
    #[must_use]
    pub fn busy_percent_since(&self, earlier: &Self) -> f64 {
        let busy = self.busy.saturating_sub(earlier.busy);
        let total = self.total().saturating_sub(earlier.total());
        if total == 0 {
            return 0.0;
        }

        (busy as f64 / total as f64 * FULL_PERCENT).clamp(0.0, FULL_PERCENT)
    }

    /// Every tick this reading accounts for, busy and idle together.
    ///
    /// Kept as the sum of the two halves rather than as a third stored field,
    /// so a reading cannot be constructed whose total disagrees with its own
    /// parts.
    fn total(&self) -> u64 {
        self.busy.saturating_add(self.idle)
    }
}

#[cfg(test)]
#[path = "../../tests/monitor/cpu_times_tests.rs"]
mod tests;
