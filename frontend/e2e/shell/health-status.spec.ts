import { expect, test } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { setPersistedLocale } from '../fixtures/set-locale'

test('status page states the operational sentence and nothing machine-readable', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page, 'ok')
  await stubEmptyModules(page)

  await page.goto('/')

  // `exact: true` reads the element's whole text, so a re-appearing `(ok)` fails here rather than
  // matching as a substring of a longer sentence.
  await expect(page.getByText('All systems are operational.', { exact: true })).toBeVisible()
})

test('the operational sentence carries no English health token in a Russian interface', async ({ page }) => {
  // The screen this asserts on is the first one after login and, on a healthy panel, its only
  // line. The Russian sentence ended in a parenthesised `(ok)`: the raw `status` field of
  // `GET /health` interpolated into it, in all three bundles at once. Asserted in Russian
  // because that is where the defect is visible — in English `(ok)` reads as merely redundant.
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  // A status value the stub is free to choose, and one nobody would mistake for prose: if any
  // branch of the page still interpolates the field, this string is what would appear on screen.
  await stubHealthy(page, 'zzstatuszz')
  await stubEmptyModules(page)

  await page.goto('/')

  const card = page.getByText('Все системы работают исправно.', { exact: true })
  await expect(card).toBeVisible()
  // The value, not a bound: the page's whole text may not contain the token the backend sent, nor
  // the `ok` the real endpoint always sends.
  await expect(page.locator('body')).not.toContainText('zzstatuszz')
  await expect(page.locator('body')).not.toContainText('(ok)')
})
