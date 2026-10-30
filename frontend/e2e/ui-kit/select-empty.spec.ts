import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans } from '../fixtures/stub-plans-route'

// Issue #225: a select opened with no options says so in words instead of opening a blank box.

test('the plan chooser opened with no plans shows the empty message', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubAccounts(page, [])
  await stubPlans(page, [])

  await page.goto('/accounts/new')
  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()

  const chooser = page.getByRole('combobox', { name: 'Plan', exact: true })
  await chooser.click()

  await expect(chooser).toHaveAttribute('aria-expanded', 'true')
  await expect(page.getByRole('status').filter({ hasText: 'Nothing to choose from yet.' })).toBeVisible()
  await expect(page.getByRole('option')).toHaveCount(0)

  // Escape still closes the popup and keeps focus on the trigger.
  await page.keyboard.press('Escape')
  await expect(page.getByText('Nothing to choose from yet.')).toHaveCount(0)
  await expect(chooser).toBeFocused()
})
