import { hasWhitespaceOrControl } from './cronText'
import type { CronSchedule, CronScheduleBuilderValues } from '../types/cronEntry'

/**
 * The client-side mirror of the panel's `CronScheduleValidator`, plus the two translations the cron
 * screen needs between a schedule and the way a human writes one.
 *
 * **A mirror, and only a mirror.** The module re-validates every field and the agent re-validates
 * it again; what checking here buys is that an operator is told which field is wrong before a
 * request goes out, instead of after (rules/vue.md: "Client-side rules mirror the server's
 * validator (read it; do not guess). The server remains the authority").
 *
 * The grammar is the module's, field for field: an item is one of a wildcard, a wildcard with a
 * step, a number, a range, or a range with a step; numbers are decimal, unpadded and bounded per
 * field; a range may not run backwards; a step is at least 1, at most the DISTANCE between the
 * field's bounds, and may follow only a wildcard or a range. Month and weekday names, the
 * `@hourly` family and whitespace are refused here because they are refused there.
 */

/** The most bytes one field may be, matching the module's ceiling. */
const MAXIMUM_FIELD_LENGTH_IN_BYTES = 256

/** The most digits any single number in a field may have; the module caps it at three. */
const MAXIMUM_NUMBER_DIGITS = 3

/** The item that means "every value this field has". */
const WILDCARD = '*'

/** How many whitespace-separated fields a crontab schedule has, and must have. */
const FIELD_COUNT = 5

/**
 * The inclusive bounds of each field, in the order a crontab line writes them.
 *
 * Stated per field rather than shared, exactly as the module states them: a step is capped at the
 * SPAN between a field's bounds, so reading day-of-month's cap off a zero-based field would admit a
 * step of 31, which selects the first of the month and nothing else while looking like "every 31
 * days".
 */
const FIELD_BOUNDS: readonly { readonly minimum: number; readonly maximum: number }[] = [
  { minimum: 0, maximum: 59 },
  { minimum: 0, maximum: 23 },
  { minimum: 1, maximum: 31 },
  { minimum: 1, maximum: 12 },
  { minimum: 0, maximum: 6 },
]

/** The field names, in the same crontab order as {@link FIELD_BOUNDS}. */
const FIELD_NAMES: readonly (keyof CronSchedule)[] = [
  'minute',
  'hour',
  'dayOfMonth',
  'month',
  'dayOfWeek',
]


/** Measures a string the way the module does — in UTF-8 bytes, not in UTF-16 code units. */
const encoder = new TextEncoder()

/**
 * Reads a bare decimal number: no sign, no padding, at most three digits.
 *
 * Written by hand rather than handed to `Number()`, which accepts a sign, surrounding whitespace,
 * exponents and digits from other scripts — any of which would let a value through a field that
 * must hold ASCII digits and nothing else. The leading-zero refusal is what keeps one schedule to
 * one text, so a stored schedule and a later read compare equal.
 * @param text The digits as written.
 * @returns The value, or `null` when the text is not one to three unpadded ASCII digits.
 */
const readNumber = (text: string): number | null => {
  if (text.length === 0 || text.length > MAXIMUM_NUMBER_DIGITS) {
    return null
  }

  if (text.length > 1 && text.startsWith('0')) {
    return null
  }

  for (const character of text) {
    if (character < '0' || character > '9') {
      return null
    }
  }

  return Number(text)
}

/**
 * Reads a number and checks it against its field's inclusive bounds.
 * @param text The digits as written.
 * @param minimum The smallest number this field accepts.
 * @param maximum The largest number this field accepts.
 * @returns The value, or `null` when it is not a bare number inside the bounds.
 */
const readBounded = (text: string, minimum: number, maximum: number): number | null => {
  const value = readNumber(text)
  return value !== null && value >= minimum && value <= maximum ? value : null
}

/**
 * Reads the part of an item before any step, and says whether it spans values.
 * @param basePart The item with its step removed.
 * @param minimum The smallest number this field accepts.
 * @param maximum The largest number this field accepts.
 * @returns Whether the base is acceptable, and whether it is a wildcard or a range.
 */
const readBase = (
  basePart: string,
  minimum: number,
  maximum: number,
): { valid: boolean; carriesASpan: boolean } => {
  if (basePart === WILDCARD) {
    return { valid: true, carriesASpan: true }
  }

  const dash = basePart.indexOf('-')
  if (dash < 0) {
    return { valid: readBounded(basePart, minimum, maximum) !== null, carriesASpan: false }
  }

  const low = readBounded(basePart.slice(0, dash), minimum, maximum)
  const high = readBounded(basePart.slice(dash + 1), minimum, maximum)

  // A range that runs backwards is refused rather than reversed: the module refuses it too, and
  // reversing it here would send a schedule the operator did not write.
  return { valid: low !== null && high !== null && low <= high, carriesASpan: true }
}

/**
 * Whether one comma-separated item of a field is acceptable.
 * @param item The item, which may carry a range and a step.
 * @param minimum The smallest number this field accepts.
 * @param maximum The largest number this field accepts.
 * @returns True when the item is one of the five shapes the grammar allows.
 */
