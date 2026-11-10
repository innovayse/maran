import { expect, test } from '../fixtures/guarded-test'
import { openApps } from '../fixtures/open-apps'
import { appsWorld } from '../fixtures/stub-apps-routes'
import { EMPTY_STACK, READY_STACK } from '../fixtures/stub-stack-routes'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #517: a host with no site web server has exactly one meaningful screen. The stack guard
// holds an administrator on the setup wizard from every URL, holds everyone else on a plain "being
// set up" screen, and lets a ready host through. Each redirect is paired with the same URL opening
// when the host is ready, so a guard that redirects everything cannot pass.

/** A customer: someone who cannot fix a host that has no web server. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-0000000000c1',
  username: 'carol',
  email: 'carol@example.com',
  role: 'customer',
  accountId: '22222222-2222-2222-2222-222222222222',
}

test('an administrator is sent to the setup wizard from any address while no web server is installed', async ({ page }) => {
  await openApps(page, appsWorld(), { path: '/apps', stack: EMPTY_STACK })

  await expect(page).toHaveURL(/\/setup\/stack$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Set up this server' })).toBeVisible()
})

test('the same address opens when the host is ready', async ({ page }) => {
  await openApps(page, appsWorld(), { path: '/apps', stack: READY_STACK })

  await expect(page).toHaveURL(/\/apps$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Apps' })).toBeVisible()
})

test('the wizard cannot be re-entered once the host is ready', async ({ page }) => {
  await openApps(page, appsWorld(), { path: '/setup/stack', stack: READY_STACK })

  await expect(page).not.toHaveURL(/\/setup\/stack$/)
})

test('everyone else is held on the "being set up" screen, and cannot reach the wizard', async ({ page }) => {
  await openApps(page, appsWorld(), { path: '/', stack: EMPTY_STACK, user: CUSTOMER })

  await expect(page).toHaveURL(/\/setup\/pending$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Panel is being set up' })).toBeVisible()
  await expect(page.getByText('This panel is being set up')).toBeVisible()

  await page.goto('/setup/stack')
  await expect(page).toHaveURL(/\/setup\/pending$/)
})

test('a stack status that cannot be read lets the visit through, because the server is the boundary', async ({
  page,
  guard,
}) => {
  guard.allow(503, /\/api\/v1\/apps\/stack$/)

  await openApps(page, appsWorld(), { path: '/apps', stack: 'unreadable' })

  await expect(page).toHaveURL(/\/apps$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Apps' })).toBeVisible()
})
