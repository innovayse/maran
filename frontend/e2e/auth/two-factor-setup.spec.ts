import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubProfile } from '../fixtures/stub-profile-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubSteeredAdministrator } from '../fixtures/stub-password-reset-routes'
import { stubSessions } from '../fixtures/stub-sessions-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Page } from '@playwright/test'

test('an administrator steered into enrolment lands there from any URL, with no navigation', async ({
  page,
}) => {
  await stubSteeredAdministrator(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  // A deep link into a licensed module.
  await page.goto('/accounts')
  await expect(page).toHaveURL('/two-factor-setup')

  // Their own settings, which their token also cannot reach.
  await page.goto('/settings/sessions')
  await expect(page).toHaveURL('/two-factor-setup')

  // Even the sign-in screen, which a signed-in visitor is otherwise sent away from
  // to the panel's home page.
  await page.goto('/login')
  await expect(page).toHaveURL('/two-factor-setup')

  // No shell around it: the navigation landmark the authenticated layout renders is
  // absent, so there is nothing to click through to a screen that could only 403.
  // This can fail — the same assertion on `/settings/sessions` for an unsteered
  // administrator finds the landmark and goes red.
  await expect(page.getByRole('navigation')).toHaveCount(0)
  // Level 2, and exactly one level 1: the auth layout's wordmark is the screen's h1, so the page's
  // own title is an h2 here (issue #82). Asserting the title as a bare heading let a second h1
  // through; the h1 count is the half that protects the outline.
  await expect(page.getByRole('heading', { name: 'Two-step verification', level: 2 })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
})

test('the navigation this spec looks for is really there when nobody is steered', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubSessions(page, [])

  await page.goto('/settings/sessions')

  // The control assertion for the one above: without this, "no navigation" would
  // pass on a page that never renders navigation in the first place — the exact
  // failure mode of a spec in this repository that asserted a heading was absent on
  // a screen with no headings at all.
  await expect(page.getByRole('navigation').first()).toBeVisible()
})

/**
 * Holds an administrator on the steered enrolment screen and answers its endpoints. The refresh
 * endpoint tells the truth about the account as it stands: steered until the confirmation has
 * been answered, signed in afterwards, and signed out once the logout has been answered.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const holdOnEnrolment = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  let stage: 'steered' | 'enrolled' | 'signedOut' = 'steered'
  // Installed after stubSignedIn on purpose: the newest route wins, and this one varies by stage.
  await page.route('**/api/v1/auth/refresh', async (route) => {
    if (stage === 'signedOut') {
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'stub.access.token',
        expiresAt: '2099-01-01T00:00:00+00:00',
        user: stubbedAdministrator,
        requiresTwoFactorSetup: stage === 'steered',
      }),
    })
  })
  await page.route('**/api/v1/auth/two-factor/enrol', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ secret: 'JBSWY3DPEHPK3PXP', provisioningUri: 'otpauth://totp/Maran:admin?secret=JBSWY3DPEHPK3PXP' }),
    })
  })
  await page.route('**/api/v1/auth/two-factor/confirm', async (route) => {
    stage = 'enrolled'
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ codes: ['aaaa-bbbb-cccc'] }) })
  })
  await page.route('**/api/v1/auth/logout', async (route) => {
    stage = 'signedOut'
    await route.fulfill({ status: 204 })
  })
}

test('the steered enrolment screen says the administrator requires it', async ({ page }) => {
  await holdOnEnrolment(page)

  await page.goto('/two-factor-setup')

  await expect(page.getByText('The administrator of this panel requires two-step verification')).toBeVisible()
  // Control: the shell's own copy of the page, which nobody is forced onto, carries no such sentence.
  await page.unroute('**/api/v1/auth/refresh')
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubProfile(page, {
    username: stubbedAdministrator.username,
    email: stubbedAdministrator.email,
    role: 'admin',
    isTwoFactorEnabled: false,
    activeSessionCount: 1,
  })
  await page.goto('/settings/two-factor')
  await expect(page.getByRole('button', { name: 'Set up' })).toBeVisible()
  await expect(page.getByText('The administrator of this panel requires two-step verification')).toHaveCount(0)
})

test('signing out of the steered enrolment screen returns to the sign-in screen', async ({ page }) => {
  await holdOnEnrolment(page)

  await page.goto('/two-factor-setup')
  await page.getByRole('button', { name: 'Sign out' }).click()

  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
})

test('Continue after the recovery codes enters the panel', async ({ page }) => {
  await holdOnEnrolment(page)
  // Only the panel behind the Continue button asks for these; the enrolment screen asks for none.
  await stubShellReads(page)
  await stubLandingReads(page)

  await page.goto('/two-factor-setup')
  // Before the codes there is nothing to continue to.
  await expect(page.getByRole('button', { name: 'Continue' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Set up' }).click()
  await page.getByLabel('Code from the app').fill('123456')
  await page.getByRole('button', { name: 'Turn on' }).click()

  await expect(page.getByText('aaaa-bbbb-cccc')).toBeVisible()
  await page.getByRole('button', { name: 'Continue' }).click()

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('navigation').first()).toBeVisible()
})
