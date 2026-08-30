import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

/**
 * Chooses a language from the header's locale select.
 *
 * The switcher is a menu: a trigger that opens `menuitemradio` options, which
 * is why this cannot be `selectOption`. Driving it through the roles the ARIA
 * pattern promises is also the point — if the roles regress, every locale test
 * fails, which is exactly what should happen.
 *
 * It is located by ROLE inside the header, never by its accessible name: that
 * name is itself translated, so a helper that named it in English stopped
 * finding it the moment it had switched the page to Russian — the test would
 * have been asserting against the language it had just changed.
 * @param page The page under test.
 * @param language The language's own name, as the option renders it.
 * @returns Resolves once the option has been chosen.
 */
const localeTrigger = (page: Page): Locator => {
  return page.getByRole('banner').locator('[aria-haspopup="menu"]')
}

/**
 * Chooses a language from the header's locale menu.
 * @param page The page under test.
 * @param language The language's own name, as the option renders it.
 * @returns Resolves once the option has been chosen.
 */
const chooseLanguage = async (page: Page, language: string): Promise<void> => {
  await localeTrigger(page).click()
  await page.getByRole('menuitemradio', { name: language, exact: true }).click()
}

// The sibling `shell-locale.spec.ts` covers the store's persisted-choice
// startup path. This file covers the switcher itself: rules/vue.md makes the
// locale store the single source of truth feeding BOTH the i18n chrome and
// the `Accept-Language` header, so one click must move both at once, and the
// choice must survive a reload.

test('the locale switcher changes the interface language for all three locales in turn', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()

  await chooseLanguage(page, 'Русский')
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()

  await chooseLanguage(page, 'Հայերեն')
  await expect(page.getByRole('heading', { level: 1, name: 'Համակարգի կարգավիճակ' })).toBeVisible()

  await chooseLanguage(page, 'English')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()
})

test('the switcher shows the active language on its trigger', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  const trigger = localeTrigger(page)
  await expect(trigger).toHaveText('English')

  await chooseLanguage(page, 'Русский')

  await expect(trigger).toHaveText('Русский')
})

// Deliberately does NOT seed `setPersistedLocale`: that fixture uses an init
// script, which re-runs on the reload and would re-write the stored value,
// asserting the fixture rather than the store. The default browser locale is
// English, which is the starting point this test needs anyway.
test('the chosen locale survives a reload', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  await chooseLanguage(page, 'Հայերեն')
  await expect(page.getByRole('heading', { level: 1, name: 'Համակարգի կարգավիճակ' })).toBeVisible()

  await page.reload()

  await expect(page.getByRole('heading', { level: 1, name: 'Համակարգի կարգավիճակ' })).toBeVisible()
})

// rules/vue.md: the store feeding `Accept-Language` is what keeps server error
// text in the same language as the chrome. A Russian interface asking the
// backend for English messages is the exact bug that rule exists to prevent.
test('requests carry the chosen locale in Accept-Language so server text matches the interface', async ({
  page,
}) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'ru')
  await stubEmptyModules(page)

  /** `Accept-Language` header of every `/health` request the app made. */
  const acceptLanguages: string[] = []
  await page.route('**/health', async (route) => {
    acceptLanguages.push(route.request().headers()['accept-language'] ?? '')
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'ok', agent: 'connected' }),
    })
  })

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()

  expect(acceptLanguages).toContain('ru, en;q=0.8')
})

test('the document language attribute follows the chosen locale', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')

  await chooseLanguage(page, 'Русский')

  await expect(page.locator('html')).toHaveAttribute('lang', 'ru')
})
