import type { CronSchedule } from '../types/cronEntry'
import { WEEKDAY_KEYS, type WeekdayKey } from '../types/weekday'

/**
 * Reading a cron schedule back as one of the shapes a person names in words: "every day at 03:05"
 * rather than `5 3 * * *`.
 *
 * Only the shapes the schedule builder itself produces are recognised, plus "every N minutes"
 * (`*\/N * * * *`), the one raw pattern operators type most. Anything else — a list, a range, a
 * month restriction — answers `null`, and the screen shows the expression alone: a description that
 * covered half of a schedule would be a wrong sentence, and a wrong sentence about when a job runs
 * is worse than none. The words themselves are the caller's (message bundles); this file only
 * decides which sentence applies and with which values.
 */

/** Which sentence describes the schedule, and the values it needs. */
export type CronScheduleDescription =
  | { kind: 'everyMinute' }
  | { kind: 'everyNMinutes'; minutes: number }
  | { kind: 'hourly'; minute: number }
  | { kind: 'daily'; time: string }
  | { kind: 'weekly'; time: string; weekday: WeekdayKey }
  | { kind: 'monthly'; time: string; day: number }

/** The field value meaning "every". */
const WILDCARD = '*'

/** A plain, unpadded decimal of at most two digits. */
const PLAIN_NUMBER = /^(0|[1-9][0-9]?)$/u

/** A step over the whole minute range, `*\/N`. */
const MINUTE_STEP = /^\*\/([1-9][0-9]?)$/u

/**
 * Reads a single plain number from a field.
 * @param field The field's text.
 * @returns The number, or `null` when the field is anything else.
 */
const plainNumber = (field: string): number | null => {
  return PLAIN_NUMBER.test(field) ? Number(field) : null
}

/**
 * Writes an hour and a minute as a 24-hour clock reading.
 * @param hour The hour, 0-23.
 * @param minute The minute, 0-59.
 * @returns The time as `HH:MM`.
 */
const clockTime = (hour: number, minute: number): string => {
  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
}

/**
 * Decides which plain-language sentence describes a schedule.
 * @param schedule The five fields as the panel reported them.
 * @returns The description, or `null` when the schedule is not one of the recognised shapes.
 */
export const describeCronSchedule = (schedule: CronSchedule): CronScheduleDescription | null => {
  const { minute, hour, dayOfMonth, month, dayOfWeek } = schedule
  if (month !== WILDCARD) {
    return null
  }

  if (hour === WILDCARD && dayOfMonth === WILDCARD && dayOfWeek === WILDCARD) {
    if (minute === WILDCARD) {
      return { kind: 'everyMinute' }
    }
    const step = MINUTE_STEP.exec(minute)
    if (step?.[1] !== undefined) {
      return { kind: 'everyNMinutes', minutes: Number(step[1]) }
    }
    const atMinute = plainNumber(minute)
    return atMinute === null || atMinute > 59 ? null : { kind: 'hourly', minute: atMinute }
  }

  const atMinute = plainNumber(minute)
  const atHour = plainNumber(hour)
  if (atMinute === null || atHour === null || atMinute > 59 || atHour > 23) {
    return null
  }
  const time = clockTime(atHour, atMinute)

  if (dayOfMonth === WILDCARD && dayOfWeek === WILDCARD) {
    return { kind: 'daily', time }
  }

  if (dayOfMonth === WILDCARD) {
    const weekday = plainNumber(dayOfWeek)
    const key = weekday === null ? undefined : WEEKDAY_KEYS[weekday]
    return key === undefined ? null : { kind: 'weekly', time, weekday: key }
  }

  if (dayOfWeek === WILDCARD) {
    const day = plainNumber(dayOfMonth)
    return day === null || day < 1 || day > 31 ? null : { kind: 'monthly', time, day }
  }

  return null
}
