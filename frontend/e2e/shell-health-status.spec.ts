import { expect, test } from '@playwright/test'
import { stubEmptyModules } from './fixtures/stub-modules-route'
import { stubHealthy } from './fixtures/stub-health-route'

test('status page shows the operational state with the backend-reported status value', async ({ page }) => {
  await stubHealthy(page, 'ok')
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByText('All systems operational (ok)')).toBeVisible()
})
