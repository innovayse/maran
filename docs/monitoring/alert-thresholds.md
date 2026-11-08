# Alert thresholds

The percentages at which the panel tells the operator that the disk, the processor or the memory of
the server is in trouble (issue #142). Administrators only; there is no tenant dimension and no API
key or reseller reaches it.

## What can be set

| Condition | Alarm when | Default | Can be switched off |
|---|---|---|---|
| Disk (`DiskUsage`) | the root filesystem is fuller than the figure | 90 | no |
| Processor (`CpuUsage`) | CPU utilisation across all cores is above the figure | off | yes |
| Memory (`MemoryUsage`) | memory in use is above the figure | off | yes |

- Each figure is a whole number from 50 to 99. The bounds are the entity's own rule
  (`AlertThresholds`) and the validator answers `AlertThresholdOutOfRange` (400); a database check
  constraint holds a row written any other way to the same bounds.
- "Above" is strict: a disk at exactly its threshold is not an alarm.
- The processor and the memory start **off** so an upgrade starts no alert mail nobody asked for. A
  panel nobody has configured stores no row; every reader falls back to the defaults.
- "Memory in use" is the total minus the kernel's available estimate (`MemAvailable`), never the
  total minus what is merely free, so a full page cache does not alarm
  (`agent/crates/ops/src/monitor/model/memory_usage.rs`).

## When an alert fires

The rule is the one every alert follows (`AlertState`): ten consecutive one-minute samples above the
threshold raise one alert, one mail and one `AlertRaised` journal entry; the first sample back below
it resolves it. A spike of a minute or two is not an alert.

Switching the processor or the memory threshold off while its alert is firing resolves it: the next
round records one healthy observation, so the operator is told it ended instead of the alert simply
going quiet. A threshold that is off creates no alert row.

## API and journal

- `GET /api/v1/monitoring/alert-thresholds` returns the three figures (`null` for an unwatched
  processor or memory) and the bounds the screen may offer.
- `PUT /api/v1/monitoring/alert-thresholds` replaces all three.
- Each save is journalled as `AlertThresholdsSaved` with the figures before and after
  (`disk=90;cpu=off;memory=off -> disk=85;cpu=80;memory=off`), because lowering a threshold is also how
  an alert is silenced.
