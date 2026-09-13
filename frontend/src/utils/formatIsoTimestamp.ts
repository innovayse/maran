import { format, parseISO } from 'date-fns'
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
 * The same pattern `formatUnixTimestamp` uses, because it answers the same question about a
 * different unit — this file's input is the ISO-8601 string most of the panel's DTOs carry, where
 * that one's is the Unix seconds the Cron module reports.
 */
const TIMESTAMP_PATTERN = 'd MMM yyyy HH:mm'

/**
 * Formats an ISO-8601 instant with its time of day.
 *
 * Distinct from `formatDate`, which prints the day alone, and the difference is the subject: an
 * account's creation date answers "when was this set up", where a backup answers "which of today's
 * four copies is this". Two backups taken an hour apart render as one indistinguishable date under
 * `formatDate`, and picking the wrong one to delete is unrecoverable.
 *
 * The rendering is the browser's own time zone, which is the operator's — the panel has no other
 * one to use.
 * @param isoTimestamp An ISO-8601 instant as sent by the backend.
 * @param locale The panel's active locale.
 * @returns The formatted instant, or the raw value when it cannot be parsed.
 */
export const formatIsoTimestamp = (isoTimestamp: string, locale: AppLocale): string => {
  const parsed = parseISO(isoTimestamp)

  // An unparseable instant is shown as-is rather than as "Invalid Date": the raw value is what an
  // operator needs in order to report the problem.
  if (Number.isNaN(parsed.getTime())) {
    return isoTimestamp
  }

  return format(parsed, TIMESTAMP_PATTERN, { locale: DATE_LOCALES[locale] })
}
