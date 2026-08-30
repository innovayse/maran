import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'

/** The account under test, active to begin with. */
const ACTIVE: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

/** The same account after the panel has suspended it. */
const SUSPENDED: Account = { ...ACTIVE, status: 'suspended' }

/**
 * Puts the accounts module in the catalogue and answers the detail read.
 * @param page The Playwright page whose network the routes are installed on.
 * @param account The account the panel reports.
 * @returns Resolves once the routes are installed.
 */
const stubDetail = async (page: Page, account: Account): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await page.route(`**/api/v1/accounts/${account.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(account) })
  })
}

test('the detail page shows the account and offers suspension while it is active', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)

  await expect(page.getByText('acme.example.com').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate' })).toHaveCount(0)
})

test('suspending asks first, and names what will happen', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()

  // Not "are you sure": the operator is being asked to weigh a consequence their
  // customer sees within seconds.
  await expect(page.getByText('Its sites stop serving')).toBeVisible()
})

test('a confirmed suspension shows the new state without a reload', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SUSPENDED) })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await page.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByText('Suspended')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate' })).toBeVisible()
})

test('a refused action shows the panel message verbatim and changes nothing', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'The agent is unavailable.', detail: 'The agent is unavailable.', code: 'AgentUnavailable' }),
    })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await page.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByText('The agent is unavailable.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible()
})

test('a deletion returns to the list', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route(`**/api/v1/accounts/${ACTIVE.id}`, async (route) => {
    if (route.request().method() !== 'DELETE') {
      await route.fallback()
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: '4096' })
  })
  await page.route('**/api/v1/accounts', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page).toHaveURL('/accounts')
})

test('the list opens an account by its name', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route('**/api/v1/accounts', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([ACTIVE]) })
  })

  await page.goto('/accounts')
  await page.getByRole('link', { name: 'acme' }).click()

  await expect(page).toHaveURL(`/accounts/${ACTIVE.id}`)
})
