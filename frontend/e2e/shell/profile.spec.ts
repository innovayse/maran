import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubProfile } from '../fixtures/stub-profile-route'
import type { UserRole } from '../../src/types/auth'

/**
 * Signs a person in with the given role, answers their profile, and counts every request to the
 * customer's hosting-account endpoint, so a spec can assert an administrator never caused one.
 * @param page The Playwright page whose network the routes are installed on.
 * @param role The role the panel reports for the signed-in user.
 * @returns A function reporting how many times `/api/v1/accounts/me` was requested so far.
 */
const stubPanel = async (page: Page, role: UserRole): Promise<() => number> => {
  await stubSignedIn(page, {
    ...stubbedAdministrator,
    role,
    accountId: role === 'customer' ? '00000000-0000-0000-0000-0000000000a1' : null,
  })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubProfile(page, {
    username: stubbedAdministrator.username,
    email: stubbedAdministrator.email,
    role,
    isTwoFactorEnabled: role === 'admin',
    activeSessionCount: 2,
  })
  let accountCalls = 0
  await page.route(/\/api\/v1\/accounts\/me(\?.*)?$/, async (route) => {
    accountCalls += 1
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AccountNotFound', title: 'Not found', detail: 'Not found' }),
    })
  })
  return () => {
    return accountCalls
  }
}

test('an administrator reaches their profile from the account menu', async ({ page }) => {
  const accountCalls = await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Profile and security' }).click()

  await expect(page).toHaveURL('/profile')
  await expect(page.getByRole('heading', { level: 1, name: 'Profile and security' })).toBeVisible()
  await expect(page.getByText(stubbedAdministrator.email, { exact: true })).toBeVisible()
  await expect(page.getByText('On', { exact: true })).toBeVisible()
  await expect(page.getByText('2 devices', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Manage two-step verification' })).toHaveAttribute(
    'href',
    '/settings/two-factor',
  )
  await expect(page.getByRole('link', { name: 'Manage devices' })).toHaveAttribute('href', '/settings/sessions')
  await expect(page.getByRole('link', { name: 'Open my hosting account' })).toHaveCount(0)
  await expect(page).toHaveTitle('Profile and security · Maran')
  expect(accountCalls()).toBe(0)
})

test('a customer reaches their profile from the account menu and is offered their hosting account', async ({
  page,
}) => {
  await stubPanel(page, 'customer')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Profile and security' }).click()

  await expect(page).toHaveURL('/profile')
  await expect(page.getByText('Off', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open my hosting account' })).toHaveAttribute('href', '/my-account')
})

test('the profile is named in the breadcrumb', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubPanel(page, 'admin')

  await page.goto('/profile')

  await expect(page.getByRole('navigation', { name: 'Breadcrumb' })).toContainText('Profile and security')
})
