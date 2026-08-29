import type { Page } from '@playwright/test'

/**
 * Key `src/stores/locale.ts` persists the chosen interface language under.
 * Duplicated here (rather than imported) because e2e specs run against a
 * built/served app, not the source tree, and this is the one detail of the
 * store's storage contract a black-box test needs to know.
 */
const LOCALE_STORAGE_KEY = 'maran.locale'

/**
 * Drives the interface language the way the app really selects it: by
 * seeding the same `localStorage` key `useLocaleStore`'s `detectInitialLocale`
 * reads on startup, before any page script runs. This matches the store's
 * documented resolution order (persisted choice first) rather than relying
 * on `Accept-Language` or a UI toggle that may not exist yet.
 * @param page The Playwright page to seed before navigation.
 * @param locale The locale code to persist (`'en' | 'ru' | 'hy'`).
 * @returns Resolves once the init script is registered.
 */
export const setPersistedLocale = async (page: Page, locale: 'en' | 'ru' | 'hy'): Promise<void> => {
  await page.addInitScript(
    ({ key, value }) => {
      window.localStorage.setItem(key, value)
    },
    { key: LOCALE_STORAGE_KEY, value: locale },
  )
}
