/**
 * The settings that decide who is told about an alert and when, from the angles the alert settings
 * screen needs them (rules/vue.md "Types" — one domain per file).
 *
 * Two server resources share the screen: the recipients (the Notifications module) and the
 * thresholds (the Monitoring module). Both are administrator-only singletons with no tenant
 * dimension.
 */

/** The alert recipients as `GET /api/v1/notifications/alert-recipients` reports them. */
export interface AlertRecipients {
  /** The addresses alert mail is sent to; empty on a fresh installation. */
  addresses: string[]
  /** The most addresses the list may hold; the screen holds no cap of its own. */
  maximum: number
}

/** The body of `PUT /api/v1/notifications/alert-recipients`: the whole list, replacing the stored one. */
export interface SaveAlertRecipientsRequest {
  /** The complete list of addresses; an empty list stops alert mail. */
  addresses: string[]
}

/** The alert thresholds as `GET /api/v1/monitoring/alert-thresholds` reports them, with the bounds. */
export interface AlertThresholds {
  /** The root filesystem alarms when it is fuller than this; always set. */
  diskPercent: number
  /** The processor alarms when it is busier than this, or `null` when it is not watched. */
  cpuPercent: number | null
  /** The memory alarms when more than this is in use, or `null` when it is not watched. */
  memoryPercent: number | null
  /** The lowest value any threshold may take; the screen holds no constant of its own. */
  minimumPercent: number
  /** The highest value any threshold may take. */
  maximumPercent: number
}

/** The body of `PUT /api/v1/monitoring/alert-thresholds`: all three thresholds, replacing the stored ones. */
export interface SaveAlertThresholdsRequest {
  /** The root filesystem alarms when it is fuller than this. */
  diskPercent: number
  /** The processor threshold, or `null` to stop watching it. */
  cpuPercent: number | null
  /** The memory threshold, or `null` to stop watching it. */
  memoryPercent: number | null
}

/** Public surface of the alert-recipients API composable. */
export interface AlertRecipientsApi {
  /** Reads the recipient list. */
  get: (signal?: AbortSignal) => Promise<AlertRecipients>
  /** Replaces the recipient list wholesale. */
  save: (request: SaveAlertRecipientsRequest, signal?: AbortSignal) => Promise<void>
}

/** Public surface of the alert-thresholds API composable. */
export interface AlertThresholdsApi {
  /** Reads the thresholds and the bounds a threshold may take. */
  get: (signal?: AbortSignal) => Promise<AlertThresholds>
  /** Replaces all three thresholds. */
  save: (request: SaveAlertThresholdsRequest, signal?: AbortSignal) => Promise<void>
}
