import { expect, test } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthProblem } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

// This is the most important spec in this pass: rules/vue.md says "the
// backend owns their text" — the frontend must render the server's own
// RFC 7807 `detail` string verbatim, and must never substitute its own
// error copy for it.
test('status page renders the backend RFC 7807 detail verbatim and not the frontend unreachable string', async ({
  page,
}) => {
  await stubSignedIn(page)
  const backendDetail = 'The provisioning agent did not respond within the configured timeout.'
  await stubHealthProblem(page, backendDetail, 503)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByText('Could not reach the API')).toHaveCount(0)
})
