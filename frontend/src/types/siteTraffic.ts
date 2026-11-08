/** Requests answered with one class of status code (e.g. all 2xx) on one day. */
export interface TrafficStatusClass {
  /** The class as an operator reads it (e.g. "Success (2xx)"), in the request's language. */
  label: string
  /** How many requests the site answered with a status of this class. */
  requests: number
}

/** One UTC day of a site's traffic. */
export interface TrafficDay {
  /** The day as `YYYY-MM-DD`, in UTC. */
  date: string
  /** How many requests the site answered that day. */
  requests: number
  /** The response body bytes sent that day. */
  bytes: number
  /** That day's requests split by status class. */
  statusClasses: TrafficStatusClass[]
}

/** How many requests the site answered with one exact status code over the window. */
export interface TrafficStatusCode {
  /** The HTTP status code, e.g. 404. */
  code: number
  /** How many requests got this status. */
  requests: number
}

/** How many requests asked for one path over the window. */
export interface TrafficPath {
  /** The requested path with its query string already removed. Customer-supplied text: render as plain text only. */
  path: string
  /** How many requests asked for this path. */
  requests: number
}

/** The sums over the whole window. */
export interface TrafficTotals {
  /** Requests answered across the window. */
  requests: number
  /** Response body bytes sent across the window. */
  bytes: number
}

/**
 * A site's traffic summary over a window, mirroring the backend's `SiteTrafficDto` field-for-field
 * (`GET /api/v1/sites/{id}/traffic?days=N`).
 */
export interface SiteTraffic {
  /** The window's length in days, one of the windows the panel offers. */
  days: number
  /** The first day of the window, as `YYYY-MM-DD`. */
  from: string
  /** The last day of the window, as `YYYY-MM-DD`. */
  to: string
  /** Whether any traffic has been recorded for the site yet; `false` is the ordinary first state. */
  hasData: boolean
  /** The sums over the window. */
  totals: TrafficTotals
  /** One entry per day with traffic, oldest first. */
  perDay: TrafficDay[]
  /** The exact status codes seen, most frequent first. */
  statusCodes: TrafficStatusCode[]
  /** The most requested paths, most frequent first. */
  topPaths: TrafficPath[]
  /** The day from which the figures may be incomplete (a gap in the log was seen), or `null` when they are complete. */
  incompleteSince: string | null
  /** When the summary was last brought up to date, as an ISO-8601 string, or `null` before the first pass. */
  updatedAt: string | null
}

/**
 * What the traffic tab last learned about the site's traffic: on its way, arrived or failed. A
 * union so a failed read cannot be mistaken for an empty summary.
 */
export type SiteTrafficLoad =
  | {
      /** The read has not been answered yet. */
      state: 'loading'
    }
  | {
      /** The panel answered. */
      state: 'loaded'
      /** The summary; `hasData` says whether it holds anything. */
      traffic: SiteTraffic
    }
  | {
      /** The panel refused or did not answer. */
      state: 'failed'
      /** The panel's already-localized reason, or `null` when it did not answer at all. */
      message: string | null
    }

/**
 * The log retention a customer saved in this session, kept with the site it belongs to. The site
 * detail held by the sites store is not re-read after a save, so this is what the control shows
 * until the detail is read again.
 */
export interface SavedLogRetention {
  /** The site whose retention was saved. */
  siteId: string
  /** The retention the panel answered with, in days. */
  days: number
}

/** How the last log retention change ended: not attempted yet, accepted by the panel, or failed. */
export type RetentionOutcome = 'none' | 'saved' | 'failed'
