import { watchEffect, type WatchHandle } from 'vue'
import type { Composer } from 'vue-i18n'
import type { Router } from 'vue-router'

/**
 * Keeps `document.title` equal to "<screen> · <product>" for the current route (WCAG 2.4.2 Page
 * Titled). Without it every screen of the SPA shares index.html's static title, so a screen reader
 * announces the same name after every navigation and the browser's tab strip and history cannot be
 * told apart.
 *
 * The screen name is the i18n key the route declares in `meta.title`; the effect reads both the
 * route and the i18n locale, so it re-runs on navigation AND when the interface language changes
 * — a title translated once at navigation time would stay in the old language until the next
 * click. It takes the router and the composer as arguments rather than injecting them because it
 * runs at boot from `main.ts`, outside any component's setup, where `useRouter`/`useI18n` are not
 * available. A route without a title (or the initial, unresolved route) shows the product name
 * alone rather than a half-built "· Maran".
 *
 * @param router The application router, whose current route names the screen.
 * @param i18n The global i18n composer that translates the key and carries the active locale.
 * @returns The handle that stops the effect (unused in the app, which lives as long as the tab).
 */
export const useDocumentTitle = (router: Router, i18n: Composer): WatchHandle => {
  return watchEffect((): void => {
    const product = i18n.t('app.title')
    const key = router.currentRoute.value.meta.title
    document.title = key === undefined ? product : `${i18n.t(key)} · ${product}`
  })
}
