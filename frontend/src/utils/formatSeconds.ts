import type { AppLocale } from '../types/app'

/**
 * Formats a duration the panel received in milliseconds as seconds, in the reader's language
 * ("1.5 s", "1,5 с"). One decimal place: a run by hand is measured to show whether a job took a
 * moment or a minute, not to profile it.
 * @param milliseconds The duration.
 * @param locale The panel's active locale.
 * @returns The formatted duration.
 */
export const formatSeconds = (milliseconds: number, locale: AppLocale): string => {
  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: 'second',
    unitDisplay: 'short',
    maximumFractionDigits: 1,
  }).format(milliseconds / 1000)
}
