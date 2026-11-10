import { expect, test } from '../fixtures/guarded-test'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { setPersistedLocale } from '../fixtures/set-locale'

test('status page states the operational sentence and nothing machine-readable', async ({ page }) => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  // `exact: true` reads the element's whole text, so a re-appearing `(ok)` fails here rather than
  // matching as a substring of a longer sentence.
  await expect(page.getByText('All systems are operational.', { exact: true })).toBeVisible()
})

test('the operational sentence carries no English health token in a Russian interface', async ({ page }) => {
  // The screen this asserts on is the first one after login and, on a healthy panel, its only
  // line. The Russian sentence ended in a parenthesised `(ok)`: a raw word of `GET /health`
  // interpolated into it, in all three bundles at once. Asserted in Russian because that is where
  // the defect is visible — in English `(ok)` reads as merely redundant.
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  const card = page.getByText('Все системы работают исправно.', { exact: true })
  await expect(card).toBeVisible()
  // The value, not a bound: the page's whole text may not contain either word the stub sent
  // (`stubHealthy` answers `connected` and `reachable`), nor the old `(ok)`.
  await expect(page.locator('body')).not.toContainText('connected')
  await expect(page.locator('body')).not.toContainText('reachable')
  await expect(page.locator('body')).not.toContainText('(ok)')
})
