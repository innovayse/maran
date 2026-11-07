import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubProfile } from '../fixtures/stub-profile-route'

/** The secret a stubbed enrolment hands out. */
const SECRET = 'JBSWY3DPEHPK3PXP'

/** The sentence the backend answers a refused code with, which the page must show as it came. */
const REFUSED = 'That code is not valid. Check your authenticator app and try again.'

/** How many times each endpoint a spec watches has been called. */
interface Calls {
  /** Requests that reached the confirmation or disable endpoint under test. */
  proof: number
  /** Requests that reached the session renewal endpoint. */
  refresh: number
}

/**
 * Signs an administrator in to the shell with the given two-factor state in their profile and
 * counts every call to the session renewal endpoint.
 * @param page The page whose network is stubbed.
 * @param isTwoFactorEnabled What the profile reports.
 * @returns The live counters; `refresh` includes the renewal the router guard makes on load.
 */
const signInCounting = async (page: Page, isTwoFactorEnabled: boolean): Promise<Calls> => {
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

  const calls: Calls = { proof: 0, refresh: 0 }
  // Installed after the sign-in stub on purpose: Playwright matches the newest route first, and this
  // one hands the request on to the stub that answers it, so only the counting is added.
  await page.route('**/api/v1/auth/refresh', async (route) => {
    calls.refresh += 1
    await route.fallback()
  })
  return calls
}

test('a refused enrolment code is ONE confirm request: no renewal and no replay', async ({ page }) => {
  const calls = await signInCounting(page, false)
  await page.route('**/api/v1/auth/two-factor/confirm', async (route) => {
    calls.proof += 1
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'TwoFactorCodeInvalid', detail: REFUSED }),
    })
  })

  await page.goto('/settings/two-factor')
  await page.getByRole('button', { name: 'Set up' }).click()
  const refreshedBeforeClick = calls.refresh
  await page.getByRole('textbox', { name: 'Code from the app' }).fill('000000')
  await page.getByRole('button', { name: 'Turn on' }).click()

  await expect(page.getByText(REFUSED)).toBeVisible()
  // This can fail: before issue #412 the endpoint answered 401, the client renewed the session and
  // replayed, so these were 2 and 1 more.
  expect(calls.proof).toBe(1)
  expect(calls.refresh).toBe(refreshedBeforeClick)
})

test('a refused code for turning two-factor off is ONE disable request: no renewal and no replay', async ({
  page,
}) => {
  const calls = await signInCounting(page, true)
  await page.route('**/api/v1/auth/two-factor/disable', async (route) => {
    calls.proof += 1
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'TwoFactorCodeInvalid', detail: REFUSED }),
    })
  })

  await page.goto('/settings/two-factor')
  const code = page.getByRole('textbox', { name: 'Code', exact: true })
  // The card renders after the router guard's own renewal; counting before it exists would blame the
  // click for that renewal.
  await expect(code).toBeVisible()
  const refreshedBeforeClick = calls.refresh
  await code.fill('000000')
  await page.getByRole('button', { name: 'Turn off', exact: true }).click()
  await page.getByRole('button', { name: 'Turn off two-step verification' }).click()

  await expect(page.getByText(REFUSED)).toBeVisible()
  expect(calls.proof).toBe(1)
  expect(calls.refresh).toBe(refreshedBeforeClick)
})

test('an expired access token on confirm is still renewed and replayed once (the control)', async ({ page }) => {
  const calls = await signInCounting(page, false)
  // The first answer says the SESSION is the problem, the replay after the renewal succeeds: the
  // renew-and-replay is still there for a real 401, so the two specs above prove the status, not a
  // disabled replay.
  await page.route('**/api/v1/auth/two-factor/confirm', async (route) => {
    calls.proof += 1
    if (calls.proof === 1) {
      await route.fulfill({ status: 401, contentType: 'application/problem+json', body: '{}' })
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ codes: ['aaaa-bbbb-cccc'] }),
    })
  })

  await page.goto('/settings/two-factor')
  await page.getByRole('button', { name: 'Set up' }).click()
  const refreshedBeforeClick = calls.refresh
  await page.getByRole('textbox', { name: 'Code from the app' }).fill('123456')
  await page.getByRole('button', { name: 'Turn on' }).click()

  await expect(page.getByText('aaaa-bbbb-cccc', { exact: true })).toBeVisible()
  expect(calls.proof).toBe(2)
  expect(calls.refresh).toBe(refreshedBeforeClick + 1)
})
