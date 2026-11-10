import { expect, test } from '../fixtures/real-test'
import { uniqueName } from '../fixtures/real-credentials'
import { createAccountByClicking, deleteAccountByClicking, suspendAndReactivate } from '../fixtures/real-account-journey'
import { signInAs } from '../fixtures/real-sign-in'
import { openDestination, sidebarDestinations } from '../fixtures/real-sidebar-walk'

// The administrator's journeys against a running panel and agent (issue #519). No `page.route`, no
// stub, no typed URL: every screen is reached by the same clicks a person makes, and the guard in
// `real-test.ts` fails the test on a 4xx/5xx nobody announced, a console error or a response that
// disagrees with the committed OpenAPI baseline. The click-through audit that found #494-#515 was
// this walk done by hand once; this makes it repeatable.

test('the administrator opens every page the sidebar offers and none of them errors', async ({ page }) => {
  test.setTimeout(300_000)
  await signInAs(page, 'administrator')

  const destinations = await sidebarDestinations(page)
  // The vacuity guard on the axis that can go blind: a sidebar read as empty would walk nothing and
  // pass. An administrator's menu has dozens of pages.
  expect(destinations.length, 'the sidebar offered the administrator almost nothing to walk').toBeGreaterThan(10)

  for (const destination of destinations) {
    await openDestination(page, destination)
  }
})

test('the administrator creates, suspends, reactivates and deletes an account', async ({ page }) => {
  test.setTimeout(300_000)
  const name = uniqueName('rl')
  await signInAs(page, 'administrator')

  await createAccountByClicking(page, { name, ownerEmail: `${name}@example.test` })
  await suspendAndReactivate(page)
  await deleteAccountByClicking(page, name)

  // The list is read back from the panel: the account is gone, not merely hidden by the screen.
  await page.getByRole('searchbox', { name: 'Search accounts' }).fill(name)
  await expect(page.getByRole('link', { name })).toHaveCount(0)
})

test('the administrator views an account as its owner and the read-only session asks for nothing it is refused', async ({
  page,
}) => {
  test.setTimeout(300_000)
  const name = uniqueName('rv')
  await signInAs(page, 'administrator')
  await createAccountByClicking(page, { name, ownerEmail: `${name}@example.test` })

  await page.getByRole('button', { name: 'View as this account' }).click()
  const banner = page.getByRole('region', { name: 'Viewing as another account' })
  await expect(banner).toBeVisible()

  // Issues #513 and #515: the customer's pages open without a 403 (the guard fails the test on
  // one), and the session says it can only read.
  for (const destination of await sidebarDestinations(page)) {
    await openDestination(page, destination)
  }

  await banner.getByRole('button', { name: 'Back to admin' }).click()
  await expect(banner).toHaveCount(0)

  // Back in the administrator's own shell, find the account again from the list and remove it.
  await openDestination(page, 'Accounts')
  await page.getByRole('searchbox', { name: 'Search accounts' }).fill(name)
  await page.getByRole('link', { name }).click()
  await deleteAccountByClicking(page, name)
})
