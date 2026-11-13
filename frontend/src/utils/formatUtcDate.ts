import { parseISO } from 'date-fns'
import type { AppLocale } from '../types/app'
import { formatDate } from './formatDate'

/**
 * Formats the UTC calendar day of a backend instant for display in the panel's current language.
 *
 * `formatDate` prints the day in the browser's time zone. That is right for "when was this created" and
 * wrong for a date the backend also names in words: the licence's expiry. The backend states it as the UTC
 * calendar day (the warning sentence and the mail say `2026-12-26`), so a licence expiring at
 * `2026-12-26T22:00Z` must read as 26 December in an Armenian browser too, not as 27 December beside a
 * sentence that says 26. The day is taken from the instant in UTC and handed to `formatDate`, which keeps
 * the one pattern and the one locale table.
 * @param isoTimestamp An ISO-8601 instant as sent by the backend.
 * @param locale The panel's active locale.
 * @returns The UTC day, formatted, or the raw value when it cannot be parsed.
 */
export const formatUtcDate = (isoTimestamp: string, locale: AppLocale): string => {
  const parsed = parseISO(isoTimestamp)

  // An unparseable instant is shown as-is, as `formatDate` does, so an operator can report the raw value.
  if (Number.isNaN(parsed.getTime())) {
    return isoTimestamp
  }

  // `yyyy-MM-dd` of the UTC day; `formatDate` reads a date-only string as that day, whatever the zone.
  return formatDate(parsed.toISOString().slice(0, 10), locale)
}
