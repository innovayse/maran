import { expect, test } from '../fixtures/real-test'
import { uniqueName } from '../fixtures/real-credentials'
import { createAccountByClicking, deleteAccountByClicking, suspendAndReactivate } from '../fixtures/real-account-journey'
import { signInAs } from '../fixtures/real-sign-in'
import { openDestination, sidebarDestinations } from '../fixtures/real-sidebar-walk'

// The reseller's journeys against a running panel and agent (issue #519). A reseller is a role of
// its own: the menu is the administrator's tree filtered by role, and the backend refuses by role
// on every request. A menu item the SPA offers and the panel refuses shows up here as the 403 the
// guard fails the test on, which is how an authorization mismatch between the two (#513) is found.

test('the reseller opens every page the sidebar offers and none of them errors', async ({ page }) => {
  test.setTimeout(300_000)
  await signInAs(page, 'reseller')

  const destinations = await sidebarDestinations(page)
  expect(destinations.length, 'the sidebar offered the reseller almost nothing to walk').toBeGreaterThan(3)

  for (const destination of destinations) {
    await openDestination(page, destination)
  }
})

test('the reseller is never offered an administrator-only page', async ({ page }) => {
  await signInAs(page, 'reseller')

  const destinations = await sidebarDestinations(page)

  // The accepting control sits beside the refusal: the reseller's own overview is offered, so an
  // empty list could not satisfy the second line.
  expect(destinations).toContain('Accounts')
  expect(destinations).not.toContain('Panel certificate')
  expect(destinations).not.toContain('Panel updates')
})

test('the reseller creates, suspends, reactivates and deletes an account of its own', async ({ page }) => {
  test.setTimeout(300_000)
  const name = uniqueName('rs')
  await signInAs(page, 'reseller')

  await createAccountByClicking(page, { name, ownerEmail: `${name}@example.test` })
  await suspendAndReactivate(page)
  await deleteAccountByClicking(page, name)
  await openDestination(page, 'Accounts')
})
