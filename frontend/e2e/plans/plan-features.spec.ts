import { expect, test, type Page } from '@playwright/test'
import { FULL_CATALOGUE, mainNavigation } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { MANAGED_PLANS, PLAN_FEATURE_CHOICES, stubManagedPlans, type PlanRequests } from '../fixtures/stub-plans-route'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import type { AccountFeatureName } from '../../src/types/accountFeature'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #316: a plan states which features it sells. The plan form offers the panel's own list as
// checkboxes, and the customer's menu follows the plan. Navigation is by clicks only: each spec
// enters once, at the page a signed-in person would open, and clicks from there.

/** Starter sells everything but mail and DNS, so both boxes start clear on its edit form. */
const STARTER = MANAGED_PLANS[0]

/** The feature names the panel lists, in its order. */
const EVERY_FEATURE: AccountFeatureName[] = PLAN_FEATURE_CHOICES.map((choice) => {
  return choice.feature
})

/** The customer every menu spec signs in as. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/**
 * The customer's own account on a plan that sells exactly the given features.
 * @param features The feature names the plan includes.
 * @returns The account the panel would report for the customer.
 */
const accountOn = (features: AccountFeatureName[]): MyAccount => {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'olive',
    primaryDomain: 'olive.example.com',
    status: 'active',
    statusName: 'Active',
    plan: {
      displayName: 'Starter',
      diskQuotaMb: 1024,
      maxSites: 5,
      maxDatabases: 5,
      maxSftpUsers: 5,
      maxFtpUsers: 5,
      maxCronEntries: 5,
      features,
    },
  }
}

/**
 * Signs the administrator in, serves the plan endpoints and opens the plans list.
 * @param page The Playwright page under test.
 * @returns What the stubbed plan endpoints received.
 */
const openPlans = async (page: Page): Promise<PlanRequests> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  const requests = await stubManagedPlans(page)
  await page.goto('/plans')
  return requests
}

/**
 * Signs the customer in on a plan with the given features and opens the dashboard.
 * @param page The Playwright page under test.
 * @param features The feature names the customer's plan includes.
 * @returns Resolves once the page has been opened.
 */
const openAsCustomer = async (page: Page, features: AccountFeatureName[]): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubMyAccount(page, accountOn(features))
  await page.goto('/')
}

/**
 * The names of the top-level menu items, in drawing order.
 * @param page The Playwright page under test.
 * @returns The labels of the items directly under the main navigation.
 */
const topLevelNames = async (page: Page): Promise<string[]> => {
  return mainNavigation(page)
    .locator('ul[aria-labelledby] > li')
    .evaluateAll((rows): string[] => {
      return rows.map((row): string => {
        return row.querySelector('a, [role="link"]')?.firstElementChild?.textContent?.trim() ?? ''
      })
    })
}

test('the plan form offers the panel own features and a new plan starts with all of them ticked', async ({ page }) => {
  await openPlans(page)
  await page.getByRole('link', { name: 'New plan' }).last().click()

  const group = page.getByRole('group', { name: 'Features' })
  await expect(group.getByRole('checkbox')).toHaveCount(PLAN_FEATURE_CHOICES.length)
  for (const choice of PLAN_FEATURE_CHOICES) {
    await expect(group.getByRole('checkbox', { name: choice.featureDisplayName })).toBeChecked()
  }
})

test('unticking a feature leaves it out of the plan that is created', async ({ page }) => {
  const requests = await openPlans(page)
  await page.getByRole('link', { name: 'New plan' }).last().click()

  await page.getByLabel('Plan name').fill('No mail')
  await page.getByLabel('Disk quota (MB)').fill('10240')
  await page.getByLabel('Sites', { exact: true }).fill('10')
  await page.getByRole('textbox', { name: 'Databases', exact: true }).fill('5')
  await page.getByLabel('SFTP logins').fill('4')
  await page.getByLabel('FTPS logins').fill('0')
  await page.getByLabel('Cron entries').fill('7')
  await page.getByLabel('PHP workers per pool').fill('8')
  await page.getByLabel('CPU (% of one core)').fill('150')
  await page.getByRole('checkbox', { name: 'No limit' }).nth(1).check({ force: true })
  await page.getByLabel('Processes', { exact: true }).fill('64')
  await page.getByRole('group', { name: 'Features' }).getByRole('checkbox', { name: 'Mail' }).uncheck({ force: true })
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page).toHaveURL('/plans')
  expect(requests.created).toHaveLength(1)
  expect(requests.created[0]?.features).toEqual(EVERY_FEATURE.filter((feature) => {
    return feature !== 'mail'
  }))
})

test('editing a plan shows the features it sells and saves the set that is ticked', async ({ page }) => {
  const requests = await openPlans(page)
  await page.getByRole('link', { name: 'Edit Starter' }).click()

  const group = page.getByRole('group', { name: 'Features' })
  await expect(group.getByRole('checkbox', { name: 'Databases' })).toBeChecked()
  await expect(group.getByRole('checkbox', { name: 'Mail' })).not.toBeChecked()
  await expect(group.getByRole('checkbox', { name: 'DNS' })).not.toBeChecked()

  await group.getByRole('checkbox', { name: 'Mail' }).check({ force: true })
  await group.getByRole('checkbox', { name: 'Backups' }).uncheck({ force: true })
  await page.getByRole('button', { name: 'Save plan' }).click()

  await expect(page).toHaveURL('/plans')
  expect(requests.updated).toHaveLength(1)
  expect(requests.updated[0]?.id).toBe(STARTER.id)
  // The panel's order, however the boxes were clicked.
  expect(requests.updated[0]?.body.features).toEqual(['mail', 'databases', 'cron', 'fileTransfer'])
})

test('a plan without mail, DNS and backups hides those items from its customer menu', async ({ page }) => {
  await openAsCustomer(page, ['databases', 'cron', 'fileTransfer'])

  await expect.poll(async (): Promise<string[]> => {
    return topLevelNames(page)
  }).toEqual(['System status', 'Sites and domains', 'Databases', 'Files and access', 'Activity', 'My account'])
})

test('a plan that includes every feature keeps the whole customer menu', async ({ page }) => {
  // The inverse control: the filter above hides because of the plan, not for every customer.
  await openAsCustomer(page, EVERY_FEATURE)

  await expect.poll(async (): Promise<string[]> => {
    return topLevelNames(page)
  }).toEqual([
    'System status',
    'Sites and domains',
    'DNS',
    'Mail',
    'Databases',
    'Files and access',
    'Backups',
    'Activity',
    'My account',
  ])
})
