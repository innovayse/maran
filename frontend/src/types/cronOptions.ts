/**
 * One entry of a choice list the scheduled-tasks screens select from, mirroring the backend's
 * `CronChoiceDto`.
 *
 * The label arrives already localized for the request's language, so the SPA renders it as it is
 * and holds no vocabulary of its own for these sets (rules/vue.md "Data comes from the backend").
 */
export interface CronChoice {
  /**
   * The machine-stable value the screen sends back: a frequency name, a cron weekday number
   * (Sunday is `0`), an hour, a minute, a day of the month or a run outcome. The endpoints validate
   * it again, so a list is advice and never the boundary.
   */
  value: string
  /** The choice as the operator reads it, in the interface language. */
  label: string
}

/**
 * Every closed set the scheduled-tasks screens select from, mirroring the backend's
 * `CronOptionsDto` from `GET /api/v1/cron-options`.
 */
export interface CronOptions {
  /** The shapes the schedule builder offers, in reading order. */
  frequencies: CronChoice[]
  /** The seven weekdays, Sunday first; the value is the number cron uses. */
  weekdays: CronChoice[]
  /** The hours of the day, `0` to `23`. */
  hours: CronChoice[]
  /** The minutes of the hour, `0` to `59`. */
  minutes: CronChoice[]
  /** The days of the month, `1` to `31`. */
  daysOfMonth: CronChoice[]
  /** The outcomes a run started by hand can end in, for the history filter. */
  runOutcomes: CronChoice[]
}

/**
 * Typed access to the cron options endpoint. Called from Pinia stores only, never from a component
 * (rules/vue.md).
 */
export interface CronOptionsApi {
  /**
   * Reads every closed set the scheduled-tasks screens select from, named for the request's language.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The lists.
   */
  get: (signal?: AbortSignal) => Promise<CronOptions>
}
