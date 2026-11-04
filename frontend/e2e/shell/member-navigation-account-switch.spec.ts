import { expect, test, type Locator, type Page } from '@playwright/test'
import { holdRoute } from '../fixtures/hold-route'
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

// Issue #366: a person who is a member of several accounts switches the active one. The switch
// reloads the panel (ShellAccountSelector), so the page has no earlier grants to keep: between the
// reload and the answer to the NEW account's access read the sidebar shows its loading skeleton, never the full
// permissive menu that the answer would then shrink. Only a read that FAILS offers everything. Every wait is
// ordered by the test (a held request, a polled assertion), never by a sleep. Navigation is by
// clicks only.

const OLIVE_ACCOUNT = '11111111-1111-1111-1111-111111111111'
const SHOP_ACCOUNT = '22222222-2222-2222-2222-222222222222'

/** The profile endpoint, whose `accountAccess` is the standing the menu follows. */
const PROFILE_PATTERN = /\/api\/v1\/profile(\?.*)?$/

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

/** What the member holds on the first account: databases and backups. */
const OLIVE_MEMBER: ProfileStub = {
  ...OWNER_PROFILE,
  accountAccess: { role: 'member', roleDisplayName: 'Member', features: ['databases', 'backups'] },
}

/** What the member holds on the second account: backups and mail, and no databases. */
const SHOP_MEMBER: ProfileStub = {
  ...OWNER_PROFILE,
  accountAccess: { role: 'member', roleDisplayName: 'Member', features: ['backups', 'mail'] },
}

/** The top-level rows of the menu of {@link OLIVE_MEMBER}. */
const OLIVE_MENU = [
  'System status',
  'Sites and domains',
  'Databases',
  'Files and access',
  'Backups',
  'Activity',
  'My account',
]

/** The top-level rows of the menu of {@link SHOP_MEMBER}. */
const SHOP_MENU = [
  'System status',
  'Sites and domains',
  'Mail',
  'Files and access',
  'Backups',
  'Activity',
  'My account',
]

/** The top-level rows of the menu that offers everything (the permissive default). */
const EVERYTHING_MENU = [
  'System status',
  'Sites and domains',
  'DNS',
  'Mail',
  'Databases',
  'Files and access',
  'Backups',
  'Activity',
  'My account',
]

/** A desktop wide enough for the sidebar and its collapsed rail. */
const DESKTOP = { width: 1440, height: 900 }

/** A phone, below the breakpoint at which the shell draws a bottom tab bar instead of a sidebar. */
const PHONE = { width: 390, height: 844 }

/**
 * Stubs a panel the member holds two accounts in, the first active, at the given viewport; the
 * profile answers with the grants of whichever account is active. Nothing is opened yet, so a
 * caller can still hold a read before the first load.
 * @param page The Playwright page under test.
 * @param viewport The viewport to open the panel at.
 * @returns The memberships the stubbed panel holds, mutated by the switch.
 */
const stubTwoAccountPanel = async (
  page: Page,
  viewport: { width: number; height: number },
): Promise<MembershipsState> => {
  const state: MembershipsState = {
    accounts: [
      { accountId: OLIVE_ACCOUNT, name: 'olive' },
      { accountId: SHOP_ACCOUNT, name: 'shop' },
    ],
    activeAccountId: OLIVE_ACCOUNT,
    switchRequests: [],
  }
  await page.setViewportSize(viewport)
  await stubSignedIn(page, CUSTOMER)
  await stubRefreshFollowsActiveAccount(page, CUSTOMER, state)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  // The account read answers for whichever account is active, as the panel does.
  await stubMyAccount(page, (): MyAccount => {
    return { ...OWN_ACCOUNT, id: state.activeAccountId }
  })
  await stubAccountMemberships(page, state)
  await stubProfile(page, (): ProfileStub => {
    return state.activeAccountId === OLIVE_ACCOUNT ? OLIVE_MEMBER : SHOP_MEMBER
  })
  return state
}

/**
 * Signs the member in on a desktop with two accounts, the first active, and opens the panel.
 * @param page The Playwright page under test.
 * @returns The memberships the stubbed panel holds, mutated by the switch.
 */
const signInHoldingTwoAccounts = async (page: Page): Promise<MembershipsState> => {
  const state = await stubTwoAccountPanel(page, DESKTOP)
  await page.goto('/')
  await expect(mainNavigation(page).getByRole('button', { name: 'Show the Backups pages' })).toBeVisible()
  return state
}

/**
 * The bottom tab bar of a phone, a navigation landmark of its own.
 * @param page The Playwright page under test.
 * @returns The tab bar's locator.
 */
const tabBar = (page: Page): Locator => {
  return page.getByRole('navigation', { name: 'Sections' })
}

/**
 * Switches the active account to the shop through the account selector, by clicks.
 * @param page The Playwright page under test.
 * @returns Resolves once the shop was chosen in the menu.
 */
