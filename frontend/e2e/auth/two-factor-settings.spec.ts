import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubProfile } from '../fixtures/stub-profile-route'
import { stubSteeredAdministrator } from '../fixtures/stub-password-reset-routes'

/** The secret a stubbed enrolment hands out. */
const SECRET = 'JBSWY3DPEHPK3PXP'

/** The recovery codes a stubbed confirmation returns. */
const RECOVERY_CODES = ['aaaa-bbbb-cccc', 'dddd-eeee-ffff']

/**
 * Signs an administrator in to the shell with the given two-factor state in their profile.
 * @param page The page whose network is stubbed.
 * @param isTwoFactorEnabled What the profile reports.
 * @returns Resolves once the stubs are installed.
 */
const signInWithTwoFactor = async (page: Page, isTwoFactorEnabled: boolean): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubProfile(page, {
    username: stubbedAdministrator.username,
    email: stubbedAdministrator.email,
    role: 'admin',
    isTwoFactorEnabled,
    activeSessionCount: 1,
  })
}

/**
 * Answers the enrolment, confirmation and disable endpoints as the panel does on success.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const stubTwoFactorEndpoints = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/auth/two-factor/enrol', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        secret: SECRET,
        provisioningUri: `otpauth://totp/Maran:admin?secret=${SECRET}&issuer=Maran`,
      }),
    })
  })
  await page.route('**/api/v1/auth/two-factor/confirm', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ codes: RECOVERY_CODES }) })
  })
  await page.route('**/api/v1/auth/two-factor/disable', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })
}

test('with two-factor off the page offers Set up and nothing to turn off', async ({ page }) => {
  await signInWithTwoFactor(page, false)

  await page.goto('/settings/two-factor')

  await expect(page.getByRole('button', { name: 'Set up' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Turn off' })).toHaveCount(0)
  await expect(page.getByText('Two-step verification is on')).toHaveCount(0)
})

test('with two-factor on the page says so and offers Turn off, not Set up', async ({ page }) => {
  await signInWithTwoFactor(page, true)

  await page.goto('/settings/two-factor')

  await expect(page.getByRole('heading', { name: 'Two-step verification is on' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Turn off' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Set up' })).toHaveCount(0)
})

test('the steered enrolment screen offers Set up only, and never asks for the profile', async ({ page }) => {
  await stubSteeredAdministrator(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  let profileCalls = 0
  await page.route(/\/api\/v1\/profile(\?.*)?$/, async (route) => {
    profileCalls += 1
    await route.fulfill({ status: 403, contentType: 'application/problem+json', body: '{}' })
  })

  await page.goto('/two-factor-setup')

  await expect(page.getByRole('button', { name: 'Set up' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Turn off' })).toHaveCount(0)
  expect(profileCalls).toBe(0)
})

test('while enrolling the page shows the QR code beside the secret', async ({ page }) => {
  await signInWithTwoFactor(page, false)
  await stubTwoFactorEndpoints(page)

  await page.goto('/settings/two-factor')
  await page.getByRole('button', { name: 'Set up' }).click()

  await expect(page.getByRole('img', { name: 'QR code for your authenticator app' })).toBeVisible()
  await expect(page.getByText(SECRET, { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Set up' })).toHaveCount(0)
})

test('turning two-factor on and then off switches the card each time', async ({ page }) => {
  await signInWithTwoFactor(page, false)
  await stubTwoFactorEndpoints(page)

  await page.goto('/settings/two-factor')
  await page.getByRole('button', { name: 'Set up' }).click()
  await page.getByRole('textbox', { name: 'Code from the app' }).fill('123456')
  await page.getByRole('button', { name: 'Turn on' }).click()

  // On: the status and Turn off, with the recovery codes still on screen right after confirming.
  await expect(page.getByRole('heading', { name: 'Two-step verification is on' })).toBeVisible()
  await expect(page.getByText(RECOVERY_CODES[0], { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Set up' })).toHaveCount(0)

  await page.getByRole('textbox', { name: 'Code', exact: true }).fill('654321')
  await page.getByRole('button', { name: 'Turn off' }).click()
  await page.getByRole('button', { name: 'Yes, turn off' }).click()

  // Off: Set up again, and nothing of the enrolled card left behind.
  await expect(page.getByRole('button', { name: 'Set up' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Turn off' })).toHaveCount(0)
  await expect(page.getByText(RECOVERY_CODES[0], { exact: true })).toHaveCount(0)
})
