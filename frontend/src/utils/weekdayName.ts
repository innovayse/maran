import { format } from 'date-fns'
import { enUS, hy, ru, type Locale } from 'date-fns/locale'
import type { AppLocale } from '../types/app'
import { WEEKDAY_KEYS, type WeekdayKey } from '../types/weekday'

/**
 * date-fns locale for each language the panel supports.
 *
 * An explicit map, not a lookup by string: a locale the panel offers but date-fns does not would
 * then fail at build time here, rather than silently falling back to English in front of a
 * customer. Same shape, and for the same reason, as `utils/formatDate.ts`.
 */
const DATE_LOCALES: Record<AppLocale, Locale> = {
  en: enUS,
  ru,
  hy,
}

/**
 * A Sunday, used only to walk a whole week by adding the day's index to its date.
 *
 * Constructed from parts rather than parsed from a string, because a parsed ISO date is UTC and
 * would land on the previous day for anyone west of Greenwich — which would shift every name by
 * one, in the one place a reader has no way to notice.
 */
const REFERENCE_SUNDAY_YEAR = 2024

/** Month of {@link REFERENCE_SUNDAY_YEAR}'s reference Sunday, zero-based as `Date` counts months. */
const REFERENCE_SUNDAY_MONTH = 0

/** Day of the month of the reference Sunday: 7 January 2024 was a Sunday. */
const REFERENCE_SUNDAY_DATE = 7

/**
 * The standalone name of a weekday in the panel's current language.
 *
 * The names are the date library's own, never this panel's: the same week already reaches the
 * screen through `formatDate` and friends, and a hand-written translation beside it is a second
 * statement of one fact that can disagree in case, form or spelling — most invisibly in Russian and
 * Armenian, where we are least able to see it. It also made us the maintainer of an Armenian
 * weekday translation the library maintains for us.
 *
 * `EEEE` is the nominative standalone form — what a picker wants ("Monday", "понедельник"). A
 * weekday rendered INSIDE a sentence would need a different form in Russian and is not what this
 * returns; a caller needing that must say so rather than bend this one.
 *
 * Whatever case the library returns is the language's own: English capitalises weekday names,
 * Russian and Armenian do not. The result is deliberately not normalised.
 * @param day The day to name.
 * @param locale The panel's active locale.
 * @returns The day's name in that language.
 */
export const weekdayName = (day: WeekdayKey, locale: AppLocale): string => {
  const date = new Date(
    REFERENCE_SUNDAY_YEAR,
    REFERENCE_SUNDAY_MONTH,
    REFERENCE_SUNDAY_DATE + WEEKDAY_KEYS.indexOf(day),
  )

  return format(date, 'EEEE', { locale: DATE_LOCALES[locale] })
}
