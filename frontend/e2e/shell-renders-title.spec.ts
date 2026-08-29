import { expect, test } from '@playwright/test'
import { stubEmptyModules } from './fixtures/stub-modules-route'
import { stubHealthy } from './fixtures/stub-health-route'

test('shell renders the application title', async ({ page }) => {
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'Maran' })).toBeVisible()
})
