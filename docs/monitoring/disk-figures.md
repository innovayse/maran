# The root filesystem's disk figures

What the panel reports about the root filesystem, where each number comes from, and which one each
screen, chart and alert uses (issue #77).

## The three figures

The agent asks `statvfs("/")` and splits the filesystem into three parts. Each block count is
multiplied by `f_frsize`, the same field `df` uses.

| Figure | statvfs arithmetic | `df -B1` column | Meaning |
|---|---|---|---|
| used | `(f_blocks - f_bfree) × f_frsize` | `Used` | space the filesystem accounts as occupied |
| reserved | `(f_bfree - f_bavail) × f_frsize` | none (`Size - Used - Avail`) | free space only root may write: 5% of an ext4 filesystem by default, 0 on xfs |
| available | `f_bavail × f_frsize` | `Avail` | space any non-root writer, every hosting account included, may still add |
| total | the sum of the three | `Size` | the filesystem's capacity |

**used + reserved + available = total, always.** A filesystem that reports inconsistent counts, for
example a network filesystem with more free blocks than it has, is clamped (free to at most the
total, available to at most free) so the three parts never sum past the size.

Before this change the panel reported a single "used" figure of `total - available`, which counted
the reserve as used. On a 97.9 GiB ext4 root that read 12.7 GiB where `df` said 7.7 GiB.

## Where each figure appears

| Place | Field | Figure |
|---|---|---|
| agent contract, `HostMetrics` (`proto/agent/v1/monitor.proto`) | `disk_used_bytes`, `disk_reserved_bytes`, `disk_available_bytes`, `disk_total_bytes` | the four above; since contract revision 6 |
| `GET /api/v1/monitoring/metrics` (`HostMetricsDto`) and the dashboard's `resources` | `diskUsedBytes`, `diskReservedBytes`, `diskAvailableBytes`, `diskTotalBytes` | the four above |
| dashboard disk tile, monitoring disk card | stacked bar "In use", "Reserved for the system", "Available" | the three parts; the large figure is used / total |
| `GET /api/v1/monitoring/chart` (`MetricBucketDto`) | `diskUsedBytes` | used, the mean over the bucket; `null` for a bucket holding only readings taken before the update |
| free-space floor alert (`AlertEvaluator`, `FreeSpaceFloorApproaching`) | available, against `free_space_floor_bytes` | raised while available is under twice the floor; see below |
| disk-full alert (`MetricsSampler`) | percentage | `(total - available) / total`, so the reserve counts as full: at the moment available reaches zero every account's write fails, while used alone still reads about 95% |

## The free-space floor

The agent refuses work that writes into a filesystem with less than its free-space floor available
(issue #148). The floor is `--free-space-floor-mib` on the agent's command line, written by the
installer into `/etc/maran/agent.env` as `MARAN_AGENT_FREE_SPACE_FLOOR_MIB` (1024 by default, an
operator's own value kept when the installer is re-run). It is compared with `f_bavail`, the same
"available" figure as above, of the filesystem each write lands on:

| Operation | Filesystem measured | Asked |
|---|---|---|
| `CreateAccount` | the one holding the account homes (`/home`) | after the existence check, before `useradd` |
| `WriteFile`, the only upload path | the one holding the account homes | before any directory is created |
| `CreateBackup` | the account's backup directory, where the artifact lands | after the already-published check, before the scratch or any dump exists |
| `RestoreBackup` | the one holding the account homes | before anything is staged or extracted |

A refusal is `ERROR_CODE_BELOW_FREE_SPACE_FLOOR` on the wire and `AgentBelowFreeSpaceFloor` in the
panel (HTTP 503), with the sentence "The server is nearly out of disk space…" in the reader's
language; a failed backup or restore task shows it as its failure. Nothing has been written when it
is refused. A filesystem the agent cannot measure is refused too, as a system failure.

The agent reports its floor as `HostMetrics.free_space_floor_bytes` (contract revision 7). The panel
raises the `FreeSpaceFloorApproaching` alert when the ROOT filesystem has had less than twice the
floor available for ten consecutive checks, so the operator hears about it while one floor's worth of
room is still left. Two limits: a disk that loses that much within the ten checks reaches the floor
first, and when the account homes or the backup root live on another filesystem the agent's refusal
follows that filesystem while the alert still watches the root.

The floor reduces how often a write meets a full disk; it is not what keeps a full disk from leaving
half-written files. Each writing path is all-or-nothing on its own: an upload writes a temporary
name and renames it; a backup writes `<id>.partial`, renames it only when complete, and removes the
`.partial`, the scratch and (when the sidecar cannot be written in full) the just-published
artifact on any failure; a restore keeps the previous home until its swap.

## The stored history

`monitoring.Samples` keeps seven days of readings.

- `DiskUsedBytes` keeps the meaning it had when every existing row was written:
  `total - available`, which is space no account may write. The previous release reads it, so it
  cannot change meaning.
- `DiskInUseBytes` (nullable, added by migration `SplitDiskReserve`) holds used, `df`'s `Used`.
  Rows written before the update hold `null`, because nothing stored says how to split the old sum.

The disk chart plots `DiskInUseBytes`. A bucket whose rows are all from before the update is drawn
as a gap rather than as the old figure, which would draw a step the size of the reserve at the
upgrade. Those gaps leave the chart when the old rows age out.

## Contract revision

The change raises the agent contract to revision 6. The panel's window
(`AgentProtoVersionWindow`) is 6 to 6. A revision-5 agent sends no reserved or available figure,
and protobuf reads a missing field as zero, so a panel driving one would show a full disk. It also
sends a used figure with the reserve folded in. The panel therefore refuses an agent of either
other revision, and both must be updated together.

The free-space floor raises it to revision 7 (issue #148), and the window is now 7 to 7: a
revision-6 agent never refuses at a floor and sends no `free_space_floor_bytes`, which protobuf reads
as zero, so the floor alert could never fire against it.