const isValidItem = (item: string, minimum: number, maximum: number): boolean => {
  if (item.length === 0) {
    return false
  }

  // The FIRST slash, so a doubled step reads as one base with a malformed step and is refused for
  // the step rather than being silently read as something shorter.
  const slash = item.indexOf('/')
  const basePart = slash < 0 ? item : item.slice(0, slash)
  const stepPart = slash < 0 ? null : item.slice(slash + 1)

  const base = readBase(basePart, minimum, maximum)
  if (!base.valid) {
    return false
  }

  if (stepPart === null) {
    return true
  }

  // A step needs a span to step across, so the grammar allows it after a wildcard and after a range
  // and nowhere else. A step on a bare number is refused rather than read as "every second value
  // from here onwards", which is what some crons make of it and others reject outright.
  if (!base.carriesASpan) {
    return false
  }

  const step = readNumber(stepPart)

  // The SPAN, not the maximum: a step starts at the low bound, so it names a second value only
  // while the low bound plus the step is still inside the field.
  return step !== null && step >= 1 && step <= maximum - minimum
}

/**
 * Whether one whole field of a schedule is acceptable.
 *
 * The character-class refusal comes first and by name, so that loosening the grammar below can
 * never quietly re-admit a newline.
 * @param candidate The field as the operator typed it.
 * @param minimum The smallest number this field accepts.
 * @param maximum The largest number this field accepts.
 * @returns True when every comma-separated item of the field is acceptable.
 */
const isValidField = (candidate: string, minimum: number, maximum: number): boolean => {
  if (candidate.length === 0) {
    return false
  }

  if (encoder.encode(candidate).length > MAXIMUM_FIELD_LENGTH_IN_BYTES) {
    return false
  }

  // Whitespace and control characters, refused by name before the grammar is consulted, so that
  // loosening the grammar below can never quietly re-admit a newline.
  if (hasWhitespaceOrControl(candidate)) {
    return false
  }

  return candidate.split(',').every((item) => {
    return isValidItem(item, minimum, maximum)
  })
}

/**
 * The schedule's five fields in crontab order, so the checks and the formatter agree on it.
 * @param schedule The schedule to lay out.
 * @returns Minute, hour, day-of-month, month, day-of-week.
 */
const orderedFields = (schedule: CronSchedule): string[] => {
  return [schedule.minute, schedule.hour, schedule.dayOfMonth, schedule.month, schedule.dayOfWeek]
}

/**
 * Which of a schedule's five fields this panel would refuse, by field name.
 *
 * Returned as names rather than as a bare boolean so a form can mark the field that is wrong
 * instead of shrugging at the whole schedule — which is the entire reason the module's contract
 * carries five fields rather than one line.
 * @param schedule The schedule to check.
 * @returns The names of the failing fields; empty when the schedule is acceptable.
 */
export const invalidCronScheduleFields = (schedule: CronSchedule): (keyof CronSchedule)[] => {
  const fields = orderedFields(schedule)

  return FIELD_NAMES.filter((_name, index) => {
    const bounds = FIELD_BOUNDS[index]
    const field = fields[index]
    return (
      bounds === undefined ||
      field === undefined ||
      !isValidField(field, bounds.minimum, bounds.maximum)
    )
  })
}

/**
 * Whether this panel would let the schedule through to the module.
 * @param schedule The schedule to check.
 * @returns True when every field is acceptable.
 */
export const isValidCronSchedule = (schedule: CronSchedule): boolean => {
  return invalidCronScheduleFields(schedule).length === 0
}

/**
 * Writes a schedule the way a crontab line does, for a preview and for raw mode's field.
 * @param schedule The schedule to write.
 * @returns The five fields, single-spaced.
 */
export const formatCronExpression = (schedule: CronSchedule): string => {
  return orderedFields(schedule).join(' ')
}

/**
 * Reads a whole crontab expression — the thing an operator has in their clipboard — into the five
 * fields the module's contract carries.
 *
 * Splitting on any run of whitespace and requiring EXACTLY five fields is what stops a six-field
 * line (a schedule with the command still attached, or one of the `@reboot` family) from being
 * silently read as five and something extra. A line that is not five fields is refused here, and
 * nothing is sent.
 * @param text The expression as the operator typed it.
 * @returns The schedule, or `null` when the text is not exactly five whitespace-separated fields.
 */
export const parseCronExpression = (text: string): CronSchedule | null => {
  const fields = text
    .trim()
    .split(/\s+/u)
    .filter((field) => {
      return field.length > 0
    })

  if (fields.length !== FIELD_COUNT) {
    return null
  }

  const [minute, hour, dayOfMonth, month, dayOfWeek] = fields
  if (
    minute === undefined ||
    hour === undefined ||
    dayOfMonth === undefined ||
    month === undefined ||
    dayOfWeek === undefined
  ) {
    return null
  }

  return { minute, hour, dayOfMonth, month, dayOfWeek }
}

/**
 * Maps the builder's frequency and its parts onto the five fields.
 *
 * The parts a frequency does not use become the wildcard rather than carrying whatever the operator
 * last typed: a "daily" pattern that quietly kept a weekday from an earlier "weekly" choice would
 * run once a week while its form said daily.
 * @param values The builder's own model.
 * @returns The schedule those values describe.
 */
export const buildCronSchedule = (values: CronScheduleBuilderValues): CronSchedule => {
  const everyField: CronSchedule = {
    minute: WILDCARD,
    hour: WILDCARD,
    dayOfMonth: WILDCARD,
    month: WILDCARD,
    dayOfWeek: WILDCARD,
  }

  switch (values.frequency) {
    case 'everyMinute':
      return everyField
    case 'hourly':
      return { ...everyField, minute: values.minute }
    case 'daily':
      return { ...everyField, minute: values.minute, hour: values.hour }
    case 'weekly':
      return {
        ...everyField,
        minute: values.minute,
        hour: values.hour,
        dayOfWeek: values.dayOfWeek,
      }
    default:
      return {
        ...everyField,
        minute: values.minute,
        hour: values.hour,
        dayOfMonth: values.dayOfMonth,
      }
  }
}
