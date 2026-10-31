import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubProfile } from '../fixtures/stub-profile-route'
import type { ChangePasswordRequest, UserRole } from '../../src/types/auth'

/** The password the stubbed panel accepts as the caller's current one. */
const CURRENT_PASSWORD = 'the old password'

/** The refusal the stubbed panel words for a wrong current password. */
const WRONG_PASSWORD_DETAIL = 'The current password is not correct.'

/**
 * Signs a person in with the given role, answers their profile, and answers the password change
 * the way the panel does: 200 for the right current password, the classified 400 otherwise.
 * @param page The Playwright page whose network the routes are installed on.
 * @param role The role the panel reports for the signed-in user.
 * @returns The request bodies the change endpoint received, in order.
 */
const stubPanel = async (page: Page, role: UserRole): Promise<ChangePasswordRequest[]> => {
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
    isTwoFactorEnabled: false,
    activeSessionCount: 2,
  })
  const received: ChangePasswordRequest[] = []
  await page.route(/\/api\/v1\/auth\/change-password$/, async (route) => {
    const body = route.request().postDataJSON() as ChangePasswordRequest
    received.push(body)
    if (body.currentPassword === CURRENT_PASSWORD) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        status: 400,
        code: 'CurrentPasswordIncorrect',
        title: WRONG_PASSWORD_DETAIL,
        detail: WRONG_PASSWORD_DETAIL,
      }),
    })
  })
  return received
}

/**
 * Opens the profile the way a person does: the account menu, then its profile entry.
 * @param page The Playwright page.
 * @returns Resolves once the profile heading is on screen.
 */
const openProfile = async (page: Page): Promise<void> => {
  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Profile and security' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Profile and security' })).toBeVisible()
}

/**
 * Fills the three password fields.
 * @param page The Playwright page.
 * @param current The current password typed.
 * @param next The new password typed.
 * @param confirmation The new password typed again.
 * @returns Resolves once the fields are filled.
 */
const fillForm = async (page: Page, current: string, next: string, confirmation: string): Promise<void> => {
  await page.getByLabel('Current password', { exact: true }).fill(current)
  await page.getByLabel('New password', { exact: true }).fill(next)
  await page.getByLabel('Confirm password', { exact: true }).fill(confirmation)
}

for (const role of ['admin', 'customer'] as const) {
  test(`${role}: changing the password from the profile sends both passwords and says the rest are signed out`, async ({
    page,
  }) => {
    const received = await stubPanel(page, role)
    await openProfile(page)

    await fillForm(page, CURRENT_PASSWORD, 'a brand new passphrase', 'a brand new passphrase')
    await page.getByRole('button', { name: 'Change password' }).click()

    await expect(page.getByText('Your password has been changed. Every other device has been signed out.')).toBeVisible()
    expect(received).toEqual([{ currentPassword: CURRENT_PASSWORD, newPassword: 'a brand new passphrase' }])
    await expect(page.getByLabel('Current password', { exact: true })).toHaveValue('')
  })

  test(`${role}: a wrong current password shows the panel's own refusal`, async ({ page }) => {
    await stubPanel(page, role)
    await openProfile(page)

    await fillForm(page, 'not the password', 'a brand new passphrase', 'a brand new passphrase')
    await page.getByRole('button', { name: 'Change password' }).click()

    await expect(page.getByText(WRONG_PASSWORD_DETAIL)).toBeVisible()
  })
}

test('two different new entries are caught on the page and nothing is sent', async ({ page }) => {
  const received = await stubPanel(page, 'admin')
  await openProfile(page)

  await fillForm(page, CURRENT_PASSWORD, 'a brand new passphrase', 'a different passphrase')
  await page.getByRole('button', { name: 'Change password' }).click()

  await expect(page.getByText('The passwords do not match.')).toBeVisible()
  expect(received).toEqual([])
})

test('a short new password is not refused on the page: the panel is the judge', async ({ page }) => {
  const received = await stubPanel(page, 'customer')
  await openProfile(page)

  await fillForm(page, CURRENT_PASSWORD, 'short', 'short')
  await page.getByRole('button', { name: 'Change password' }).click()

  await expect
    .poll(() => {
      return received.length
    })
    .toBe(1)
})
