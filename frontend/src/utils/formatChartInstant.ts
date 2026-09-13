import { format } from 'date-fns'
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
 * Day-month and the time of day, with no year — the axis form.
 *
 * The year is dropped deliberately, and only here: a chart spans at most seven days, its ticks
 * recur along one edge, and every label carries the same year the reader already knows. That is
 * what keeps this a separate pattern from `formatIsoTimestamp`'s and `formatUnixTimestamp`'s
 * `d MMM yyyy HH:mm` — same question, different room to answer it in.
 */
const AXIS_PATTERN = 'd MMM, HH:mm'

/**
 * Formats a chart instant — an epoch the plot positions points by — for its axis ticks and hover
 * readout, in the panel's current language.
 *
 * This exists because `UiChart` used to call date-fns directly with no locale option, which is
 * date-fns' own English — so a Russian screen read `8 Sep, 17:35` on the axis beside `8 сент. 2026`
 * in its tables. The locale mapping above is the same explicit map every date formatter in this
 * folder carries; the chart goes through it like the rest of the panel rather than formatting
 * dates a second way.
 *
 * Epoch milliseconds, not seconds: the chart's points carry `Date.parse` output, and this is the
 * one formatter whose input is that unit. The rendering is the browser's own time zone, which is
 * the operator's — the panel has no other one to use.
 * @param at The instant, as a Unix epoch in milliseconds.
 * @param locale The panel's active locale.
 * @returns The formatted instant, or the raw number as text when it cannot be read as one.
 */
export const formatChartInstant = (at: number, locale: AppLocale): string => {
  const parsed = new Date(at)

  // An unreadable instant is shown as its raw value rather than as "Invalid Date": the number is
  // what an operator needs in order to report the problem.
  if (Number.isNaN(parsed.getTime())) {
    return String(at)
  }

  return format(parsed, AXIS_PATTERN, { locale: DATE_LOCALES[locale] })
}
