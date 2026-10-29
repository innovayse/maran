import type { AuditEvent } from './audit'
import type { ServiceStatus } from './monitoring'

/**
 * The host's resource use right now, mirroring the backend's `HostMetricsDto` field-for-field.
 *
 * A LIVE reading taken by asking the agent, not the newest stored sample: the two answer different
 * questions, and a landing page fed from the samples table would lag by a sampling interval and
 * would show nothing at all on a server whose sampler has not yet run.
 *
 * The two network figures are COUNTERS since boot, passed through unaltered, not rates. A rate
 * needs two readings, so the backend deliberately does not offer one here rather than offering a
 * fabricated zero — and this page does not display them for that reason.
 */
export interface DashboardResources {
  /** Processor utilisation across all cores, 0.0-100.0. */
  cpuPercent: number
  /** Memory in use, in bytes. */
  memoryUsedBytes: number
  /** Total installed memory, in bytes. */
  memoryTotalBytes: number
  /** Disk space in use on the root filesystem, in bytes. */
  diskUsedBytes: number
  /** Total capacity of the root filesystem, in bytes. */
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
 * How much of each thing this server holds, mirroring the backend's `DashboardCountsDto`.
 *
 * Scheduled tasks are deliberately absent, and the backend's DTO carries the reason: the Cron
 * module keeps no table of its own, so a server-wide count would be one privileged agent call per
 * account.
 */
export interface DashboardCounts {
  /** Hosting accounts on this server. */
  accounts: number
  /** Sites across every account. */
  sites: number
  /** Databases across every account. */
  databases: number
}

/**
 * What on this server is asking to be looked at, mirroring the backend's `DashboardAttentionDto`.
 *
 * Zero is the ordinary answer and renders as a zero: on this screen "nothing needs attention" is
 * the good news, and an empty state would read as "not loaded".
 */
export interface DashboardAttention {
  /** Certificates at or inside the backend's expiry horizon, already-expired ones included. */
  certificatesExpiringSoon: number
  /** Backups whose last run failed. */
  failedBackups: number
  /** Addresses the firewall is currently refusing. */
  bannedAddresses: number
  /** Panel tasks that ended in failure. */
  failedTasks: number
}

/**
 * Everything the landing screen shows, mirroring the backend's `DashboardDto`.
 *
 * **A null section means "not for this caller"** — never "none", and never "not loaded yet". Both
 * audiences receive this one shape, so this SPA has a single contract: an administrator gets every
 * section filled, everyone else gets every administrator-only section null. The decision is the
 * server's, and `isAdministrator` says which answer arrived, so the page can state that there is
 * nothing to show rather than drawing a screen of missing panels.
 *
 * The two list sections are not nullable: an empty list and a withheld list look the same on screen,
 * so there is no third state worth carrying for them.
 */
export interface Dashboard {
  /** Whether the sections below were filled for this caller. */
  isAdministrator: boolean
  /** The host's live resource reading, or `null` when withheld or unreadable. */
  resources: DashboardResources | null
  /** The services the agent watches; empty when withheld or unreadable. */
  services: ServiceStatus[]
  /** What this server holds, or `null` when withheld or unreadable. */
  counts: DashboardCounts | null
  /** What needs looking at, or `null` when withheld or unreadable. */
  attention: DashboardAttention | null
  /** The newest audit entries; empty when withheld or unreadable. */
  recentAudit: AuditEvent[]
}

/** The dashboard endpoint this SPA calls. */
export interface DashboardApi {
  /**
   * Reads the landing screen's contents for the signed-in caller.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns What this caller is allowed to be shown.
   */
  get: (signal?: AbortSignal) => Promise<Dashboard>
}
