import { expect, test, type Page } from '@playwright/test'
import { stubEmptyModules, stubModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'

/**
 * Switches the interface language from the header's locale menu, located by role and never by its
 * (translated) name for the reason the locale-switch spec gives.
 * @param page The page under test.
 * @param language The language's own name, as the option renders it.
 * @returns Resolves once the option has been chosen.
 */
const chooseLanguage = async (page: Page, language: string): Promise<void> => {
  await page.getByRole('banner').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: language, exact: true }).click()
}

// WCAG 2.4.2 (issue #82): every screen used to share index.html's literal "Maran", so a tab strip,
// a history list and a screen reader's navigation announcement could not tell two screens apart.

test('the dashboard names itself in the tab, and the name follows a language change', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  await expect(page).toHaveTitle('System status · Maran')

  // Re-applied on the switch itself, not at the next navigation: the title is translated by an
  // effect that reads the locale, and this is the assertion that fails if it is translated once.
  await chooseLanguage(page, 'Հայերեն')
  await expect(page).toHaveTitle('Համակարգի կարգավիճակ · Maran')
})

test('the accounts list has its own title', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubAccounts(page, [])

  await page.goto('/accounts')

  await expect(page).toHaveTitle('Accounts · Maran')
})

test('the sign-in screen has its own title, in the persisted language', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await setPersistedLocale(page, 'en')

  await page.goto('/login')
  await expect(page).toHaveTitle('Sign in · Maran')

  // No banner on the auth layout, so the switcher is found by the role it exposes, not by region.
  await page.locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: 'Հայերեն', exact: true }).click()
  await expect(page).toHaveTitle('Մուտք · Maran')
})

test('an unknown path is titled as not found', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/this-route-does-not-exist')

  await expect(page).toHaveTitle('Page not found · Maran')
})

// WCAG 2.4.1: the logo and the whole sidebar sat in front of every page's first control.

test('the first Tab stop is the skip link, and activating it moves focus into the main landmark', async ({
  page,
}) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()

  await page.keyboard.press('Tab')
  const skipLink = page.getByRole('link', { name: 'Skip to content' })
  await expect(skipLink).toBeFocused()
  // Hidden until focused: it has to actually be drawn once it has focus, or a sighted keyboard
  // user lands on something they cannot see.
  await expect(skipLink).toBeVisible()
  const box = await skipLink.boundingBox()
  expect(box?.width ?? 0).toBeGreaterThan(20)

  await page.keyboard.press('Enter')
  await expect(page.getByRole('main')).toBeFocused()
  // No fragment appended: the link must not rewrite the URL.
  await expect(page).toHaveURL(/\/$/)
})
