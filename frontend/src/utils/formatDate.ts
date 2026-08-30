import { format, parseISO } from 'date-fns'
import { enUS, hy, ru, type Locale } from 'date-fns/locale'
import type { AppLocale } from '../types/app'

/**
 * date-fns locale for each language the panel supports.
 *
 * An explicit map, not a lookup by string: a locale the panel offers but
 * date-fns does not would then fail at build time here, rather than silently
 * falling back to English in front of a customer.
 */
const DATE_LOCALES: Record<AppLocale, Locale> = {
  en: enUS,
  ru,
  hy,
}

/** Day-month-year pattern, the form every supported locale reads unambiguously. */
const DATE_PATTERN = 'd MMM yyyy'

/**
 * Formats a backend timestamp for display in the panel's current language.
 *
 * The backend sends instants as ISO-8601 and never as pre-formatted text, so the
 * panel decides how a date reads. It decides with the locale the user chose, not
 * the browser's: the panel has one source of truth for language (rules/vue.md),
 * and a list showing Russian labels beside American dates is the visible symptom
 * of ignoring it.
 *
 * The pattern is spelled out rather than left to each locale's "short" form,
 * because those disagree on digit order — `01/02/2026` is January in `en` and
 * February in `ru`, and a hosting panel showing a creation or expiry date must
 * not be ambiguous about which.
 * @param isoTimestamp An ISO-8601 instant as sent by the backend.
 * @param locale The panel's active locale.
 * @returns The formatted date, or the raw value when it cannot be parsed.
 */
export const formatDate = (isoTimestamp: string, locale: AppLocale): string => {
  const parsed = parseISO(isoTimestamp)

  // An unparseable timestamp is shown as-is rather than as "Invalid Date": the
  // raw value is what an operator needs in order to report the problem.
  if (Number.isNaN(parsed.getTime())) {
    return isoTimestamp
  }

  return format(parsed, DATE_PATTERN, { locale: DATE_LOCALES[locale] })
}
