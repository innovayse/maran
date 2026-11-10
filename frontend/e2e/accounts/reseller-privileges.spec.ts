import { expect, test } from '../fixtures/guarded-test'
import { stubAccountResources } from '../fixtures/stub-account-resources-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { ACME_HOSTING } from '../fixtures/stub-resellers-routes'
import type { AccountDetail } from '../../src/types/account'
import type { Page } from '@playwright/test'

// Issues #482 and #488: what a reseller may not do to the way a customer signs in. It is offered no
// control to change the owner's address (that decides where the next password reset goes) and no
// way to choose the owner's first password; an administrator keeps both. The screen only advises;
// the backend refuses the same requests (AccountResellerPrivilegeEndpointTests).

/** The plan the account is on. */
const [PLAN] = THREE_PLANS

/** The account the detail page shows. */
const ACCOUNT: AccountDetail = {
  id: '00000000-0000-0000-0000-0000000000f1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: PLAN.id,
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-30T09:00:00+00:00',
  resellerId: ACME_HOSTING.id,
  resellerName: ACME_HOSTING.name,
  planDisplayName: PLAN.displayName,
  ownerEmail: 'owner@acme.example.com',
}

/**
 * Signs in as a reseller or as an administrator and stubs what the account screens read.
 * @param page The Playwright page whose network the routes are installed on.
 * @param asReseller Whether the caller is the reseller; an administrator otherwise.
 * @returns Resolves once the routes are installed.
 */
const signIn = async (page: Page, asReseller: boolean): Promise<void> => {
  if (asReseller) {
    await stubResellerSession(page, ACME_HOSTING, RESELLER_MODULES)
  } else {
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubModules(page, [
      { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    ])
    await stubShellReads(page)
  }
  await stubAccountResources(page, { accountId: ACCOUNT.id, resources: [] })
  await stubInvitation(page, ACCOUNT.id, 'pending')
  await stubAccounts(page, [])
  await stubPlans(page, THREE_PLANS)
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
}

test('a reseller is not offered the change of an account owner email', async ({ page }) => {
  await signIn(page, true)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  await expect(page.getByRole('button', { name: 'Change plan' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Change owner email' })).toHaveCount(0)
})

test('an administrator is offered the change of an account owner email', async ({ page }) => {
  await signIn(page, false)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  await expect(page.getByRole('button', { name: 'Change owner email' })).toBeVisible()
})

test('a reseller creating an account is offered the invitation alone, with no initial password', async ({ page }) => {
  await signIn(page, true)

  await page.goto('/accounts/new')

  await expect(page.getByLabel('Primary domain')).toBeVisible()
  await expect(page.getByRole('radio', { name: 'Set an initial password' })).toHaveCount(0)
  await expect(page.getByLabel('Initial password', { exact: true })).toHaveCount(0)
})

test('an administrator creating an account may choose an initial password', async ({ page }) => {
  await signIn(page, false)

  await page.goto('/accounts/new')

  await expect(page.getByRole('radio', { name: 'Set an initial password' })).toBeVisible()
})
