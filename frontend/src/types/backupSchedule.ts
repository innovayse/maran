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
 * The cadence names as the wire carries them: the backend's `BackupFrequency` members. This is the
 * request contract only (`maran api` reads it against the command); the cadences a picker offers
 * and their names come from `GET /api/v1/backup-options`.
 */
export type BackupFrequencyWire = 'daily' | 'weekly'

/** Every {@link BackupScheduleDay}, in the order a week is read. */
export const BACKUP_SCHEDULE_DAYS: readonly BackupScheduleDay[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
]

/**
 * Which schedule the schedule screen is editing: the host-wide policy, or one account's override.
 *
 * A union rather than a nullable account id or an empty string, because "no account chosen" and
 * "the policy for every account" are different statements and the screen must never confuse them.
 * It maps to the wire's `accountId: null` for `hostWide` and to the account's identity otherwise.
 */
export type BackupScheduleScope =
  | {
      /** The policy every account without an override is backed up by. */
      kind: 'hostWide'
    }
  | {
      /** One account's override of the host-wide policy. */
      kind: 'account'
      /** The account the override belongs to. */
      accountId: string
    }

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
  /**
   * How often a backup is taken: the backend's `BackupFrequency` as its camelCase member name. The
   * cadences on offer are the panel's (`GET /api/v1/backup-options`), so the SPA holds no list of
   * them and the backend's validator decides what is accepted.
   */
  frequency: BackupFrequencyWire
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
  /**
   * When the schedule will next run, as an ISO-8601 string, or `null` while it is switched off.
   *
   * Computed by the server from the same rule its sweep decides by, so the screen never works it out
   * from the hour itself. It can lie in the past when the panel was down across the hour; that run
   * is taken at the next tick.
   */
  nextRunAt: string | null
}

/**
 * What `GET /api/v1/backup-schedules` answers for one scope, mirroring the backend's
 * `BackupScheduleScopeDto`.
 *
 * A scope nobody has configured is an ordinary `200` with `schedule: null` — the first state of every
 * fresh server, not a failure. The panel invents no default, so there is nothing else to show.
 */
export interface BackupScheduleScopeView {
  /**
   * The schedule recorded for the scope, or `null` when none has ever been saved for it. A recorded
   * schedule that is switched off is present, with `enabled: false`.
   */
  schedule: BackupSchedule | null
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
  /** How often the backup is taken: one of the cadence values the panel listed. */
  frequency: BackupFrequencyWire
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
   * Reads what is scheduled for one scope: the host-wide policy, or one account's override.
   *
   * A scope that has never been configured answers `200` with `schedule: null` — the ordinary first
   * state of this endpoint, not a fault.
   * @param accountId The account whose override to read, or `null` for the host-wide policy.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The scope, holding its schedule or none.
   */
  get: (accountId: string | null, signal?: AbortSignal) => Promise<BackupScheduleScopeView>

  /**
   * Creates or replaces one schedule and answers with what was stored.
   * @param request The schedule to store.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored schedule, including the `lastRunAt` the panel already held.
   */
  save: (request: SaveBackupScheduleRequest, signal?: AbortSignal) => Promise<BackupSchedule>
}

/**
 * Request body for `PUT /api/v1/me/backup-schedule`, binding the backend's `SaveMyBackupScheduleCommand`.
 *
 * It is {@link SaveBackupScheduleRequest} without the two members a customer does not own: there is
 * no `accountId` (the schedule is the signed-in customer's own, established by the server from their
 * login, and a body that named one would be discarded) and no `destinationId` (where an archive is
 * written is the operator's decision). The server bounds the cadence to the customer's list and
 * `retainCount` to the customer's ceiling; both lists come from `GET /api/v1/backup-options`.
 */
export interface SaveMyBackupScheduleRequest {
  /** How often the backup is taken: one of the customer cadences the panel listed. */
  frequency: BackupFrequencyWire
  /** The hour of the day, in UTC. The server bounds it to 0..23. */
  hourUtc: number
  /** The weekday for a weekly schedule; must be `null` for a daily one. */
  dayOfWeekUtc: BackupScheduleDay | null
  /** How many successful backups to keep, up to the customer ceiling the panel listed. */
  retainCount: number
  /** Whether the schedule runs at all. */
  enabled: boolean
}

/**
 * Typed access to the signed-in customer's own backup schedule.
 *
 * Two calls, matching the two routes `MyBackupScheduleController` publishes, and neither names an
 * account: the schedule it serves is the caller's. Called from Pinia stores only — never from a
 * component (rules/vue.md).
 */
export interface MyBackupScheduleApi {
  /**
   * Reads the caller's own schedule. An account that has never saved one answers `200` with
   * `schedule: null` — the ordinary first state, not a fault.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The scope, holding the schedule or none.
   */
  get: (signal?: AbortSignal) => Promise<BackupScheduleScopeView>

  /**
   * Creates or replaces the caller's own schedule and answers with what was stored.
   * @param request The schedule to store.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored schedule.
   */
  save: (request: SaveMyBackupScheduleRequest, signal?: AbortSignal) => Promise<BackupSchedule>
}
