/**
 * One entry of a choice list the backup screens select from, mirroring the backend's
 * `BackupChoiceDto`.
 *
 * The label arrives already localized for the request's language, so the SPA renders it as it is
 * and holds no vocabulary of its own for these sets (rules/vue.md "Data comes from the backend").
 */
export interface BackupChoice {
  /**
   * The machine-stable value the screen sends back or filters by: a status, a reason, a kind of
   * storage, a cadence, a weekday name, an hour or a number of copies. The endpoints validate it
   * again, so a list is advice and never the boundary.
   */
  value: string
  /** The choice as the operator reads it, in the interface language. */
  label: string
}

/**
 * Every closed set the backup screens select from, mirroring the backend's `BackupOptionsDto` from
 * `GET /api/v1/backup-options`.
 */
export interface BackupOptions {
  /** The states a backup can be in, for the history filter. */
  statuses: BackupChoice[]
  /** The reasons a backup is taken, for the history filter. */
  kinds: BackupChoice[]
  /** The kinds of storage a destination can name, for the destinations filter. */
  destinationKinds: BackupChoice[]
  /** The cadences a schedule can have. */
  frequencies: BackupChoice[]
  /** The weekdays a weekly schedule can fire on, Monday first. */
  weekdays: BackupChoice[]
  /** The hours of the day (UTC) a schedule can name, `0` to `23`. */
  hours: BackupChoice[]
  /** How many copies a schedule can keep, from the minimum to the ceiling the server enforces. */
  retainCounts: BackupChoice[]
  /** The cadences a customer may choose for their own schedule: a subset of {@link frequencies}. */
  customerFrequencies: BackupChoice[]
  /** How many copies a customer may keep on their own schedule: a subset of {@link retainCounts}. */
  customerRetainCounts: BackupChoice[]
}

/**
 * Typed access to the backup options endpoint. Called from Pinia stores only, never from a
 * component (rules/vue.md).
 */
export interface BackupOptionsApi {
  /**
   * Reads every closed set the backup screens select from, named for the request's language.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The lists.
   */
  get: (signal?: AbortSignal) => Promise<BackupOptions>
}
