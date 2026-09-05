import { formatDistanceStrict, parseISO } from 'date-fns'
import { enUS, hy, ru, type Locale } from 'date-fns/locale'
import type { AppLocale } from '../types/app'

/**
 * date-fns locale for each language the panel supports.
 *
 * An explicit map, not a lookup by string: a locale the panel offers but date-fns does not would
 * then fail at build time here, rather than silently falling back to English in front of an
 * operator.
 */
const DATE_LOCALES: Record<AppLocale, Locale> = {
  en: enUS,
  ru,
  hy,
}

/**
 * Formats how long is left until a backend instant, in the panel's current language.
 *
 * Written for a ban's expiry, which is the one value on the firewall screen that changes while
 * nobody touches the page: a row saying "in 30 minutes" is what an operator judges a ban by, and a
 * timestamp alone makes them do the arithmetic. The suffix is part of the formatting rather than a
 * separate label, because a past instant has to read as one — an expiry that has already gone by
 * says so instead of showing a distance that could be read either way.
 *
 * `now` is a parameter rather than read from the clock inside, for the same reason the backend
 * injects `IClock`: a function that reads the ambient time cannot be driven by a test, and the
 * countdown is exactly the behaviour worth driving.
 * @param isoTimestamp The instant to count towards, as the backend sent it.
 * @param now The instant to measure from, as epoch milliseconds.
 * @param locale The panel's active locale.
 * @returns The formatted distance, or the raw value when the instant cannot be parsed.
 */
export const formatCountdown = (isoTimestamp: string, now: number, locale: AppLocale): string => {
  const parsed = parseISO(isoTimestamp)

  // An unparseable timestamp is shown as-is rather than as "Invalid Date": the raw value is what an
  // operator needs in order to report the problem.
  if (Number.isNaN(parsed.getTime())) {
    return isoTimestamp
  }

  return formatDistanceStrict(parsed, new Date(now), { locale: DATE_LOCALES[locale], addSuffix: true })
}
