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
  /** Mean disk space in use across the bucket, in bytes. */
  diskUsedBytes: number
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
   * camelCase like every other enum member on this API. Not localized by the panel — the module
   * ships no display text for it — so it is rendered verbatim, the same honest fallback the
   * sidebar uses for a module it has no label for.
   */
  service: string
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
   * Reads whether each service the agent watches is up, down, or not known.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per watched service; a service with no row is one the host does not observe.
   */
  listServices: (signal?: AbortSignal) => Promise<ServiceStatus[]>

  /**
   * Reads what every hosting account occupies on disk beside what its plan allows.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per account the panel holds.
   */
  listAccountDiskUsage: (signal?: AbortSignal) => Promise<AccountDiskUsage[]>
}
