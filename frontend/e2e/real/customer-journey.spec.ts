import { expect, test } from '../fixtures/real-test'
import { uniqueName } from '../fixtures/real-credentials'
import { createAccountByClicking, deleteAccountByClicking } from '../fixtures/real-account-journey'
import { signInAs, signInWith, signOut } from '../fixtures/real-sign-in'
import { openDestination, sidebarDestinations } from '../fixtures/real-sidebar-walk'

// The customer's journeys against a running panel and agent (issue #519).

/** A password that satisfies the panel's default policy, used as the account's first password. */
const INITIAL_PASSWORD = 'Initial-Password-Real-1'

/** The password the owner chooses at the first sign-in. */
const CHOSEN_PASSWORD = 'Chosen-Password-Real-1'

test('the customer opens every page the sidebar offers and none of them errors', async ({ page }) => {
  test.setTimeout(300_000)
  await signInAs(page, 'customer')

  const destinations = await sidebarDestinations(page)
  expect(destinations.length, 'the sidebar offered the customer almost nothing to walk').toBeGreaterThan(3)

  for (const destination of destinations) {
    await openDestination(page, destination)
  }
})

test('an owner given an initial password signs in, is made to choose a new one, and reaches the panel', async ({
  page,
}) => {
  test.setTimeout(300_000)
  const name = uniqueName('ip')
  await signInAs(page, 'administrator')
  await createAccountByClicking(page, {
    name,
    ownerEmail: `${name}@example.test`,
    initialPassword: INITIAL_PASSWORD,
  })
  await signOut(page)

  // Issue #514: the first sign-in lands on the new-password screen, with no shell around it and no
  // read that could only answer 403 (the guard fails the test on one).
  await signInWith(page, name, INITIAL_PASSWORD)
  await expect(page.getByRole('heading', { name: 'Choose a new password', level: 2 })).toBeVisible()
  await page.getByLabel('Current password').fill(INITIAL_PASSWORD)
  await page.getByLabel('New password').fill(CHOSEN_PASSWORD)
  await page.getByLabel('Confirm password').fill(CHOSEN_PASSWORD)
  await page.getByRole('button', { name: 'Set the password' }).click()
  await expect(page.getByRole('navigation').first()).toBeVisible()
  await signOut(page)

  // The accepting control: the chosen password is the one that works from now on.
  await signInWith(page, name, CHOSEN_PASSWORD)
  await expect(page.getByRole('navigation').first()).toBeVisible()
  await signOut(page)

  // Clean up through the administrator, the only role that may delete the account.
  await signInAs(page, 'administrator')
  await openDestination(page, 'Accounts')
  await page.getByRole('searchbox', { name: 'Search accounts' }).fill(name)
  await page.getByRole('link', { name }).click()
  await deleteAccountByClicking(page, name)
})
