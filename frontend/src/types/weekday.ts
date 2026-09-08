/**
 * The days of the week, Sunday first.
 *
 * Sunday first because that is how cron numbers them (`0` is Sunday), so the position in this list
 * is the number the contract carries and no second table is needed to relate the two. The names are
 * keys, not text: what a day is CALLED in the interface language comes from `utils/weekdayName.ts`,
 * which asks the date library rather than this panel holding its own translations.
 */
export const WEEKDAY_KEYS = [
  'sunday',
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
] as const

/** One day of the week, named as the panel and the backend both name it. */
export type WeekdayKey = (typeof WEEKDAY_KEYS)[number]
