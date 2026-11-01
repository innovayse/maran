import type { ListRequest, Paged } from './list'

/**
 * How far back a chart reaches, mirroring the backend's `ChartRange` enum.
 *
 * A union of the two camelCase member names the panel serializes, not a numeric enum: the
 * panel-wide `JsonStringEnumConverter` writes an enum-typed property as its camelCase member name,
 * and `MetricsChartDto.Range` is enum-typed, so `lastDay` is literally what arrives. The same
 * spelling is what `GET /api/v1/monitoring/chart?range=…` binds from — ASP.NET Core's query-string
 * model binder parses an enum member name case-insensitively.
 *
 * There are exactly two ranges because that is what the seven-day retention window can answer; a
 * third value here would be a request the panel's validator refuses (`IsInEnum`).
 */
export type ChartRange = 'lastDay' | 'lastWeek'

/**
 * The host's resource use right now, mirroring the backend's `HostMetricsDto` field-for-field. The
 * monitoring screen reads it from `GET /api/v1/monitoring/metrics`, and the dashboard receives the
 * same shape as its `resources` section — one definition, because it is one DTO.
 *
 * A LIVE reading taken by asking the agent, not the newest stored sample: the two answer different
 * questions, and a screen fed from the samples table would lag by a sampling interval and would
 * show nothing at all on a server whose sampler has not yet run.
 *
 * **The root filesystem is reported as three figures that sum to its size** (issue #77): what is
 * used, what is reserved for root, and what everyone else can still write. ext4 reserves about 5 %
 * of its blocks for root, so "size minus available" — what the panel once called "in use" — counted
 * that reserve as occupied and disagreed with `df` by several gibibytes. Each figure now means one
 * thing, and `diskUsedBytes + diskReservedBytes + diskAvailableBytes === diskTotalBytes`.
 *
 * The two network figures are COUNTERS since boot, passed through unaltered, not rates. A rate
 * needs two readings, so the backend deliberately does not offer one here rather than offering a
 * fabricated zero — and no screen displays them for that reason.
 */
export interface HostMetrics {
  /** Processor utilisation across all cores, 0.0-100.0. */
  cpuPercent: number
  /** Memory in use, in bytes. */
  memoryUsedBytes: number
  /** Total installed memory, in bytes. */
  memoryTotalBytes: number
  /** Blocks the root filesystem accounts as occupied, in bytes — exactly `df`'s Used column. */
  diskUsedBytes: number
  /**
   * Blocks only root may still write, in bytes: size minus used minus available. The filesystem's
   * reserve (about 5 % on ext4); 0 on a filesystem that keeps none, such as xfs.
   */
  diskReservedBytes: number
  /** What accounts other than root can still write, in bytes — exactly `df`'s Avail column. */
  diskAvailableBytes: number
  /** Size of the root filesystem, in bytes: used plus reserved plus available. */
  diskTotalBytes: number
  /** Bytes received since the host booted — a counter, not traffic. */
  networkRxBytes: number
  /** Bytes sent since the host booted — a counter, not traffic. */
  networkTxBytes: number
  /** System load average over the last minute. */
  loadAverage1m: number
  /** System load average over the last five minutes. */
  loadAverage5m: number
  /** System load average over the last fifteen minutes. */
  loadAverage15m: number
}

/**
 * One point on the monitoring charts, mirroring the backend's `MetricBucketDto` field-for-field.
 *
 * The two network figures are RATES the panel derived from the host's counters, divided by the
 * seconds actually elapsed between the two samples — so the series is not on a fixed step and a gap
 * in sampling is an ordinary thing to receive. They are nullable because the first bucket of any
 * chart has no earlier reading to measure against; a null is a gap, never a second of no traffic.
 */
export interface MetricBucket {
  /** The instant the bucket begins, as the panel's ISO-8601 offset string. */
  at: string
  /** Mean processor utilisation across the bucket, 0.0-100.0. */
  cpuPercent: number
  /** Mean memory in use across the bucket, in bytes. */
  memoryUsedBytes: number
  /** Mean installed memory across the bucket, in bytes. */
  memoryTotalBytes: number
  /**
   * Mean disk space used across the bucket, in bytes, as `df` counts Used — or `null` for a bucket
   * sampled before issue #77. Those older samples stored size minus available, which counted the
   * root reserve as used, so they measure a different thing and are not plotted: a null is a gap,
   * like a network rate with no earlier reading, never a zero.
   */
  diskUsedBytes: number | null
  /** Mean root filesystem capacity across the bucket, in bytes. */
  diskTotalBytes: number
  /** Mean one-minute load average across the bucket. */
  loadAverage1m: number
  /** Mean bytes received per second, or `null` for a bucket with no earlier reading. */
  networkReceivedBytesPerSecond: number | null
  /** Mean bytes sent per second, or `null` for a bucket with no earlier reading. */
  networkSentBytesPerSecond: number | null
}

/**
 * Everything one chart screen needs, mirroring the backend's `MetricsChartDto`.
 *
 * The range is echoed back by the panel rather than left implicit, and this SPA uses the echo for
 * exactly what it is for: a slow seven-day request can land after the operator has switched back to
 * twenty-four hours, and without comparing the echo the screen would draw points answering a
 * question it is no longer asking.
 */
