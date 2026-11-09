import type { ListRequest, Paged } from './list'

/** Which part of the panel raised a notification, mirroring the backend's `NotificationSource` in camelCase. */
export type NotificationSource = 'task' | 'alert' | 'backup'

/** How much a notification matters, mirroring the backend's `NotificationSeverity` in camelCase. */
export type NotificationSeverity = 'info' | 'warning' | 'critical'

/**
 * One entry of the administrators' feed, mirroring the backend's `NotificationDto`. The sentence
 * arrives already worded in the reader's language; the SPA renders it as plain text.
 */
export interface PanelNotification {
  /** The entry, the only identifier a dismissal names. */
  id: string
  /** Which part of the panel raised it. */
  source: NotificationSource
  /** How much it matters. */
  severity: NotificationSeverity
  /** The headline sentence. */
  title: string
  /** A second line (a task's failure reason, an alert's figure), or `null`. */
  detail: string | null
  /** A route name from the closed set the panel offers, or `null`; the SPA maps it to a page. */
  deepLink: string | null
  /** When the thing happened, an ISO 8601 instant. */
  occurredAt: string
}

/** How many entries the caller has not dismissed, mirroring the backend's `NotificationCountDto`. */
export interface NotificationCount {
  /** The number, never above the panel's cap. */
  count: number
  /** True when there are more than `count`; the badge then reads "99+". */
  capped: boolean
}

/**
 * Typed access to the notification feed endpoints.
 *
 * Called from Pinia stores only, never from a component (rules/vue.md).
 */
export interface NotificationsApi {
  /**
   * Reads one page of the entries the caller has not dismissed, newest first.
   * @param request The page and page size; the feed takes no search, sort or filter from the bell.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<PanelNotification>>

  /**
   * Reads the badge figure alone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The count and whether it was capped.
   */
  count: (signal?: AbortSignal) => Promise<NotificationCount>

  /**
   * Dismisses one entry for the caller. Idempotent on the panel: an unknown or already dismissed
   * id is also a success.
   * @param id The entry.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves when the panel accepted it.
   */
  dismiss: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Dismisses every entry the caller has not dismissed.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves when the panel accepted it.
   */
  dismissAll: (signal?: AbortSignal) => Promise<void>
}
