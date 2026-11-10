import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccountResources } from '../fixtures/stub-account-resources-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubResellerSession } from '../fixtures/stub-reseller-session'
import type { AccountDetail, Plan } from '../../src/types/account'
import type { Page } from '@playwright/test'

/** The plan the account starts on (an administrator's plan). */
const [CURRENT_PLAN] = THREE_PLANS

/** The plan of the reseller the account is handed to: only the reseller's own plans are offered for it. */
const RESELLER_PLAN: Plan = {
  id: '55555555-5555-5555-5555-555555555555',
  displayName: 'Acme Gold',
  diskQuotaMb: 10240,
  maxSites: 10,
}

/** The account under test (#369): owned by the administrator, so it can be handed to a reseller. */
const ACCOUNT: AccountDetail = {
  id: '00000000-0000-0000-0000-0000000000f1',
  name: 'moving',
  primaryDomain: 'moving.example.com',
  planId: CURRENT_PLAN.id,
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-30T09:00:00+00:00',
  resellerId: null,
  resellerName: null,
  planDisplayName: CURRENT_PLAN.displayName,
  ownerEmail: 'owner@moving.example.com',
}

/** What the stubbed endpoints received. */
interface TransferRequests {
  /** Bodies sent to `PUT .../owner`. */
  owner: unknown[]
  /** The `forResellerId` query value of every plan-choices read; `null` when the parameter was absent. */
  planReadsFor: (string | null)[]
}

/**
 * Signs in as the administrator and answers the account's read, the plan choices (the reseller's plan
 * only when its id is named) and the owner move, which is refused with `refusal` when one is given.
 * @param page The Playwright page whose network the routes are installed on.
 * @param refusal The panel's own sentence and code for a refused move, or undefined to accept it.
 * @returns What the endpoints received.
 */
const openAccount = async (
  page: Page,
  refusal?: { code: string; message: string },
): Promise<TransferRequests> => {
  const requests: TransferRequests = { owner: [], planReadsFor: [] }
  let current = ACCOUNT
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubShellReads(page)
  await stubAccountResources(page, { accountId: ACCOUNT.id, resources: [] })
  await stubInvitation(page, ACCOUNT.id, 'pending')
  await stubPlans(page, THREE_PLANS)
  await stubResellers(page)
  // Registered after stubPlans, so it answers first: the plans of the named owner, never the shared list.
  await page.route(/\/api\/v1\/plans\/choices(\?.*)?$/, async (route) => {
    const forReseller = new URL(route.request().url()).searchParams.get('forResellerId')
    requests.planReadsFor.push(forReseller)
    const items = forReseller === ACME_HOSTING.id ? [RESELLER_PLAN] : THREE_PLANS
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items, total: items.length, page: 1, pageSize: 25 }),
    })
  })
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}/owner`, async (route) => {
    const body = route.request().postDataJSON() as { resellerId: string | null; planId: string }
    requests.owner.push(body)
    if (refusal !== undefined) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: refusal.code, title: refusal.message, detail: refusal.message }),
      })
      return
    }
    current = {
      ...current,
      resellerId: body.resellerId,
      resellerName: body.resellerId === null ? null : ACME_HOSTING.name,
      planId: body.planId,
      planDisplayName: RESELLER_PLAN.displayName,
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })
  await page.goto(`/accounts/${ACCOUNT.id}`)
  return requests
}

/**
 * Opens the dialog and chooses the reseller as the new owner.
 * @param page The page showing the account.
 * @returns The dialog.
 */
const chooseReseller = async (page: Page): Promise<ReturnType<Page['getByRole']>> => {
  await page.getByRole('button', { name: 'Change owner', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Change the owner of moving' })
  await dialog.getByRole('combobox', { name: 'New owner', exact: true }).click()
  await page.getByRole('option', { name: ACME_HOSTING.name }).click()
  return dialog
}

test('an administrator hands the account to a reseller with that reseller\'s plan after confirming', async ({ page }) => {
  const requests = await openAccount(page)

  const dialog = await chooseReseller(page)
  await dialog.getByRole('combobox', { name: 'Plan of the new owner', exact: true }).click()
  await page.getByRole('option', { name: RESELLER_PLAN.displayName }).click()
  await dialog.getByRole('button', { name: 'Continue' }).click()

  // Nothing is sent until the question is answered.
  await expect(dialog).toContainText('Move moving from Administrator (no reseller) to acmehosting')
  expect(requests.owner).toEqual([])
  await dialog.getByRole('button', { name: 'Move account' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(requests.owner).toEqual([{ resellerId: ACME_HOSTING.id, planId: RESELLER_PLAN.id }])
  expect(requests.planReadsFor).toContain(ACME_HOSTING.id)
  await expect(page.getByText('The account was moved to its new owner.')).toBeVisible()
  await expect(page.getByText(ACME_HOSTING.name, { exact: true })).toBeVisible()
})

test('continuing without a plan of the new owner is caught in the dialog and sends nothing', async ({ page }) => {
  const requests = await openAccount(page)

  const dialog = await chooseReseller(page)
  await dialog.getByRole('button', { name: 'Continue' }).click()

  await expect(dialog.getByText('Choose a plan of the new owner.')).toBeVisible()
  expect(requests.owner).toEqual([])
})

test('changing the owner clears the plan chosen for the previous one', async ({ page }) => {
  await openAccount(page)

  const dialog = await chooseReseller(page)
  await dialog.getByRole('combobox', { name: 'Plan of the new owner', exact: true }).click()
  await page.getByRole('option', { name: RESELLER_PLAN.displayName }).click()
  await dialog.getByRole('combobox', { name: 'New owner', exact: true }).click()
  await page.getByRole('option', { name: 'Administrator' }).click()
  await dialog.getByRole('button', { name: 'Continue' }).click()

  await expect(dialog.getByText('Choose a plan of the new owner.')).toBeVisible()
})

for (const refusal of [
  {
    code: 'AccountOwnerUnchanged',
    message: 'The account already belongs to this owner. To put it on another plan of the same owner, change its plan instead.',
  },
  { code: 'ResellerAllocationChanged', message: "The reseller's allocation changed while the account was being moved. Try again." },
  { code: 'ResellerAccountLimitReached', message: 'This reseller cannot take another account: its account limit is reached.' },
]) {
  test(`a move refused as ${refusal.code} returns to the form over the panel sentence`, async ({ page, guard }) => {
    // The refusal is the subject: the owner move answers 409 with the panel's own sentence.
    guard.allow(409, new RegExp(`/api/v1/accounts/${ACCOUNT.id}/owner$`))
    await openAccount(page, refusal)

    const dialog = await chooseReseller(page)
    await dialog.getByRole('combobox', { name: 'Plan of the new owner', exact: true }).click()
    await page.getByRole('option', { name: RESELLER_PLAN.displayName }).click()
    await dialog.getByRole('button', { name: 'Continue' }).click()
    await dialog.getByRole('button', { name: 'Move account' }).click()

    await expect(dialog.getByText(refusal.message, { exact: true })).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Continue' })).toBeVisible()
  })
}

test('a reseller is not offered the owner change', async ({ page }) => {
  await stubResellerSession(page)
  await stubAccountResources(page, { accountId: ACCOUNT.id, resources: [] })
  await stubPlans(page, THREE_PLANS)
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
  await page.goto(`/accounts/${ACCOUNT.id}`)

  await expect(page.getByRole('button', { name: 'Change plan' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Change owner', exact: true })).toHaveCount(0)
})
