import { expect, test, type ConsoleMessage, type Locator, type Page } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

// The truth of the STORED locale, not just the visible one. Born from a live
// finding: a tab showed Russian while `localStorage['maran.locale']` already
// read 'en' (written by another same-origin tab), so client-side navigations
// kept Russian and the next full load silently booted English. These specs pin
// the three seams that close that class: the stored value survives a reload
// unchanged (so no boot-time writer can ever quietly restore a default), a
// foreign same-origin write is adopted live instead of festering until the next
// boot, and a FAILED persist is observable instead of indistinguishable from a
// successful one. The sibling `locale-switch.spec.ts` covers the switcher's
// visible behavior; this file covers what storage holds and who may write it.

/** The exact warning `stores/locale.ts` emits when persisting the choice fails. */
const PERSIST_FAILURE_WARNING =
  'Maran: the chosen interface language could not be persisted; it applies now but will not survive a reload.'

/** Storage key the locale store persists under (duplicated: black-box specs). */
const LOCALE_STORAGE_KEY = 'maran.locale'

/**
 * The header's locale menu trigger, located by ROLE inside the banner — never
 * by its accessible name, which is itself translated and would stop matching
 * the moment the language it just changed took effect.
 * @param page The page under test.
 * @returns The trigger's locator.
 */
const localeTrigger = (page: Page): Locator => {
  return page.getByRole('banner').locator('[aria-haspopup="menu"]')
}

/**
 * Chooses a language from the header's locale menu through the real pointer
 * path — the same path a user takes.
 * @param page The page under test.
 * @param language The language's own name, as the option renders it.
 * @returns Resolves once the option has been chosen.
 */
const chooseLanguage = async (page: Page, language: string): Promise<void> => {
  await localeTrigger(page).click()
  await page.getByRole('menuitemradio', { name: language, exact: true }).click()
}

/**
 * Reads the persisted locale value exactly as the store's boot would.
 * @param page The page under test.
 * @returns The raw stored value, or null when nothing is stored.
 */
const storedLocale = async (page: Page): Promise<string | null> => {
  return page.evaluate((key: string) => {
    return window.localStorage.getItem(key)
  }, LOCALE_STORAGE_KEY)
}

// Deliberately does NOT seed the locale fixture: `setPersistedLocale` installs
// an init script, and an init script re-runs on the reload and would re-write
// the stored value — the test would assert the fixture, not the store. (That
// re-run is itself one way a live session can manufacture the very anomaly this
// file guards against.) The browser's default locale is English, which is the
// starting point this test needs anyway.
test('switching to Russian survives a hard reload in both the visible language and the stored value', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  /** Warnings the app emitted; a successful persist must not produce the failure warning. */
  const warnings: string[] = []
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'warning') {
      warnings.push(message.text())
    }
  })

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()

  await chooseLanguage(page, 'Русский')

  // Positive control before the reload: the switch really happened and really persisted.
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()
  expect(await storedLocale(page)).toBe('ru')

  await page.reload()

  // The exact visible string AND the exact stored value: a future boot-time
  // writer restoring the default would flip both back to English/'en'.
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'ru')
  expect(await storedLocale(page)).toBe('ru')

  // Inverse control: a working persist emits no failure warning.
  expect(warnings).not.toContain(PERSIST_FAILURE_WARNING)
})

test('a language chosen in a second tab reaches an already-open tab live, without a reload', async ({
  page,
  context,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()

  // A second tab in the same browser context shares the same origin storage —
  // the exact configuration the live anomaly was produced in.
  const second = await context.newPage()
  await stubSignedIn(second)
  await stubHealthy(second)
  await stubEmptyModules(second)
  await second.goto('/')
  await expect(second.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()

  await chooseLanguage(second, 'Русский')
  await expect(second.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()

  // The FIRST tab, untouched and unreloaded, adopts the newest choice (last
  // write wins): visible language, document language and stored value agree.
  // Without the store's `storage` listener this tab would keep showing English
  // over a stored 'ru' — the silent divergence that later surfaces as a
  // mystery language flip on the next full load.
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'ru')
  expect(await storedLocale(page)).toBe('ru')

  // Convergence is stable: the adopting tab did not write back a fight — the
  // stored value is still the second tab's choice after a full boot of tab one.
  await page.reload()
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()
  expect(await storedLocale(page)).toBe('ru')
})

test('a failed persist still switches the interface and is surfaced by the exact console warning', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  // Break ONLY this key's write, before any page script runs: the storage that
  // throws on `setItem` is the private-mode/full-quota browser the store's
  // read-back exists for. Reads stay real, so the read-back genuinely observes
  // that nothing was written.
  await page.addInitScript((key: string) => {
    // Bound to the real localStorage before the patch, so unaffected keys still write for real.
    const write = window.localStorage.setItem.bind(window.localStorage)
    Storage.prototype.setItem = (itemKey: string, value: string): void => {
      if (itemKey === key) {
        throw new Error('storage is full')
      }
      write(itemKey, value)
    }
  }, LOCALE_STORAGE_KEY)

  /** Warnings the app emitted while switching under a broken storage. */
  const warnings: string[] = []
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'warning') {
      warnings.push(message.text())
    }
  })

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()

  await chooseLanguage(page, 'Русский')

  // The interface MUST still switch: persistence is a convenience, never a gate.
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()

  // The write really failed — nothing is stored…
  expect(await storedLocale(page)).toBeNull()

  // …and the failure is observable: the exact warning, not silence. This is
  // the line that distinguishes today's store from one that swallows the
  // failure and lets the next reload silently boot the wrong language.
  await expect
    .poll(() => {
      return warnings.filter((text: string) => {
        return text === PERSIST_FAILURE_WARNING
      }).length
    })
    .toBe(1)
})
