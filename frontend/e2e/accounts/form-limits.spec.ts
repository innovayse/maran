import { expect, test, type Page } from '@playwright/test'
import { stubCreateRefused, recordCreateRequests, submitCreate } from '../fixtures/stub-account-create'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubPlans, THE_PLAN } from '../fixtures/stub-plans-route'
import { stubResellerSession } from '../fixtures/stub-reseller-session'
import type { Plan } from '../../src/types/account'

// Issue #194: a new account can carry its own process, inode and open-files limits. An empty field
// follows the plan, a figure is sent, a process limit of zero is refused, and a reseller can only
// lower a limit below its plan.

/** The plan the specs choose: it caps processes at 50, so "Plan: 50" is the process field's default. */
const LIMITED_PLAN: Plan = { ...THE_PLAN, maxProcesses: { kind: 'limited', maximum: 50 } }

/**
 * Opens the create form with a plan that states a process ceiling, as an administrator or a reseller.
 * @param page The Playwright page.
 * @param asReseller Whether to sign in as a reseller instead of the administrator.
 * @returns Resolves once the form is on screen.
 */
const openForm = async (page: Page, asReseller: boolean): Promise<void> => {
  if (asReseller) {
    await stubResellerSession(page)
  } else {
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  }
  await stubAccounts(page, [])
  await stubPlans(page, [LIMITED_PLAN])
  // The create is refused so the page stays and the sent body can be read.
  await stubCreateRefused(page)
  await page.goto('/accounts/new')
  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()
  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: THE_PLAN.displayName, exact: false }).click()
  await page.getByLabel('Owner email').fill('owner@alpha.example.com')
}

test('an untouched limits section adds nothing to the body and shows the plan figure', async ({ page }) => {
  await openForm(page, false)

  await expect(page.getByLabel('Process limit')).toHaveAttribute('placeholder', 'Plan: 50')
  const request = await submitCreate(page)

  const body = request.postDataJSON() as Record<string, unknown>
  expect(body).not.toHaveProperty('processLimit')
  expect(body).not.toHaveProperty('inodeLimit')
  expect(body).not.toHaveProperty('openFilesLimit')
})

test('typed limits are sent as whole numbers', async ({ page }) => {
  await openForm(page, false)
  await page.getByLabel('Process limit').fill('20')
  await page.getByLabel('Inode limit').fill('100000')
  await page.getByLabel('Open files limit').fill('4096')

  const request = await submitCreate(page)

  expect(request.postDataJSON()).toMatchObject({ processLimit: 20, inodeLimit: 100000, openFilesLimit: 4096 })
})

test('a process limit of zero is refused on the form and nothing is sent', async ({ page }) => {
  await openForm(page, false)
  const createRequests = recordCreateRequests(page)

  await page.getByLabel('Process limit').fill('0')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByText('The limit must be at least 1.')).toBeVisible()
  await expect(page.getByLabel('Process limit')).toHaveAttribute('aria-invalid', 'true')
  expect(createRequests).toEqual([])
})

test('zero is accepted for inodes and open files, where it means no limit', async ({ page }) => {
  await openForm(page, false)
  await page.getByLabel('Inode limit').fill('0')
  await page.getByLabel('Open files limit').fill('0')

  const request = await submitCreate(page)

  expect(request.postDataJSON()).toMatchObject({ inodeLimit: 0, openFilesLimit: 0 })
})

test('text that is not a whole number is refused', async ({ page }) => {
  await openForm(page, false)
  await page.getByLabel('Inode limit').fill('12.5')

  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByText('Enter a whole number, or leave the field empty.')).toBeVisible()
})

test('an administrator may set a process limit above the plan', async ({ page }) => {
  await openForm(page, false)
  await page.getByLabel('Process limit').fill('500')

  const request = await submitCreate(page)

  expect(request.postDataJSON()).toMatchObject({ processLimit: 500 })
})

test('a reseller is told it can only lower a limit, and a figure above the plan is refused', async ({ page }) => {
  await openForm(page, true)
  const createRequests = recordCreateRequests(page)
  await expect(page.getByText('You can only lower a limit below your plan', { exact: false })).toBeVisible()

  await page.getByLabel('Process limit').fill('51')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByText('A limit can only be lowered below the plan, which allows 50.')).toBeVisible()
  expect(createRequests).toEqual([])
})

test('a reseller lowering the process limit below the plan is sent', async ({ page }) => {
  await openForm(page, true)
  await page.getByLabel('Process limit').fill('50')

  const request = await submitCreate(page)

  expect(request.postDataJSON()).toMatchObject({ processLimit: 50 })
})
