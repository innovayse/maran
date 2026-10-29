import { expect, test } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

test('shell renders the application title', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  // The product name is the app bar's link to the landing screen, NOT a heading (issue #82): the
  // page's own title is the document's one `<h1>`, and a wordmark `<h1>` beside it gave every
  // signed-in screen two. Asserting the wordmark as a heading protected that defect.
  await expect(page.getByRole('banner').getByRole('link', { name: 'Maran' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
})
