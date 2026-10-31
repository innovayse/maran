import type { AuditEvent } from './audit'
import type { ServiceStatus } from './monitoring'

/**
 * One section of the landing screen, mirroring the backend's `DashboardSection<T>`: either the data a
 * module answered with, or the fact that it could not be read.
 *
 * A union and not a value beside a flag: an unavailable section has no `data` at all, so TypeScript
 * refuses to read the data of a section that has none, and nothing stands in for "could not be read"
 * — not an empty list, not a zero, not a null (issue #248). Narrow on `state` before reading `data`.
 */
export type DashboardSection<T> =
  | {
      /** The module answered. */
      state: 'loaded'
      /** What it answered with. */
      data: T
    }
  | {
      /** The module refused or failed; there is nothing to show. */
      state: 'unavailable'
    }

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
  /** Hosting accounts on this server; a count that could not be taken is unavailable, never zero. */
  accounts: DashboardSection<number>
  /** Sites across every account. */
  sites: DashboardSection<number>
  /** Databases across every account. */
  databases: DashboardSection<number>
}

/**
 * What on this server is asking to be looked at, mirroring the backend's `DashboardAttentionDto`.
 *
 * Zero is the ordinary answer and renders as a zero: on this screen "nothing needs attention" is
 * the good news, and an empty state would read as "not loaded". Each count carries whether its
 * source could be read: an unavailable source is named on screen, and the all-clear is drawn only
 * when every source was read (issue #248).
 */
export interface DashboardAttention {
  /** Certificates at or inside the backend's expiry horizon, already-expired ones included. */
  certificatesExpiringSoon: DashboardSection<number>
  /** Backups whose last run failed. */
  failedBackups: DashboardSection<number>
  /** Addresses the firewall is currently refusing. */
  bannedAddresses: DashboardSection<number>
  /** Panel tasks that ended in failure. */
  failedTasks: DashboardSection<number>
}

/**
 * Everything the landing screen shows, mirroring the backend's `DashboardDto`.
 *
 * **A null section means "not for this caller"**, and nothing else. Both audiences receive this one
 * shape, so this SPA has a single contract: an administrator gets every section, everyone else gets
 * every section null. The decision is the server's, and `isAdministrator` says which answer arrived,
 * so the page can state that there is nothing to show rather than drawing a screen of missing panels.
 *
 * Whether a section could be READ is its {@link DashboardSection} state, never an empty list or a
 * null: an empty list is a real answer (no services watched, nothing journalled yet) and a different
 * one from a module that did not answer.
 */
export interface Dashboard {
  /** Whether the sections below were filled for this caller. */
  isAdministrator: boolean
  /** The host's live resource reading, or `null` when withheld. */
  resources: DashboardSection<DashboardResources> | null
  /** The services the agent watches, or `null` when withheld. */
  services: DashboardSection<ServiceStatus[]> | null
  /** What this server holds, or `null` when withheld. */
  counts: DashboardCounts | null
  /** What needs looking at, or `null` when withheld. */
  attention: DashboardAttention | null
  /** The newest audit entries, or `null` when withheld. */
  recentAudit: DashboardSection<AuditEvent[]> | null
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
