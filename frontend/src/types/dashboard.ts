import type { AuditEvent } from './audit'
import type { LiveDashboardHandlers } from './liveDashboard'
import type { ClockSync, HostMetrics, ServiceStatus } from './monitoring'

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
 * How one alert channel (mail or webhook) fared the last time the panel tried it, mirroring the
 * backend's `AlertChannelStatusDto`.
 */
export interface AlertChannelStatus {
  /** `ok`, `failing` (the last delivery failed) or `unknown` (nothing has been attempted on the channel yet). */
  state: 'ok' | 'failing' | 'unknown'
  /** When the failing delivery was attempted; `null` unless the channel is failing. */
  lastFailureAt: string | null
  /** The panel's reason, already localized; `null` unless the channel is failing. */
  failureMessage: string | null
}

/** Both alert channels, mirroring the backend's `AlertDeliveryStatusDto` (issue #142). */
export interface AlertDeliveryStatus {
  /** The alert mail. */
  mail: AlertChannelStatus
  /** The alert webhook. */
  webhook: AlertChannelStatus
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
  /** Sites running on a PHP version past its end of life, which no longer receives security fixes (issue #157). */
  sitesOnEndOfLifePhp: DashboardSection<number>
  /**
   * Whether alert mail and the alert webhook reached their destination the last time the panel tried
   * (issue #142). Not a count: each failing channel is a row of its own, and a state that could not be
   * read is unchecked, never "delivering".
   */
  alertDelivery: DashboardSection<AlertDeliveryStatus>
  /**
   * Whether the host's clock is synchronised (issue #150). Not a count: it needs attention when it
   * is anything but synchronised, and is unchecked — never synchronised — when it could not be read.
   */
  clock: DashboardSection<ClockSync>
}

/**
 * What an administrator's landing screen shows, mirroring the backend's `AdministratorDashboardDto`:
 * every server-wide section, each with its own read state.
 *
 * Whether a section could be READ is its {@link DashboardSection} state, never an empty list or a
 * null: an empty list is a real answer (no services watched, nothing journalled yet) and a different
 * one from a module that did not answer.
 */
export interface AdministratorDashboard {
  /** Discriminant: this answer was filled for an administrator. */
  audience: 'administrator'
  /** The host's live resource reading. */
  resources: DashboardSection<HostMetrics>
  /** The services the agent watches. */
  services: DashboardSection<ServiceStatus[]>
  /** What this server holds. */
  counts: DashboardCounts
  /** What needs looking at. */
  attention: DashboardAttention
  /** The newest audit entries. */
  recentAudit: DashboardSection<AuditEvent[]>
}

/**
 * What a customer's landing request is answered with, mirroring the backend's `CustomerDashboardDto`:
 * nothing of the server's state. The shape has no section at all, so there is none to mistake for a
 * read that failed.
 */
export interface CustomerDashboard {
  /** Discriminant: this answer was filled for a customer. */
  audience: 'customer'
}

/**
 * Everything the landing screen shows, mirroring the backend's `DashboardDto`: exactly one of two
 * shapes, told apart by `audience`. The decision is the server's; this SPA agreeing with it is advice
 * and not enforcement (rules/vue.md).
 */
export type Dashboard = AdministratorDashboard | CustomerDashboard

/** The dashboard endpoint this SPA calls. */
export interface DashboardApi {
  /**
   * Reads the landing screen's contents for the signed-in caller.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns What this caller is allowed to be shown.
   */
  get: (signal?: AbortSignal) => Promise<Dashboard>
  /**
   * Reads how many things on the server ask to be looked at: the dashboard's attention block added
   * up, without the rest of the dashboard. Administrators only; anyone else is refused.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The count, or that it could not be taken; never a zero standing in for a failed read.
   */
  attentionCount: (signal?: AbortSignal) => Promise<DashboardSection<number>>
  /**
   * Opens the live dashboard stream and delivers each frame to its handler until the connection
   * ends. Frames of a name this SPA does not know are dropped (the vocabulary is additive).
   * @param handlers Where each kind of frame goes.
   * @param since The instant of the newest reading already held (an ISO 8601 string), so the panel
   * back-fills only what is missing; `null` on the first connection of a visit.
   * @param signal Abort signal that closes the stream and releases its connection.
   * @returns Resolves when the stream ended or was aborted; rejects when it could not be opened.
   */
  openLive: (handlers: LiveDashboardHandlers, since: string | null, signal: AbortSignal) => Promise<void>
}
