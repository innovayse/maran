import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { type AppLocale, SUPPORTED_LOCALES } from '../types/app'

/**
 * Key the chosen locale is persisted under, so a reload keeps the user's language.
 */
const STORAGE_KEY = 'maran.locale'

/**
 * Reads the initial locale: a previously chosen one, else the browser's preference when it is
 * supported, else English. Never throws — storage access fails in private modes and embedded
 * webviews, and a language preference must not be able to break the shell.
 *
 * @returns The locale the application should start in.
 */
const detectInitialLocale = (): AppLocale => {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY)
    if (stored !== null && (SUPPORTED_LOCALES as readonly string[]).includes(stored)) {
      return stored as AppLocale
    }
  } catch {
    // Storage unavailable — fall through to the browser preference.
  }

  const browserLanguage = navigator.language.split('-')[0]
  return (SUPPORTED_LOCALES as readonly string[]).includes(browserLanguage)
    ? (browserLanguage as AppLocale)
    : 'en'
}

/**
 * The single source of truth for the interface language. Both the i18n instance (UI chrome) and
 * `useApi`'s `Accept-Language` header (server-produced messages) read from here, so the panel
 * never shows an English interface alongside Russian error text.
 */
export const useLocaleStore = defineStore('locale', () => {
  /** The active interface language. */
  const current: Ref<AppLocale> = ref(detectInitialLocale())

  /** The value sent as `Accept-Language`, so the backend localizes its messages to match. */
  const acceptLanguageHeader: ComputedRef<string> = computed(() => {
    return `${current.value}, en;q=0.8`
  })

  /**
   * Switches the interface language and remembers the choice.
   *
   * @param locale The language to switch to.
   * @returns Nothing; state updates synchronously.
   */
  const setLocale = (locale: AppLocale): void => {
    current.value = locale
    try {
      window.localStorage.setItem(STORAGE_KEY, locale)
    } catch {
      // Persisting is a convenience: an unavailable storage must not break switching.
    }
  }

  return { current, acceptLanguageHeader, setLocale }
})
