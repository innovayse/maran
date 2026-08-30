import { createI18n } from 'vue-i18n'
import type { AppLocale } from './types/app'

/**
 * Every locale's messages, assembled from one file per area of the panel
 * (`locales/<locale>/<area>.json`).
 *
 * Split by area rather than kept in one growing file per language: a screen's copy
 * is then edited where that screen conceptually lives, two people adding two
 * features stop colliding in the same file, and a missing translation is visible as
 * a missing key in a small file instead of a line lost among hundreds.
 *
 * The bundles are merged rather than nested under their file name, so a key reads
 * the same in every component regardless of which file it came from — moving a key
 * between files is a refactor of the folder, not of every call site.
 * @param locale The locale directory to read.
 * @returns That locale's complete message bundle.
 */
const loadMessages = (locale: AppLocale): Record<string, unknown> => {
  // Eager, not lazy: the whole panel is one bundle and the entire set of messages is
  // a few kilobytes, so splitting them across network requests would buy nothing and
  // cost a flash of untranslated interface on the first render of every screen.
  const modules = import.meta.glob<{ default: Record<string, unknown> }>('./locales/*/*.json', {
    eager: true,
  })

  const messages: Record<string, unknown> = {}
  for (const [path, module] of Object.entries(modules)) {
    if (!path.startsWith(`./locales/${locale}/`)) {
      continue
    }

    // Merged one namespace at a time rather than with a shallow Object.assign over the
    // whole file: two files may both contribute to `app`, and a shallow merge would let
    // the last one read win and silently discard the other's keys. That is exactly how
    // the `accounts` namespace was lost the first time this split was attempted.
    for (const [namespace, bundle] of Object.entries(module.default)) {
      const existing = (messages[namespace] ?? {}) as Record<string, unknown>
      messages[namespace] = { ...existing, ...(bundle as Record<string, unknown>) }
    }
  }

  return messages
}

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
  messages: {
    en: loadMessages('en'),
    ru: loadMessages('ru'),
    hy: loadMessages('hy'),
  },
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
export const createAppI18n = (): ReturnType<typeof createI18n<typeof i18nOptions>> => {
  return createI18n(i18nOptions)
}
