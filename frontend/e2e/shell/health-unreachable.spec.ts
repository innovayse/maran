import { expect, test } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthUnreachable } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

test('status page shows the frontend-owned unreachable message when the backend cannot be reached', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthUnreachable(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByText('Could not reach the API')).toBeVisible()
})
