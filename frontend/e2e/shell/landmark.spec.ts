import { expect, test } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

test('document has exactly one main landmark', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.locator('main')).toHaveCount(1)
})
