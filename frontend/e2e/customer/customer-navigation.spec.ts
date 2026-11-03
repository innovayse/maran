import { expect, test, type Page } from '@playwright/test'
import { FULL_CATALOGUE, breadcrumbItems, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { OWNER_PROFILE, stubProfile } from '../fixtures/stub-profile-route'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #312: the customer's menu is the same navigation tree as the operators', filtered by role.
// Nine items, nothing that is the server's; pages not built yet are drawn disabled; the tasks page
// sits under Activity for a customer and the bottom bar is Home · Sites · Mail · Account · More.
// Navigation is by clicks only.

const OWN_ACCOUNT: MyAccount = {
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
  },
}

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/** The customer's top-level items, in drawing order. */
const CUSTOMER_ITEMS = [
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

const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  // The account's owner: the menu offers Sub-users, which only an owner manages.
  await stubProfile(page, OWNER_PROFILE)
  await page.goto('/')
}

const topLevelNames = async (page: Page): Promise<string[]> => {
  return mainNavigation(page)
    .locator('ul[aria-labelledby] > li')
    .evaluateAll((rows): string[] => {
      return rows.map((row): string => {
        return row.querySelector('a, [role="link"]')?.firstElementChild?.textContent?.trim() ?? ''
      })
    })
}

test('a customer sees the nine items of their own account in order', async ({ page }) => {
  await signIn(page)
  await expect.poll(async (): Promise<string[]> => {
    return topLevelNames(page)
  }).toEqual(CUSTOMER_ITEMS)
})

test('the mail a customer cannot have yet is shown disabled, not left out', async ({ page }) => {
  await signIn(page)
  const mail = mainNavigation(page).getByRole('link', { name: 'Mail Coming soon' })
  await expect(mail).toHaveAttribute('aria-disabled', 'true')
  await expect(mail).not.toHaveAttribute('href', /.*/)
})

test('a customer reaches their tasks under Activity, and the breadcrumb says so', async ({ page }) => {
  await signIn(page)
  await openSection(page, 'Activity')
  await mainNavigation(page).getByRole('link', { name: 'Background tasks', exact: true }).click()

  await expect(page).toHaveURL('/tasks')
  await expect(breadcrumbItems(page).nth(1)).toHaveText('Activity')
})

test('a customer reaches their own signed-in devices from My account', async ({ page }) => {
  await signIn(page)
  await openSection(page, 'My account')
  // Sub-users is built (issue #315): an ordinary link now, no longer the disabled "Coming soon" entry,
  // and the owner is offered it (issue #328).
  await expect(mainNavigation(page).getByRole('link', { name: 'Sub-users', exact: true })).toHaveAttribute(
    'href',
    '/my-account/sub-users',
  )
  await mainNavigation(page).getByRole('link', { name: 'Signed-in devices', exact: true }).click()
  await expect(page).toHaveURL('/settings/sessions')
})

test('on a phone the customer bar is Home, Sites, Mail disabled, Account and More', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await signIn(page)
  const bar = page.getByRole('navigation', { name: 'Sections' })

  await expect(bar.getByRole('link')).toHaveText(['Home', 'Sites', /^Mail/, 'Account'])
  await expect(bar.getByRole('link', { name: 'Mail Coming soon' })).toHaveAttribute('aria-disabled', 'true')
  await expect(bar.getByRole('button', { name: 'More' })).toBeVisible()

  await bar.getByRole('link', { name: 'Account', exact: true }).click()
  await expect(page).toHaveURL('/my-account')
})

test('an administrator bar keeps its own four tabs', async ({ page }) => {
  // Positive control: the customer's order is chosen by role, not applied to everyone.
  await page.setViewportSize({ width: 390, height: 844 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto('/')

  const bar = page.getByRole('navigation', { name: 'Sections' })
  await expect(bar.getByRole('link')).toHaveText(['Home', 'Accounts', 'Sites', 'Databases'])
})
