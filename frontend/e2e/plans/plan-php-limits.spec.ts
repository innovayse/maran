import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { MANAGED_PLANS, stubManagedPlans, type PlanRequests } from '../fixtures/stub-plans-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

/** Catalogue every spec here starts from: the accounts module licensed, which is what owns plans. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** Business has nobody on it and states 256 MiB, 64 MiB, 120 s and 3000 variables. */
const BUSINESS = MANAGED_PLANS[1]

/** The four PHP fields' labels, in the order the form draws them. */
const LABELS = {
  memory: 'PHP memory limit, at most (MB)',
  upload: 'PHP upload size, at most (MB)',
  execution: 'PHP execution time, at most (seconds)',
  variables: 'PHP input variables, at most',
}

/**
 * Installs the shell and the plan endpoints, then opens a path.
 * @param page The Playwright page under test.
 * @param path Where to go.
 * @param refusal A sentence the stubbed server refuses every update with, or `undefined`.
 * @returns What the stubbed endpoints received.
 */
const open = async (page: Page, path: string, refusal?: string): Promise<PlanRequests> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubShellReads(page)
  const requests = await stubManagedPlans(page, undefined, refusal === undefined ? {} : { update: refusal })
  await page.goto(path)
  return requests
}

test('editing a plan starts from its PHP bounds and puts them back changed', async ({ page }) => {
  const requests = await open(page, `/plans/${BUSINESS.id}`)

  await expect(page.getByRole('heading', { level: 2, name: 'PHP limits' })).toBeVisible()
  await expect(page.getByLabel(LABELS.memory)).toHaveValue('256')
  await expect(page.getByLabel(LABELS.upload)).toHaveValue('64')
  await expect(page.getByLabel(LABELS.execution)).toHaveValue('120')
  await expect(page.getByLabel(LABELS.variables)).toHaveValue('3000')

  await page.getByLabel(LABELS.memory).fill('512')
  await page.getByLabel(LABELS.execution).fill('60')
  await page.getByRole('button', { name: 'Save plan' }).click()

  await expect(page).toHaveURL('/plans')
  expect(requests.updated).toHaveLength(1)
  expect(requests.updated[0]?.body.phpLimits).toEqual({
    memoryLimitMaxMb: 512,
    uploadMaxMb: 64,
    maxExecutionSeconds: 60,
    maxInputVars: 3000,
  })
})

test('a PHP bound that is zero or not a number is caught and nothing is sent', async ({ page }) => {
  const requests = await open(page, `/plans/${BUSINESS.id}`)

  await page.getByLabel(LABELS.memory).fill('0')
  await page.getByLabel(LABELS.upload).fill('big')
  await page.getByRole('button', { name: 'Save plan' }).click()

  await expect(page.getByText('Enter a whole number of at least 1.')).toBeVisible()
  await expect(page.getByText('Enter a whole number.')).toBeVisible()
  await expect(page.getByLabel(LABELS.memory)).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByLabel(LABELS.upload)).toHaveAttribute('aria-invalid', 'true')
  expect(requests.updated).toEqual([])
})

test('a ceiling refusal from the server is rendered verbatim', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/plans\/[0-9a-f-]{36}$/)
  const detail = 'The PHP memory limit may be at most 512 MB.'
  await open(page, `/plans/${BUSINESS.id}`, detail)

  await page.getByLabel(LABELS.memory).fill('9999')
  await page.getByRole('button', { name: 'Save plan' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page).toHaveURL(`/plans/${BUSINESS.id}`)
  await expect(page.getByLabel(LABELS.memory)).toHaveValue('9999')
})

test('a new plan sends the PHP bounds as typed', async ({ page }) => {
  const requests = await open(page, '/plans/new')

  await page.getByLabel('Plan name').fill('Agency')
  await page.getByLabel('Disk quota (MB)').fill('10240')
  await page.getByLabel('Sites', { exact: true }).fill('10')
  await page.getByRole('textbox', { name: 'Databases', exact: true }).fill('5')
  await page.getByLabel('SFTP logins').fill('4')
  await page.getByLabel('FTPS logins').fill('0')
  await page.getByLabel('Cron entries').fill('7')
  await page.getByLabel('PHP workers per pool').fill('8')
  await page.getByLabel('CPU (% of one core)').fill('150')
  await page.getByLabel('Memory (MB)', { exact: true }).fill('1024')
  await page.getByLabel('Processes', { exact: true }).fill('64')
  await page.getByLabel(LABELS.memory).fill('128')
  await page.getByLabel(LABELS.upload).fill('32')
  await page.getByLabel(LABELS.execution).fill('45')
  await page.getByLabel(LABELS.variables).fill('2000')
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page).toHaveURL('/plans')
  expect(requests.created).toHaveLength(1)
  expect(requests.created[0]?.phpLimits).toEqual({
    memoryLimitMaxMb: 128,
    uploadMaxMb: 32,
    maxExecutionSeconds: 45,
    maxInputVars: 2000,
  })
})
