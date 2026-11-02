import { expect, test, type Page } from '@playwright/test'
import { FULL_CATALOGUE, mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #320: the dashboard item carries the needs-attention count, read from a narrow endpoint. A
// figure the panel could not take draws no badge at all, never a wrong one. The mail queue and the
// available updates have a place in the menu but no data source yet: their pages show no badge and
// the shell never asks for them. Navigation is by clicks only.

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The narrow endpoint the needs-attention badge is read from. */
const ATTENTION_COUNT = /\/api\/v1\/dashboard\/attention-count(\?.*)?$/

/**
 * Opens the administrator's shell with the attention endpoint answering what a spec names. The
 * answer is installed after the shell's catch-all so it wins, and the page is reloaded because the
 * shell reads the figure when it mounts.
 * @param page The Playwright page under test.
 * @param status The HTTP status the endpoint answers with.
 * @param body The JSON body it answers with.
 * @returns Resolves once the reloaded shell is drawn.
 */
const openWithAttention = async (page: Page, status: number, body: unknown): Promise<void> => {
  await openFullShell(page)
  await page.route(ATTENTION_COUNT, async (route) => {
    await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  })
  await page.reload()
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
}

test('the status item shows how many things need attention and says what it counts', async ({ page }) => {
  await openWithAttention(page, 200, { state: 'loaded', data: 4 })

  // The count is announced with what it counts, in the link's accessible name.
  const status = mainNavigation(page).getByRole('link', { name: 'System status 4 need attention' })
  await expect(status).toBeVisible()
  await status.click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
})

test('nothing needing attention draws no badge', async ({ page }) => {
  await openWithAttention(page, 200, { state: 'loaded', data: 0 })

  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()
})

test('a figure the panel could not take draws no badge rather than a wrong one', async ({ page }) => {
  await openWithAttention(page, 200, { state: 'unavailable' })

  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /need attention/ })).toHaveCount(0)
})

test('a refused or failed read draws no badge either', async ({ page }) => {
  await openWithAttention(page, 503, { title: 'Service unavailable', status: 503 })

  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /need attention/ })).toHaveCount(0)
})

test('the mail queue and the updates have no badge and the shell never asks for them', async ({ page }) => {
  const requests: string[] = []
  page.on('request', (request) => {
    requests.push(request.url())
  })
  await openFullShell(page)
  const nav = mainNavigation(page)

  await openSection(page, 'Mail')
  await expect(nav.getByRole('link', { name: 'Mail queue Coming soon' })).toHaveAttribute('aria-disabled', 'true')
  await openSection(page, 'Servers')
  await expect(nav.getByRole('link', { name: 'Updates Coming soon' })).toHaveAttribute('aria-disabled', 'true')

  // Neither page has a figure, so neither row announces one and no request is made for one.
  await expect(nav.getByRole('link', { name: /in the mail queue|updates available/ })).toHaveCount(0)
  // Only the panel's API is judged: the dev server's own module URLs are not requests for a figure.
  expect(requests.filter((url) => {return url.includes('/api/v1/') && /queue|updates/.test(url)})).toEqual([])
})

test('a customer is never shown, and never asks for, the server-wide figure', async ({ page }) => {
  const requests: string[] = []
  page.on('request', (request) => {
    requests.push(request.url())
  })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto('/')
  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()

  expect(requests.filter((url) => {return ATTENTION_COUNT.test(url)})).toEqual([])
})
