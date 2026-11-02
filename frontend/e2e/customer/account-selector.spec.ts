import { expect, test, type Locator, type Page } from '@playwright/test'
import { FULL_CATALOGUE } from '../fixtures/open-full-shell'
import {
  stubAccountMemberships,
  stubRefreshFollowsActiveAccount,
  type MembershipsState,
} from '../fixtures/stub-account-memberships-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #313: one person who may act for several hosting accounts switches between them from the
// app bar without signing out. The selector is drawn only with two or more accounts; choosing
// another reloads the panel on its landing screen, where the session (restored from the refresh
// cookie) names the chosen account. Navigation is by clicks only: the one page.goto is the
// panel's entry point, as in every other spec.

const OLIVE_ACCOUNT = '11111111-1111-1111-1111-111111111111'
const SHOP_ACCOUNT = '22222222-2222-2222-2222-222222222222'

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OLIVE_ACCOUNT,
}

const twoAccounts = (): MembershipsState => {
  return {
    accounts: [
      { accountId: OLIVE_ACCOUNT, name: 'olive' },
      { accountId: SHOP_ACCOUNT, name: 'shop' },
    ],
    activeAccountId: OLIVE_ACCOUNT,
    switchRequests: [],
  }
}

const openAs = async (page: Page, user: AuthenticatedUser, state: MembershipsState): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, user)
  await stubRefreshFollowsActiveAccount(page, user, state)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubAccountMemberships(page, state)
  await page.goto('/')
}

const selectorFor = (page: Page, accountName: string): Locator => {
  return page.getByRole('button', { name: `Current account: ${accountName}. Switch account` })
}

test('a customer with two accounts sees the selector naming the active one', async ({ page }) => {
  await openAs(page, CUSTOMER, twoAccounts())

  await expect(selectorFor(page, 'olive')).toBeVisible()
})

test('choosing the other account switches it and the panel then acts for it', async ({ page }) => {
  const state = twoAccounts()
  await openAs(page, CUSTOMER, state)

  await selectorFor(page, 'olive').click()
  await expect(page.getByRole('menuitemradio', { name: 'olive' })).toHaveAttribute('aria-checked', 'true')
  await expect(page.getByRole('menuitemradio', { name: 'shop' })).toHaveAttribute('aria-checked', 'false')
  await page.getByRole('menuitemradio', { name: 'shop' }).click()

  // The panel reloaded: the session restored from the refresh cookie now names the other account.
  await expect(selectorFor(page, 'shop')).toBeVisible()
  expect(state.switchRequests).toEqual([{ accountId: SHOP_ACCOUNT }])
})

test('a refused switch keeps the person where they are and says why', async ({ page }) => {
  const state = twoAccounts()
  state.refuseSwitchWith = { status: 404, detail: 'This account could not be found.' }
  await openAs(page, CUSTOMER, state)

  await selectorFor(page, 'olive').click()
  await page.getByRole('menuitemradio', { name: 'shop' }).click()

  await expect(page.getByText('This account could not be found.')).toBeVisible()
  await expect(selectorFor(page, 'olive')).toBeVisible()
})

test('a customer with one account is not offered a selector', async ({ page }) => {
  const state = twoAccounts()
  state.accounts = [{ accountId: OLIVE_ACCOUNT, name: 'olive' }]
  await openAs(page, CUSTOMER, state)

  await expect(page.getByTestId('account-selector')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Switch account/ })).toHaveCount(0)
})

test('an administrator is not offered a selector and the panel never asks for accounts', async ({ page }) => {
  const asked: string[] = []
  page.on('request', (request) => {
    if (request.url().includes('/api/v1/me/accounts')) {
      asked.push(request.url())
    }
  })
  await openAs(page, { ...CUSTOMER, role: 'admin', accountId: null }, twoAccounts())

  await expect(page.getByTestId('account-selector')).toHaveCount(0)
  expect(asked).toEqual([])
})
