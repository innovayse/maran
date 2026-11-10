import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { STEERED_CUSTOMER, stubRequiredPasswordChange } from '../fixtures/stub-required-password-change-routes'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubQuietOwnAccount } from '../fixtures/stub-quiet-own-account'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'

/** The new password the specs choose; long enough that no client rule could matter, because none exists. */
const NEW_PASSWORD = 'a-password-only-I-know'

test('a login holding a first password lands on the new-password screen from any URL, with no shell', async ({
  page,
}) => {
  const recorded = await stubRequiredPasswordChange(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  // A deep link into the panel, then the sign-in screen: both end on the same screen.
  await page.goto('/sites')
  await expect(page).toHaveURL('/change-password')
  await page.goto('/login')
  await expect(page).toHaveURL('/change-password')

  await expect(page.getByRole('heading', { name: 'Choose a new password', level: 2 })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
  // No shell around it: nothing to click through to a screen that could only answer 403.
  await expect(page.getByRole('navigation')).toHaveCount(0)
  // And the requests that would answer 403 were never made (issue #514's 403 storm).
  expect(recorded.shellReads).toEqual([])
})

test('choosing the new password sends it, renews the session and enters the panel', async ({ page }) => {
  const recorded = await stubRequiredPasswordChange(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  // Installed after the stub above on purpose: once the password is accepted the customer enters
  // the panel, and these later routes answer the reads the shell then makes.
  await stubShellReads(page)
  // The customer's shell reads the account it acts for once the password is accepted.
  await stubQuietOwnAccount(page, STEERED_CUSTOMER.accountId ?? '')
  await stubEmptyTasks(page)
  await stubLandingReads(page, 'customer')

  await page.goto('/change-password')
  await page.getByLabel('Current password').fill('the-initial-password')
  await page.getByLabel('New password').fill(NEW_PASSWORD)
  await page.getByLabel('Confirm password').fill(NEW_PASSWORD)
  await page.getByRole('button', { name: 'Set the password' }).click()

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('navigation').first()).toBeVisible()
  expect(recorded.changeBodies).toEqual([{ currentPassword: 'the-initial-password', newPassword: NEW_PASSWORD }])
})

test('two different new passwords are caught before anything is sent', async ({ page }) => {
  const recorded = await stubRequiredPasswordChange(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/change-password')
  await page.getByLabel('Current password').fill('the-initial-password')
  await page.getByLabel('New password').fill(NEW_PASSWORD)
  await page.getByLabel('Confirm password').fill('something-else-entirely')
  await page.getByRole('button', { name: 'Set the password' }).click()

  await expect(page.getByText('The passwords do not match.')).toBeVisible()
  expect(recorded.changeBodies).toEqual([])
})

test('a refusal is shown as the panel worded it and the person stays on the screen', async ({ page, guard }) => {
  // The refusal is the subject of this test.
  guard.allow(400, /\/api\/v1\/auth\/change-password$/)
  const refusal = 'Choose a password of at least 12 characters.'
  await stubRequiredPasswordChange(page, refusal)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/change-password')
  await page.getByLabel('Current password').fill('the-initial-password')
  await page.getByLabel('New password').fill('short')
  await page.getByLabel('Confirm password').fill('short')
  await page.getByRole('button', { name: 'Set the password' }).click()

  await expect(page.getByText(refusal)).toBeVisible()
  await expect(page).toHaveURL('/change-password')
})

test('signing out of the new-password screen returns to the sign-in screen', async ({ page }) => {
  await stubRequiredPasswordChange(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/change-password')
  await page.getByRole('button', { name: 'Sign out' }).click()

  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
})

test('the new-password screen is not offered to a login that has nothing to replace', async ({ page }) => {
  // The control for the specs above: an ordinary signed-in session opening the same address is
  // sent into the panel, so "lands on /change-password" is the steer's doing and not a screen that
  // every signed-in visitor sees.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubLandingReads(page)

  await page.goto('/change-password')

  await expect(page).not.toHaveURL('/change-password')
  await expect(page.getByRole('navigation').first()).toBeVisible()
})
