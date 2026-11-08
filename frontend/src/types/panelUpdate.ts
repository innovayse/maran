/**
 * The panel's own release state (issue #382), from the angles the "Panel updates"
 * screen needs it (rules/vue.md "Types" — one domain per file).
 */

/** A newer release the last manifest check found, as the signed manifest describes it. */
export interface PanelUpdateAvailable {
  /** The version the check found. */
  version: string
  /** When it was released, as an ISO 8601 date, or `null` when the manifest carries none. */
  releasedAt: string | null
  /** The signed release-notes address, or `null` when none is published. Rendered only as a plain link. */
  notesUrl: string | null
}

/**
 * What `GET /api/v1/panel-update` answers: the facts of the running release and of the last check.
 * Every sentence a person reads about the outcome of an update lives in the task list, produced by
 * the backend; this shape carries facts only.
 */
export interface PanelUpdateState {
  /** The version that is running. */
  currentVersion: string
  /** The release channel the panel follows. */
  channel: string
  /** The newer release found by the last check, or `null` when the panel is current or never checked. */
  available: PanelUpdateAvailable | null
  /** When the root update command last fetched the signed manifest, as an ISO 8601 timestamp, or `null` when none has. */
  lastCheckedAt: string | null
  /** The version a rollback would restore, or `null` when no rollback is possible. */
  previousVersion: string | null
  /** True while an update is queued or running, so the action is refused rather than offered. */
  updateInProgress: boolean
  /**
   * Whether this server can hand an update to the root update service (the installer's request
   * directory exists). It says nothing about the caller's role: the endpoint refuses everyone but an
   * administrator whatever this holds, and the button is only advice.
   */
  canStart: boolean
}

/** What `POST /api/v1/panel-update` answers (202): the task that carries the update's progress. */
export interface PanelUpdateRequested {
  /** The task whose progress and result appear in the task list. */
  taskId: string
}

/** Public surface of the panel-update API composable. */
export interface PanelUpdateApi {
  /** Reads the running version, the last check's result and whether a rollback is possible. */
  get: (signal?: AbortSignal) => Promise<PanelUpdateState>
  /** Requests an update. The request carries nothing: the version comes from the signed manifest, never the browser. */
  start: (signal?: AbortSignal) => Promise<PanelUpdateRequested>
}
