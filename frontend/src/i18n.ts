import { createI18n } from 'vue-i18n'
import en from './locales/en/app.json'
import ru from './locales/ru/app.json'
import hy from './locales/hy/app.json'

/** Locale codes supported by the Maran SPA, in menu order. */
export const SUPPORTED_LOCALES = ['en', 'ru', 'hy'] as const

/** Locale codes supported by the Maran SPA. */
export type AppLocale = (typeof SUPPORTED_LOCALES)[number]

/**
 * Options passed to `createI18n`, extracted to a named constant so
 * {@link createAppI18n}'s return type can be derived from it with
 * `ReturnType` instead of hand-duplicating vue-i18n's generic signature.
 */
const i18nOptions = {
  // Composition API mode (`legacy: false`) is required to use `useI18n()`
  // inside `<script setup>` and to drive `i18n.global.locale.value` from the
  // locale store (main.ts), rather than the Options-API `$t` global mixin.
  legacy: false as const,
  locale: 'en' as AppLocale,
  fallbackLocale: 'en' as AppLocale,
  messages: { en, ru, hy },
}

/**
 * Creates the application's vue-i18n instance with all supported locale
 * message bundles preloaded and `en` as the fallback locale.
 *
 * The starting locale is `en`; `main.ts` immediately syncs it to the locale
 * store, which is the single source of truth for both the interface language
 * and the `Accept-Language` header (rules/vue.md: server messages arrive
 * already localized, so the two must never diverge).
 * @returns A configured vue-i18n instance to install with `app.use()`.
 */
export const createAppI18n = (): ReturnType<typeof createI18n<typeof i18nOptions>> => createI18n(i18nOptions)
