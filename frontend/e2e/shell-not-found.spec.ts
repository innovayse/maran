import { expect, test } from '@playwright/test'
import { stubEmptyModules } from './fixtures/stub-modules-route'
import { stubHealthy } from './fixtures/stub-health-route'

test('unknown path renders the not-found page with its i18n copy', async ({ page }) => {
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/this-route-does-not-exist')

  // Scoped to <main>: the shell's breadcrumb now names the current screen too, so
  // "Page not found" legitimately appears twice. Asserting inside the page is what
  // this test always meant — that the PAGE renders its copy, not the chrome.
  const page404 = page.getByRole('main')
  await expect(page404.getByText('Page not found')).toBeVisible()
  await expect(page404.getByText('The page you requested does not exist.')).toBeVisible()
})
