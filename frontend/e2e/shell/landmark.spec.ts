import { expect, test } from '../fixtures/guarded-test'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

test('document has exactly one main landmark', async ({ page }) => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.locator('main')).toHaveCount(1)
})
