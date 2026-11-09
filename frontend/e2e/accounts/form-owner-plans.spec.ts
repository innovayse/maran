import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import type { Plan } from '../../src/types/account'

// Issue #437: on the create form an administrator who chooses a reseller as owner is offered that
// reseller's plans, and the plan chosen for the previous owner is dropped.

/** The administrator's own plan, chosen first in the specs that then change the owner. */
const [ADMIN_PLAN] = THREE_PLANS

/** The only plan the stubbed reseller sells. */
const RESELLER_PLAN: Plan = {
  id: '55555555-5555-5555-5555-555555555555',
  displayName: 'Acme Gold',
  diskQuotaMb: 10240,
  maxSites: 10,
}

/**
 * Signs in as the administrator, opens the create form and records every plan-choices read.
 * @param page The Playwright page.
 * @returns The `forResellerId` query value of each plan-choices read, `null` when it was absent.
 */
const openForm = async (page: Page): Promise<(string | null)[]> => {
  const planReadsFor: (string | null)[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubAccounts(page, [])
  await stubPlans(page, THREE_PLANS)
  await stubResellers(page)
  // Registered after stubPlans, so it answers first: the plans of the named owner, never the shared list.
  await page.route(/\/api\/v1\/plans\/choices(\?.*)?$/, async (route) => {
    const forReseller = new URL(route.request().url()).searchParams.get('forResellerId')
    planReadsFor.push(forReseller)
    const items = forReseller === ACME_HOSTING.id ? [RESELLER_PLAN] : THREE_PLANS
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items, total: items.length, page: 1, pageSize: 25 }),
    })
  })
  await page.goto('/accounts/new')
  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()
  return planReadsFor
}

/**
 * Chooses the stubbed reseller in the owner picker.
 * @param page The Playwright page.
 * @returns Resolves once the reseller is chosen.
 */
const chooseReseller = async (page: Page): Promise<void> => {
  await page.getByRole('combobox', { name: 'Owner (reseller)', exact: true }).click()
  await page.getByRole('option', { name: ACME_HOSTING.name, exact: false }).click()
}

test('choosing a reseller as owner offers that reseller plans and not the administrator ones', async ({ page }) => {
  const planReadsFor = await openForm(page)

  await chooseReseller(page)
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()

  await expect(page.getByRole('option', { name: RESELLER_PLAN.displayName, exact: false })).toBeVisible()
  await expect(page.getByRole('option', { name: ADMIN_PLAN.displayName, exact: false })).toHaveCount(0)
  expect(planReadsFor).toContain(ACME_HOSTING.id)
})

test('the administrator own plans are offered while no reseller is chosen', async ({ page }) => {
  const planReadsFor = await openForm(page)

  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()

  await expect(page.getByRole('option', { name: ADMIN_PLAN.displayName, exact: false })).toBeVisible()
  expect(planReadsFor.length).toBeGreaterThan(0)
  expect(planReadsFor.every((forReseller) => {return forReseller === null})).toBe(true)
})

test('changing the owner clears the plan chosen for the previous one', async ({ page }) => {
  await openForm(page)
  const plan = page.getByRole('combobox', { name: 'Plan', exact: true })
  await plan.click()
  await page.getByRole('option', { name: ADMIN_PLAN.displayName, exact: false }).click()
  await expect(plan).toContainText(ADMIN_PLAN.displayName)

  await chooseReseller(page)

  await expect(plan).not.toContainText(ADMIN_PLAN.displayName)
})
