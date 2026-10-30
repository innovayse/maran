import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import type { Account, AccountDetail } from '../../src/types/account'

/** The plan the account starts on, and the one it is moved to. */
const [CURRENT_PLAN, OTHER_PLAN] = THREE_PLANS

/** The account under test (#216): its page shows its plan and its owner's address. */
const ACCOUNT: AccountDetail = {
  id: '00000000-0000-0000-0000-0000000000e1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: CURRENT_PLAN.id,
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
  planDisplayName: CURRENT_PLAN.displayName,
  ownerEmail: 'owner@acme.example.com',
}

/** What the stubbed edit endpoints received. */
interface EditRequests {
  /** Bodies sent to `PUT .../plan`. */
  plan: unknown[]
  /** Bodies sent to `PUT .../owner-email`. */
  ownerEmail: unknown[]
}

/**
 * Signs in, licenses the accounts module, and answers the account's read and its two edits. An edit
 * is refused with `refusal` when one is given, and otherwise answered with the account as changed.
 * @param page The Playwright page whose network the routes are installed on.
 * @param account The account the panel reports.
 * @param refusal The panel's own sentence for a refused edit, or undefined to accept every edit.
 * @returns What the edit endpoints received.
 */
const openAccount = async (page: Page, account: AccountDetail, refusal?: string): Promise<EditRequests> => {
  const requests: EditRequests = { plan: [], ownerEmail: [] }
  let current = account
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubInvitation(page, account.id, 'pending')
  await stubPlans(page, THREE_PLANS)
  await page.route(`**/api/v1/accounts/${account.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })
  await page.route(`**/api/v1/accounts/${account.id}/{plan,owner-email}`, async (route) => {
    const body = route.request().postDataJSON() as { planId?: string; ownerEmail?: string }
    const field = route.request().url().endsWith('/plan') ? 'plan' : 'ownerEmail'
    requests[field].push(body)
    if (refusal !== undefined) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'AccountPlanQuotaBelowUsage', title: refusal, detail: refusal }),
      })
      return
    }
    const plan = THREE_PLANS.find((candidate) => {
      return candidate.id === body.planId
    })
    current =
      field === 'plan' && plan !== undefined
        ? { ...current, planId: plan.id, planDisplayName: plan.displayName }
        : { ...current, ownerEmail: body.ownerEmail ?? current.ownerEmail }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })
  await page.goto(`/accounts/${account.id}`)
  return requests
}

test('the account page shows its plan and its owner email', async ({ page }) => {
  await openAccount(page, ACCOUNT)

  await expect(page.getByText(CURRENT_PLAN.displayName, { exact: true })).toBeVisible()
  await expect(page.getByText('owner@acme.example.com', { exact: true })).toBeVisible()
})

test('changing the plan sends the chosen plan and shows the account on it', async ({ page }) => {
  const requests = await openAccount(page, ACCOUNT)

  await page.getByRole('button', { name: 'Change plan' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change the plan of acme' })
  await dialog.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: OTHER_PLAN.displayName, exact: false }).click()
  await dialog.getByRole('button', { name: 'Change plan' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(requests.plan).toEqual([{ planId: OTHER_PLAN.id }])
  await expect(page.getByText('The plan was changed.')).toBeVisible()
  await expect(page.getByText(OTHER_PLAN.displayName, { exact: true })).toBeVisible()
})

test('keeping the same plan is caught in the dialog and sends nothing', async ({ page }) => {
  const requests = await openAccount(page, ACCOUNT)

  await page.getByRole('button', { name: 'Change plan' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change the plan of acme' })
  await dialog.getByRole('button', { name: 'Change plan' }).click()

  await expect(dialog.getByText('The account is already on this plan.')).toBeVisible()
  expect(requests.plan).toEqual([])
})

test('a plan change the panel refuses keeps the dialog open over the panel sentence', async ({ page }) => {
  const refusal =
    "The plan was not changed: this account already uses more disk than the new plan's quota allows."
  await openAccount(page, ACCOUNT, refusal)

  await page.getByRole('button', { name: 'Change plan' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change the plan of acme' })
  await dialog.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: OTHER_PLAN.displayName, exact: false }).click()
  await dialog.getByRole('button', { name: 'Change plan' }).click()

  await expect(dialog.getByText(refusal, { exact: true })).toBeVisible()
  await expect(page.getByText(CURRENT_PLAN.displayName, { exact: true })).toBeVisible()
})

test('changing the owner email sends the new address and shows it', async ({ page }) => {
  const requests = await openAccount(page, ACCOUNT)

  await page.getByRole('button', { name: 'Change owner email' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change the owner email of acme' })
  await expect(dialog).toContainText('its invitation and every password reset go here')
  await dialog.getByLabel('Owner email').fill('new-owner@acme.example.com')
  await dialog.getByRole('button', { name: 'Change email' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(requests.ownerEmail).toEqual([{ ownerEmail: 'new-owner@acme.example.com' }])
  await expect(page.getByText('new-owner@acme.example.com', { exact: true })).toBeVisible()
})

test('a malformed owner email is caught in the dialog and sends nothing', async ({ page }) => {
  const requests = await openAccount(page, ACCOUNT)

  await page.getByRole('button', { name: 'Change owner email' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change the owner email of acme' })
  await dialog.getByLabel('Owner email').fill('not an address')
  await dialog.getByRole('button', { name: 'Change email' }).click()

  await expect(dialog).toBeVisible()
  expect(requests.ownerEmail).toEqual([])
})

test('a suspended account offers no invitation action and says why', async ({ page }) => {
  await openAccount(page, { ...ACCOUNT, status: 'suspended' })

  const card = page.getByTestId('invitation-card')
  await expect(card.getByTestId('invitation-suspended')).toHaveText(
    'No invitation can be sent or linked while this account is suspended. Reactivate the account first.',
  )
  await expect(card.getByRole('button', { name: 'Create invitation link' })).toHaveCount(0)
  await expect(card.getByRole('button', { name: 'Resend invitation' })).toHaveCount(0)
})

test('an active account still offers both invitation actions', async ({ page }) => {
  await openAccount(page, ACCOUNT)

  const card = page.getByTestId('invitation-card')
  await expect(card.getByRole('button', { name: 'Create invitation link' })).toBeVisible()
  await expect(card.getByRole('button', { name: 'Resend invitation' })).toBeVisible()
  await expect(card.getByTestId('invitation-suspended')).toHaveCount(0)
})

test('the accounts list narrowed to a plan shows only the accounts on it, and can be widened', async ({ page }) => {
  const onPlan: Account = { ...ACCOUNT, id: '00000000-0000-0000-0000-0000000000e2', name: 'onplan' }
  const offPlan: Account = {
    ...ACCOUNT,
    id: '00000000-0000-0000-0000-0000000000e3',
    name: 'offplan',
    planId: OTHER_PLAN.id,
  }
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubPlans(page, THREE_PLANS)
  await stubAccounts(page, [onPlan, offPlan])

  await page.goto(`/accounts?plan=${CURRENT_PLAN.id}`)

  await expect(page.getByTestId('accounts-plan-filter')).toContainText(CURRENT_PLAN.displayName)
  await expect(page.getByRole('link', { name: 'onplan' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'offplan' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Show all accounts' }).click()
  await expect(page.getByRole('link', { name: 'offplan' })).toBeVisible()
})