export interface MetricsChart {
  /** The range these buckets cover, echoed back by the panel. */
  range: ChartRange
  /** How wide each bucket is, in seconds — the panel's own bucketing decision, not this SPA's. */
  bucketSeconds: number
  /** The points, oldest first. Empty is an ordinary answer on a freshly installed panel. */
  buckets: MetricBucket[]
}

/**
 * Whether a watched service is up, down, or neither, mirroring the backend's
 * `ServiceStatusDto.State`.
 *
 * camelCase, like {@link ChartRange}: `ServiceStatusDto.State` is `AgentServiceState`, a real enum,
 * so the panel-wide `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` decides the spelling —
 * `running`, `stopped`, `unknown` — the same way it decides every other enum on this API.
 *
 * The third value is not decoration. A socket-activated SSH unit on the Debian family is inactive
 * from boot until the first connection, so rendering "not known" as an outage would report one on
 * every such host at every reboot.
 */
export type ServiceState = 'running' | 'stopped' | 'unknown'

/**
 * What the agent found out about one of the services it watches, mirroring `ServiceStatusDto`.
 *
 * A service the agent does not watch has no row at all — absence is the answer, and this SPA never
 * fabricates a row to fill the gap.
 */
export interface ServiceStatus {
  /**
   * Which service the row describes, by the agent's own machine name (`webServer`, `phpFpm`, …),
   * camelCase like every other enum member on this API. Machine-stable — the row's key, never its
   * label: the panel localizes the label separately, in {@link ServiceStatus.name}.
   */
  service: string
  /**
   * The same service as a person reads it, produced and localized by the panel in the request's
   * language (rules/vue.md: data comes from the backend; the SPA only displays it). Rendered
   * verbatim — this SPA holds no display text for a server-side thing.
   */
  name: string
  /** Up, down, or not known. */
  state: ServiceState
  /** Why, in the service manager's own words. Administrators only, which this whole screen is. */
  detail: string
}

/**
 * What one hosting account occupies on disk beside what its plan allows, mirroring
 * `AccountDiskUsageDto`.
 *
 * Both figures are in BYTES: the panel converts the plan's stored megabytes on the way out, so the
 * two can be divided against each other without a unit conversion happening in the interface.
 */
export interface AccountDiskUsage {
  /** The account's identity. */
  accountId: string
  /** The account's Linux system user name — what the agent measured under. */
  username: string
  /**
   * Bytes occupied under the account's home directory, or `null` when the agent did not report
   * this account. Null and zero are different answers — "nobody has measured this" against "this
   * account holds nothing" — and the table draws them differently.
   */
  usedBytes: number | null
  /**
   * The plan's allowance in bytes, including a possible zero. What a zero-quota plan means is the
   * Accounts module's question, so the interface reports "no allowance recorded" rather than
   * dividing by it.
   */
  quotaBytes: number
  /**
   * Whether the filesystem holding this account's home can currently back {@link quotaBytes} with
   * a real kernel-enforced limit. `false` means the figure is real — it is still the plan the
   * customer paid for — but nothing on the server refuses a write past it right now. The table
   * must never draw {@link quotaBytes} as if it were in force when this is `false`: that is
   * issue #29's own defect, restated for the SPA rather than the backend.
   */
  enforceable: boolean
  /**
   * The backend's own explanation of why {@link enforceable} is `false`, already localized — or
   * `null` when {@link enforceable} is `true` and there is nothing to explain. The SPA renders this
   * text verbatim; it does not invent its own wording for what "not enforceable" means
   * (rules/vue.md, rules/architecture.md — the backend owns user-facing text).
   */
  note: string | null
}

/**
 * Whether the host's clock is synchronised with a time source (issue #150): the kernel's own
 * verdict, so a clock left drifting shows up as `notSynchronized` once the kernel stops vouching
 * for it.
 */
export type ClockSyncState = 'synchronized' | 'notSynchronized' | 'syncServiceDisabled'

/** The host clock's synchronisation, as the panel reads it live from the agent. */
export interface ClockSync {
  /** The machine state, for branching; never shown as text. */
  state: ClockSyncState
  /** What the state means for this server, already localized by the backend and shown verbatim. */
  description: string
}

/** The monitoring endpoints this SPA calls, as `useMonitoringApi` implements them. */
export interface MonitoringApi {
  /**
   * Reads the stored samples for a range, already bucketed by the panel.
   * @param range How far back the chart reaches.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The chart, with the range echoed back so a late answer can be recognised.
   */
  getChart: (range: ChartRange, signal?: AbortSignal) => Promise<MetricsChart>

  /**
   * Reads the host's resource use right now, asked of the agent rather than read from the samples.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The live reading.
   */
  getHostMetrics: (signal?: AbortSignal) => Promise<HostMetrics>

  /**
   * Reads whether each service the agent watches is up, down, or not known.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per watched service; a service with no row is one the host does not observe.
   */
  listServices: (signal?: AbortSignal) => Promise<ServiceStatus[]>

  /**
   * Reads one page of what the hosting accounts occupy on disk beside what each plan allows. The
   * panel measures only the page it answers, which is why used bytes is not a sort it offers.
   * @param request The page, size, search on the user name and sort (`username`, `quotaBytes`).
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total that matched.
   */
  listAccountDiskUsage: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<AccountDiskUsage>>

  /**
   * Reads whether the host's clock is synchronised with a time source, live from the agent.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The state with the backend's explanation of it.
   */
  getClockSync: (signal?: AbortSignal) => Promise<ClockSync>
}
