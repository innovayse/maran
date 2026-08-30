/**
 * Locale codes supported by the Maran SPA, in menu order.
 *
 * The list and the type live together because the type is derived from the
 * list: they are one fact, and splitting them would let a locale be added to
 * one without the other, which is exactly the mistake the derivation prevents.
 */
export const SUPPORTED_LOCALES = ['en', 'ru', 'hy'] as const

/** A locale code the SPA supports. */
export type AppLocale = (typeof SUPPORTED_LOCALES)[number]