const switchToShop = async (page: Page): Promise<void> => {
  await page.getByRole('button', { name: 'Current account: olive. Switch account' }).click()
  await page.getByRole('menuitemradio', { name: 'shop' }).click()
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

test('a member sees no permissive menu while the new account access read is still pending', async ({ page }) => {
  await signInHoldingTwoAccounts(page)
  await expect
    .poll(async (): Promise<string[]> => {
      return topLevelNames(page)
    })
    .toEqual(OLIVE_MENU)

  // Installed after the first read was answered and after the stub, so only the read the switch
  // causes is held and then handed on to the stub.
  const held = await holdRoute(page, PROFILE_PATTERN)
  await switchToShop(page)
  await held.requested

  // The new account's standing is unknown here and the reloaded page holds no earlier grants: the
  // sidebar shows its loading skeleton. The landmark is busy and holds no menu row at all, in
  // particular neither Mail nor DNS, which only the shop's menu or the permissive default offers,
  // and none of the old account's rows.
  const navigation = mainNavigation(page)
  await expect(navigation).toHaveAttribute('aria-busy', 'true')
  await expect(navigation.getByRole('link')).toHaveCount(0)
  await expect(navigation.getByRole('button')).toHaveCount(0)
  expect(await topLevelNames(page)).toEqual([])
  await expect(navigation.getByText('Mail', { exact: true })).toHaveCount(0)
  await expect(navigation.getByText('DNS', { exact: true })).toHaveCount(0)

  held.release()

  await expect
    .poll(async (): Promise<string[]> => {
      return topLevelNames(page)
    })
    .toEqual(SHOP_MENU)
  await expect(navigation).not.toHaveAttribute('aria-busy', 'true')
  await expect(mainNavigation(page).getByRole('button', { name: 'Show the Databases pages' })).toHaveCount(0)
})

test('a member is offered everything when the new account access read fails', async ({ page }) => {
  await signInHoldingTwoAccounts(page)
  await expect
    .poll(async (): Promise<string[]> => {
      return topLevelNames(page)
    })
    .toEqual(OLIVE_MENU)

  let refusedReads = 0
  await page.route(PROFILE_PATTERN, async (route) => {
    refusedReads += 1
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Server error', status: 500 }),
    })
  })
  await switchToShop(page)

  // The read is awaited first: a bare shell shows only the loading skeleton, so a full menu
  // below proves the refusal was applied.
  await expect
    .poll((): number => {
      return refusedReads
    })
    .toBeGreaterThan(0)
  await expect
    .poll(async (): Promise<string[]> => {
      return topLevelNames(page)
    })
    .toEqual(EVERYTHING_MENU)
  await openSection(page, 'My account')
  await expect(mainNavigation(page).getByRole('link', { name: 'Sub-users', exact: true })).toBeVisible()
})

test('a phone shows no destinations in the tab bar while the new account access read is still pending', async ({
  page,
}) => {
  await stubTwoAccountPanel(page, PHONE)
  await page.goto('/')
  await expect(tabBar(page).getByRole('link', { name: 'Home', exact: true })).toBeVisible()

  const held = await holdRoute(page, PROFILE_PATTERN)
  await switchToShop(page)
  await held.requested

  // The same unknown standing as on a desktop: the bar is busy and offers no destination, neither
  // the old account's nor the permissive default's.
  const bar = tabBar(page)
  await expect(bar).toHaveAttribute('aria-busy', 'true')
  await expect(bar.getByRole('link')).toHaveCount(0)

  held.release()

  await expect(bar.getByRole('link', { name: 'Home', exact: true })).toBeVisible()
  await expect(bar).not.toHaveAttribute('aria-busy', 'true')
})

test('a collapsed rail shows no destinations while the first account access read is still pending', async ({
  page,
}) => {
  // The switch reloads the page and a reload brings the full sidebar back (the collapsed state is
  // not kept), so the rail is reached on the FIRST load instead: the read is held before the page
  // opens, and the sidebar is collapsed by its own button while the skeleton is drawn.
  await stubTwoAccountPanel(page, DESKTOP)
  const held = await holdRoute(page, PROFILE_PATTERN)
  await page.goto('/')
  await held.requested
  await page.getByRole('button', { name: 'Collapse the sidebar' }).click()

  const rail = mainNavigation(page)
  await expect(page.getByRole('button', { name: 'Expand the sidebar' })).toBeVisible()
  await expect(rail).toHaveAttribute('aria-busy', 'true')
  await expect(rail.getByRole('link')).toHaveCount(0)
  await expect(rail.getByRole('button')).toHaveCount(0)

  held.release()

  await expect(rail.getByRole('link', { name: 'Backups', exact: true })).toBeVisible()
  await expect(rail).not.toHaveAttribute('aria-busy', 'true')
})
