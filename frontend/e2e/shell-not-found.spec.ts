import { expect, test } from '@playwright/test'
import { stubEmptyModules } from './fixtures/stub-modules-route'
import { stubHealthy } from './fixtures/stub-health-route'

test('unknown path renders the not-found page with its i18n copy', async ({ page }) => {
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/this-route-does-not-exist')

  await expect(page.getByText('Page not found')).toBeVisible()
  await expect(page.getByText('The page you requested does not exist.')).toBeVisible()
})
