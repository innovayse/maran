import type { CronEnvironmentVariable, SetCronEnvironmentRequest } from './cronEnvironmentVariable'

/**
 * The five time fields of one crontab line, mirroring the backend's `CronScheduleDto`.
 *
 * Five separate fields rather than one line, because that is the contract: the module validates
 * each field on its own so a refusal can name the one that was wrong, and a space cannot smuggle a
 * sixth field past a check meant for five. The SPA's raw mode accepts a whole line from a human and
 * splits it into these five before anything is sent.
 */
export interface CronSchedule {
  /** Minute field: `0-59`, `*`, a step, a range or a list. */
  minute: string
  /** Hour field: `0-23`, same syntax. */
  hour: string
  /** Day-of-month field: `1-31`, same syntax. */
  dayOfMonth: string
  /** Month field: `1-12`, same syntax. */
  month: string
  /** Day-of-week field: `0-6` (0 = Sunday), same syntax. */
  dayOfWeek: string
}

/**
 * Outward view of one scheduled task, mirroring the backend's `CronEntryDto`.
 *
 * Nothing here is a panel record: every field is what the agent reported the crontab currently
 * holds, so a customer who edited their crontab by hand is shown what they actually have.
 */
export interface CronEntry {
  /** The agent's identifier, and the only thing a later request may name this entry by. */
  entryId: string
  /** The account whose crontab holds it. */
  accountId: string
  /** When the entry runs. */
  schedule: CronSchedule
  /**
   * The command line exactly as the customer wrote it.
   *
   * Shown verbatim on purpose. A cron command can carry a credential, which is why the panel keeps
   * it out of its logs and its audit journal — but it is the customer's own text and this screen is
   * theirs, so masking it would leave them unable to read the job they wrote.
   */
  command: string
  /** True when the entry is a live crontab line; a disabled one stays commented out, never lost. */
  enabled: boolean
}

/**
 * What one entry's most recent run left behind, mirroring the backend's `CronEntryOutputDto`.
 *
 * Every field is nullable and each null means "the agent reported none" rather than a value —
 * an empty string is a run that printed nothing, `0` is a run that succeeded, and epoch is a real
 * instant. The whole reading can also be absent: the endpoint answers `200` with a `null` BODY for
 * an entry that has never run, which is why the store holds `CronEntryOutput | null`.
 */
export interface CronEntryOutput {
  /** The entry this reading belongs to, echoed so a response identifies itself. */
  entryId: string
  /** The tail of the run's output, bounded by the agent, or null when it reported none. */
  output: string | null
  /** The exit status of the most recent run, or null when none was reported. */
  lastExitCode: number | null
  /**
   * When the most recent run finished, in Unix SECONDS (UTC), or null when none was reported.
   *
   * Seconds, not milliseconds: the panel leaves the unit exactly as the module sends it, and the
   * screen multiplies by 1000 at the one place it renders a date.
   */
  lastRunAtUnix: number | null
}

/**
 * Request body for `POST /api/v1/cron-entries`, mirroring the backend's `CreateCronEntryRequest`.
 *
 * It carries no entry id, and none may be added: the agent mints the identifier when it installs
 * the entry, so an id a caller could choose is an id a caller could aim at one that already exists.
 */
export interface CreateCronEntryRequest {
  /** The account whose crontab gains the entry. */
  accountId: string
  /** When the entry is to run. */
  schedule: CronSchedule
  /** The command line to install, verbatim. */
  command: string
}

/**
 * Request body for `PUT /api/v1/cron-entries/{entryId}`, mirroring the backend's
 * `UpdateCronEntryRequest`.
 *
 * The entry id travels in the route rather than here, and there is deliberately no enablement flag:
 * rewriting what a job runs and switching it back on are separate decisions, and an edit that
 * quietly re-enabled a disabled entry would start a job nobody asked to start.
 */
export interface UpdateCronEntryRequest {
  /** The account whose crontab holds the entry. */
  accountId: string
  /** The new schedule. */
  schedule: CronSchedule
  /** The new command line, verbatim. */
  command: string
}

/**
 * Request body for `POST /api/v1/cron-entries/{entryId}/enabled`, mirroring the backend's
 * `SetCronEntryEnabledRequest`.
 *
 * The state is sent explicitly rather than the route offering a "toggle": a toggle applied to a
 * state the operator last saw some seconds ago switches whatever it finds, so two clicks that race
 * leave the entry in the state nobody chose.
 */
