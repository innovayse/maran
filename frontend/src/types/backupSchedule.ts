/**
 * How often a schedule takes a backup, mirroring the backend's `BackupFrequency`.
 *
 * The values are lowercase because the panel registers
 * `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` for every enum it sends and binds, so
 * `BackupFrequency.Daily` is `"daily"` on the wire. Two values and no third: the backend refused a
 * cron expression deliberately, because "daily at 03:00" is the whole of what the product promises
 * and a cron field is a parsing surface at an administrator-only boundary.
 */
export type BackupFrequency = 'daily' | 'weekly'

/**
 * The weekday a weekly schedule fires on, mirroring the backend's `DayOfWeek?` and likewise a
 * camelCase name on the wire.
 *
 * It is `null` for a daily schedule and required for a weekly one — the server refuses the two
 * wrong combinations with `BackupScheduleDayRequired` and `BackupScheduleDayNotAllowed`, and it is
 * the authority on both.
 */
export type BackupScheduleDay =
  | 'sunday'
  | 'monday'
  | 'tuesday'
  | 'wednesday'
  | 'thursday'
  | 'friday'
  | 'saturday'

/**
 * Outward view of one backup schedule, mirroring the backend's `BackupScheduleDto` field for field.
 *
 * A schedule is either the host-wide policy (`accountId` is `null`) or one account's override
 * (`accountId` names it). Those are two different things and not "with an account" and "without
 * one": the host policy is what every account without an override is backed up by.
 */
export interface BackupSchedule {
  /** The schedule's identity. Never shown — the schedule is addressed by its scope, not by this. */
  id: string
  /** The account this backs up, or `null` for the host-wide policy every other account follows. */
  accountId: string | null
  /**
   * The destination it writes to, or `null` for this server's default one.
   *
   * Carried because the backend carries it, and rendered nowhere: the only destination that exists
   * is the default local one, and `BackupDestinationsController` refuses every attempt to record
   * another. A field with one possible value is not information.
   */
  destinationId: string | null
  /** How often a backup is taken. */
  frequency: BackupFrequency
  /** The hour of the day, in UTC, at which it is taken. */
  hourUtc: number
  /** The weekday a weekly schedule fires on; `null` for a daily one. */
  dayOfWeekUtc: BackupScheduleDay | null
  /** How many successful backups are kept before the oldest are pruned. */
  retainCount: number
  /** Whether the schedule actually runs. A saved-but-off schedule keeps its cadence readable. */
  enabled: boolean
  /**
   * When a run was last started, as an ISO-8601 string, or `null` if one never has been.
   *
   * The most useful field on the screen: "enabled, daily at 03:00" says what was configured and
   * nothing at all about whether it works.
   */
  lastRunAt: string | null
}

/**
 * Request body for `PUT /api/v1/backup-schedules`, binding the backend's
 * `SaveBackupScheduleCommand`.
 *
 * Every member the command declares is here except the two it establishes itself: `ipAddress` and
 * `userAgent` are `[property: JsonIgnore]` plus `[BindNever]`, stamped by the controller from the
 * connection, and sending either would be sending a value the server discards — the audit record of
 * a request must not be written by the request.
 *
 * There is no `id`: a schedule has one identity per scope, so `accountId` addresses it, and a
 * `PUT` that stated an id could name one scope in the body and edit another.
 */
export interface SaveBackupScheduleRequest {
  /** The account to back up, or `null` to save the host-wide policy. */
  accountId: string | null
  /**
   * The destination to write to, or `null` for this server's default one.
   *
   * Always `null` from this panel. The command declares it and gives it no default, so it must be
   * sent; the screen has nothing to put in it, because the only destination that exists is the
   * default one and the endpoint that would record another refuses every request today.
   */
  destinationId: string | null
  /** How often the backup is taken. */
  frequency: BackupFrequency
  /** The hour of the day, in UTC. The server bounds it to 0..23. */
  hourUtc: number
  /** The weekday for a weekly schedule; must be `null` for a daily one. */
  dayOfWeekUtc: BackupScheduleDay | null
  /** How many successful backups to keep. The server bounds it to 1..365. */
  retainCount: number
  /** Whether the schedule runs at all. Switching it off is what a delete would have been. */
  enabled: boolean
}

/**
 * Typed access to the backup-schedules endpoints.
 *
 * Two calls, matching the two routes `BackupSchedulesController` publishes, and no third. There is
 * no delete — a schedule is switched off by saving it with `enabled: false` — and no "run now": a
 * backup on demand is `POST /api/v1/backups`, and a second way to ask would be a second place the
 * backup's kind is decided.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface BackupSchedulesApi {
  /**
   * Reads one schedule: the host-wide policy, or one account's override.
   *
   * A server that has never configured the scope answers **404**, which is the ordinary first
   * state of this endpoint and not a fault. The caller is expected to render that as "nothing is
   * scheduled" rather than as an error.
   * @param accountId The account whose override to read, or `null` for the host-wide policy.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The schedule as the panel holds it.
   */
  get: (accountId: string | null, signal?: AbortSignal) => Promise<BackupSchedule>

  /**
   * Creates or replaces one schedule and answers with what was stored.
   * @param request The schedule to store.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored schedule, including the `lastRunAt` the panel already held.
   */
  save: (request: SaveBackupScheduleRequest, signal?: AbortSignal) => Promise<BackupSchedule>
}
