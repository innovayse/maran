import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSteeredAdministrator } from '../fixtures/stub-password-reset-routes'

test('an administrator steered into enrolment lands there from any URL, with no navigation', async ({
  page,
}) => {
  await stubSteeredAdministrator(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  // A deep link into a licensed module.
  await page.goto('/accounts')
  await expect(page).toHaveURL('/two-factor-setup')

  // Their own settings, which their token also cannot reach.
  await page.goto('/settings/sessions')
  await expect(page).toHaveURL('/two-factor-setup')

  // Even the sign-in screen, which a signed-in visitor is otherwise sent away from
  // to the panel's home page.
  await page.goto('/login')
  await expect(page).toHaveURL('/two-factor-setup')

  // No shell around it: the navigation landmark the authenticated layout renders is
  // absent, so there is nothing to click through to a screen that could only 403.
  // This can fail — the same assertion on `/settings/sessions` for an unsteered
  // administrator finds the landmark and goes red.
  await expect(page.getByRole('navigation')).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Two-step verification' })).toBeVisible()
})

test('the navigation this spec looks for is really there when nobody is steered', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/settings/sessions')

  // The control assertion for the one above: without this, "no navigation" would
  // pass on a page that never renders navigation in the first place — the exact
  // failure mode of a spec in this repository that asserted a heading was absent on
  // a screen with no headings at all.
  await expect(page.getByRole('navigation').first()).toBeVisible()
})
