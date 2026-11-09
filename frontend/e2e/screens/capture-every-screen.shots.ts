import { test } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { openScreen, prepareScreen, SCREENS } from './screen-list'

/*
 * The screenshot set of issue #82: every screen at 1440, 768 and 390 px, in the dark and the light
 * theme, written to `test-results/screens/<screen>-<width>-<theme>.png`.
 *
 * This is a tool, not a test. It asserts only that each screen drew (`openScreen` waits for the
 * stubbed content), so it can fail, but it proves nothing about layout: the layout is proved by the
 * sweep specs. It is outside the everyday suite twice over: `playwright.config.ts` ignores
 * `e2e/screens/`, and the file is named `.shots.ts`, not `.spec.ts`, so the spec-file census behind
 * `maran test spa` and its per-spec-file baseline never lists it. Run it with `npm run screens`
 * from `frontend/`.
 */

/** The three widths the issue asks for, each with a realistic height. */
const WIDTHS = [
  { width: 1440, height: 900 },
  { width: 768, height: 1024 },
  { width: 390, height: 844 },
] as const

/** The themes photographed; the panel is dark by default and keeps a measured light theme. */
const THEMES = ['dark', 'light'] as const

/** Key `src/stores/theme.ts` persists the chosen theme under. */
const THEME_STORAGE_KEY = 'maran.theme'

/** Directory the images are written to, relative to `frontend/`; git-ignored with `test-results/`. */
const OUTPUT_DIRECTORY = 'test-results/screens'

/**
 * Turns a screen's display name into a file-name part.
 * @param name The screen's name, e.g. `site detail, logs`.
 * @returns Lower-case words joined by hyphens.
 */
const slugOf = (name: string): string => {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
}

for (const screen of SCREENS) {
  for (const viewport of WIDTHS) {
    for (const theme of THEMES) {
      test(`${screen.name} is photographed at ${viewport.width.toString()}px in the ${theme} theme`, async ({
        page,
      }) => {
        await page.setViewportSize(viewport)
        await setPersistedLocale(page, 'en')
        // Seeded like the locale, before any page script runs, the way the store reads it on start.
        await page.addInitScript(
          ({ key, value }) => {
            window.localStorage.setItem(key, value)
          },
          { key: THEME_STORAGE_KEY, value: theme },
        )
        await prepareScreen(page, screen)
        await openScreen(page, screen)

        await page.screenshot({
          path: `${OUTPUT_DIRECTORY}/${slugOf(screen.name)}-${viewport.width.toString()}-${theme}.png`,
          fullPage: true,
        })
      })
    }
  }
}
