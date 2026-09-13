import { format, fromUnixTime } from 'date-fns'
import { enUS, hy, ru, type Locale } from 'date-fns/locale'
import type { AppLocale } from '../types/app'

/**
 * date-fns locale for each language the panel supports.
 *
 * An explicit map, not a lookup by string: a locale the panel offers but date-fns does not would
 * then fail at build time here, rather than silently falling back to English in front of a customer.
 */
const DATE_LOCALES: Record<AppLocale, Locale> = {
  en: enUS,
  ru,
  hy,
}

/**
 * Day-month-year and the time of day, in the form every supported locale reads unambiguously.
 *
 * The minute is part of the pattern and `formatDate`'s is not, and the difference is the subject:
 * a creation date answers "when was this set up", where a cron run answers "did it fire at half
 * past three", and a date alone cannot answer the second at all.
 */
const TIMESTAMP_PATTERN = 'd MMM yyyy HH:mm'

/**
 * Formats an instant the panel received as Unix seconds.
 *
 * Seconds, not milliseconds: the Cron module reports `lastRunAtUnix` in seconds because the agent
 * does, and the panel leaves the unit exactly as it arrives rather than converting it somewhere
 * further from the reader. This is the one place that multiplies.
 *
 * The rendering is the browser's own time zone, which is the operator's — the panel has no other
 * one to use, and the module deliberately does not convert on the server for the same reason.
 * @param seconds The instant, in Unix seconds (UTC).
 * @param locale The panel's active locale.
 * @returns The formatted instant, or the raw number as text when it cannot be read as one.
 */
export const formatUnixTimestamp = (seconds: number, locale: AppLocale): string => {
  const parsed = fromUnixTime(seconds)

  // An unreadable instant is shown as its raw value rather than as "Invalid Date": the number is
  // what an operator needs in order to report the problem.
  if (Number.isNaN(parsed.getTime())) {
    return String(seconds)
  }

  return format(parsed, TIMESTAMP_PATTERN, { locale: DATE_LOCALES[locale] })
}
