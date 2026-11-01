import { expect, test } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'

// Issue #149: `index.html` declares `lang="en"` for the frame before any script runs, and the
// locale store replaces it. These cover the two paths the shell's own switcher spec does not: a
// language that is already chosen at boot, and the switcher on a signed-out screen.

test('a panel booted in Armenian declares Armenian, and its skip link is Armenian too', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'hy')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.locator('html')).toHaveAttribute('lang', 'hy')
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Անցնել բովանդակությանը' })).toBeFocused()
})

test('the language switcher on the sign-in screen moves the document language with it', async ({ page }) => {
  await stubSignedOut(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/login')
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')

  await page.getByTestId('locale-switcher').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: 'Русский', exact: true }).click()

  await expect(page.locator('html')).toHaveAttribute('lang', 'ru')
})