export interface SetCronEntryEnabledRequest {
  /** The account whose crontab holds the entry. */
  accountId: string
  /** True installs it as a live crontab line; false comments it out. */
  enabled: boolean
}

/**
 * Typed access to the cron endpoints — both controllers, because they are one module and one
 * screen. Called from Pinia stores only, never from a component (rules/vue.md).
 */
export interface CronApi {
  /**
   * Lists one account's cron entries. Another customer's account answers 404, never 403.
   * @param accountId The account whose crontab to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The entries, in the order the agent reported them.
   */
  list: (accountId: string, signal?: AbortSignal) => Promise<CronEntry[]>

  /**
   * Installs a new entry.
   * @param request The owning account, the schedule and the command.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The entry as installed, including the identifier the agent minted for it.
   */
  create: (request: CreateCronEntryRequest, signal?: AbortSignal) => Promise<CronEntry>

  /**
   * Replaces an entry's schedule and command, leaving its enablement exactly as it was.
   * @param entryId The entry to rewrite.
   * @param request The owning account, the new schedule and the new command.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel rewrote the entry.
   */
  update: (
    entryId: string,
    request: UpdateCronEntryRequest,
    signal?: AbortSignal,
  ) => Promise<boolean>

  /**
   * Switches an entry on or off without touching what it runs.
   * @param entryId The entry to switch.
   * @param request The owning account and the state to put the entry in.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel switched it.
   */
  setEnabled: (
    entryId: string,
    request: SetCronEntryEnabledRequest,
    signal?: AbortSignal,
  ) => Promise<boolean>

  /**
   * Reads what the entry's last run left behind.
   * @param entryId The entry to read.
   * @param accountId The account whose crontab holds it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reading, or `null` when the entry has never run — the module answers 200 with a
   * null body for that, and the absence is the answer rather than an empty reading.
   */
  getOutput: (
    entryId: string,
    accountId: string,
    signal?: AbortSignal,
  ) => Promise<CronEntryOutput | null>

  /**
   * Removes the entry, together with the files that held its command and its last run.
   * @param entryId The entry to remove.
   * @param accountId The account whose crontab holds it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed it.
   */
  remove: (entryId: string, accountId: string, signal?: AbortSignal) => Promise<boolean>

  /**
   * Reads one account's managed environment assignments.
   * @param accountId The account whose crontab to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The assignments the agent manages; assignments written outside its region are neither
   * reported nor touched.
   */
  listEnvironment: (accountId: string, signal?: AbortSignal) => Promise<CronEnvironmentVariable[]>

  /**
   * Replaces the managed assignments with exactly the set sent.
   * @param request The owning account and the complete new set.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel rewrote them.
   */
  setEnvironment: (request: SetCronEnvironmentRequest, signal?: AbortSignal) => Promise<boolean>
}

/**
 * How often the schedule builder's pattern repeats.
 *
 * A closed set of the five shapes a builder can express without becoming a worse version of the
 * five fields themselves. Anything outside it is what raw mode is for — the builder deliberately
 * does not grow lists, ranges and steps, because at that point the operator is writing cron and a
 * form is in their way.
 */
export type CronScheduleFrequency = 'everyMinute' | 'hourly' | 'daily' | 'weekly' | 'monthly'

/**
 * The schedule builder's own model: a frequency plus the parts that frequency actually uses.
 *
 * Every part is held whatever the frequency is, so switching from "daily" to "weekly" and back does
 * not silently lose the hour the operator already chose. `buildCronSchedule` is what decides which
 * of them reach the five fields.
 */
export interface CronScheduleBuilderValues {
  /** How often the pattern repeats. */
  frequency: CronScheduleFrequency
  /** Minute of the hour, `0`-`59`, used by every frequency except `everyMinute`. */
  minute: string
  /** Hour of the day, `0`-`23`, used by `daily`, `weekly` and `monthly`. */
  hour: string
  /** Day of the week, `0`-`6` with `0` for Sunday, used by `weekly`. */
  dayOfWeek: string
  /** Day of the month, `1`-`31`, used by `monthly`. */
  dayOfMonth: string
}
