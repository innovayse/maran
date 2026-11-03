import { expect, test, type Page } from '@playwright/test'
import {
  stubAccountMemberships,
  stubRefreshFollowsActiveAccount,
  type MembershipsState,
} from '../fixtures/stub-account-memberships-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { OWNER_PROFILE, stubProfile, type ProfileStub } from '../fixtures/stub-profile-route'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #328: the customer's menu follows what the account's owner granted. A member is offered
// what they hold and nothing of what they were not given, and never the Sub-users page, which only
// the owner manages; the endpoints refuse regardless, so the menu is advice and errs permissive: a
// profile that cannot be read offers everything. Navigation is by clicks only.

const OLIVE_ACCOUNT = '11111111-1111-1111-1111-111111111111'
const SHOP_ACCOUNT = '22222222-2222-2222-2222-222222222222'

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OLIVE_ACCOUNT,
}

/** The caller's own account; the plan lists no features, so only the grants shape the menu. */
const OWN_ACCOUNT: MyAccount = {
  id: OLIVE_ACCOUNT,
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
  },
}

/** A member holding the databases feature and nothing else. */
const DATABASES_MEMBER: ProfileStub = {
  ...OWNER_PROFILE,
  accountAccess: { role: 'member', roleDisplayName: 'Member', features: ['databases'] },
}

/**
 * Signs in as the customer on a desktop and opens the panel; the profile answers what a spec names.
 * @param page The Playwright page under test.
 * @param profile The profile the panel reports, or a function building it on every read.
 * @returns Resolves once the panel's entry page was requested.
 */
const signIn = async (page: Page, profile: ProfileStub | (() => ProfileStub)): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubProfile(page, profile)
  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
}

/**
 * The names of the top-level rows, links and disabled rows alike, in document order.
 * @param page The Playwright page under test.
 * @returns Each row's accessible name.
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

test('a member holding only databases is offered Databases and not Backups, Mail or DNS', async ({ page }) => {
  await signIn(page, DATABASES_MEMBER)

  await expect
    .poll(async (): Promise<string[]> => {
      return topLevelNames(page)
    })
    .toEqual(['System status', 'Sites and domains', 'Databases', 'Files and access', 'Activity', 'My account'])

  // Files and access keeps only the page that is not built yet: the file transfer logins and the
  // scheduled tasks the member was not granted are gone, so the row is the disabled one.
  await expect(mainNavigation(page).getByRole('link', { name: 'Files and access Coming soon' })).toHaveAttribute(
    'aria-disabled',
    'true',
  )
  await expect(mainNavigation(page).getByRole('link', { name: 'Backups', exact: true })).toHaveCount(0)
})

test('a member is not offered the Sub-users page, which only the owner manages', async ({ page }) => {
  await signIn(page, DATABASES_MEMBER)
  await openSection(page, 'My account')

  // Positive control: the section is open and its other pages are there.
  await expect(mainNavigation(page).getByRole('link', { name: 'Signed-in devices', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: 'Sub-users', exact: true })).toHaveCount(0)
})

test('the owner is offered every item, Sub-users and scheduled tasks among them', async ({ page }) => {
  await signIn(page, OWNER_PROFILE)

  await expect
    .poll(async (): Promise<string[]> => {
      return topLevelNames(page)
    })
    .toEqual([
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
  await openSection(page, 'Files and access')
  await expect(mainNavigation(page).getByRole('link', { name: 'Scheduled tasks', exact: true })).toBeVisible()
  await openSection(page, 'My account')
  await expect(mainNavigation(page).getByRole('link', { name: 'Sub-users', exact: true })).toBeVisible()
})

test('a profile the panel could not read offers everything rather than hiding the menu', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await page.route(/\/api\/v1\/profile(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Service unavailable', status: 503 }),
    })
  })
  await page.goto('/')

  await expect(mainNavigation(page).getByRole('button', { name: 'Show the Backups pages' })).toBeVisible()
  await openSection(page, 'My account')
  await expect(mainNavigation(page).getByRole('link', { name: 'Sub-users', exact: true })).toBeVisible()
})

test('switching the active account reads the standing again and the menu follows it', async ({ page }) => {
  const state: MembershipsState = {
    accounts: [
      { accountId: OLIVE_ACCOUNT, name: 'olive' },
      { accountId: SHOP_ACCOUNT, name: 'shop' },
    ],
    activeAccountId: OLIVE_ACCOUNT,
    switchRequests: [],
  }
  let reads = 0
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubRefreshFollowsActiveAccount(page, CUSTOMER, state)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubAccountMemberships(page, state)
  // The person owns olive and is a databases-only member of shop: the answer follows the active account.
  await stubProfile(page, (): ProfileStub => {
    reads += 1
    return state.activeAccountId === OLIVE_ACCOUNT ? OWNER_PROFILE : DATABASES_MEMBER
  })
  await page.goto('/')
  await expect(mainNavigation(page).getByRole('button', { name: 'Show the Backups pages' })).toBeVisible()
  const readsBeforeSwitch = reads

  await page.getByRole('button', { name: 'Current account: olive. Switch account' }).click()
  await page.getByRole('menuitemradio', { name: 'shop' }).click()

  await expect(mainNavigation(page).getByRole('button', { name: 'Show the Databases pages' })).toBeVisible()
  await expect(mainNavigation(page).getByRole('button', { name: 'Show the Backups pages' })).toHaveCount(0)
  expect(reads).toBeGreaterThan(readsBeforeSwitch)
})
