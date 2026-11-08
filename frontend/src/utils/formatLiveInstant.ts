import type { AppLocale } from '../types/app'

/**
 * Formats the time of day of an instant down to the second, in the panel's current language.
 *
 * The live charts read a new point every two seconds, so the minute-only axis form
 * (`formatChartInstant`) cannot tell two neighbouring readings apart; this one can. `Intl` is the
 * browser's own and knows all three of the panel's locales, so no table of patterns is kept here.
 * The rendering is in the browser's time zone, which is the operator's.
 * @param at The instant, as a Unix epoch in milliseconds.
 * @param locale The panel's active locale.
 * @returns For example `14:03:07`, or the raw number as text when it cannot be read as an instant.
 */
export const formatLiveInstant = (at: number, locale: AppLocale): string => {
  const instant = new Date(at)
  if (Number.isNaN(instant.getTime())) {
    return String(at)
  }
  return instant.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' })
}
