import type { Page } from '@playwright/test'
import { stubSetupState } from './stub-auth-routes'
import type { AuthenticatedUser } from '../../src/types/auth'

/** The customer an administrator created with an initial password, who signs in with it. */
export const STEERED_CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000042',
  username: 'pwtestb',
  email: 'pwtestb@example.com',
  role: 'customer',
  accountId: '22222222-2222-2222-2222-222222222222',
}

/** What the stubbed change-password endpoint did, so a spec can assert on the request itself. */
export interface RequiredPasswordChangeStub {
  /** The bodies the change-password endpoint received, in order. */
  changeBodies: unknown[]
  /** Every path the panel was asked for that a first-password token cannot reach. */
  shellReads: string[]
}

/**
 * Answers the panel as it stands for a login that holds the first password somebody else chose:
 * the refresh endpoint reports the restriction until the change has been accepted and reports a
 * fully signed-in customer afterwards, the way the real backend re-issues the token.
 *
 * It also records every request a first-password token cannot reach (the profile, the account, the
 * dashboard, the view-as read), so a spec can prove the shell never started rather than only that
 * a heading is on screen.
 * @param page The Playwright page whose network the routes are installed on.
 * @param refusal When given, the change is refused with this localized sentence instead of accepted.
 * @returns What the stubs recorded, filled in as the page runs.
 */
export const stubRequiredPasswordChange = async (
  page: Page,
  refusal: string | null = null,
): Promise<RequiredPasswordChangeStub> => {
  await stubSetupState(page, { isComplete: true })

  const recorded: RequiredPasswordChangeStub = { changeBodies: [], shellReads: [] }
  let changed = false
  let signedOut = false

  await page.route('**/api/v1/auth/refresh', async (route) => {
    if (signedOut) {
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'stub.access.token',
        expiresAt: '2099-01-01T00:00:00+00:00',
        user: STEERED_CUSTOMER,
        requiresTwoFactorSetup: false,
        requiresPasswordChange: !changed,
      }),
    })
  })

  await page.route('**/api/v1/auth/change-password', async (route) => {
    recorded.changeBodies.push(route.request().postDataJSON())
    if (refusal !== null) {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'CurrentPasswordIncorrect', detail: refusal }),
      })
      return
    }
    changed = true
    await route.fulfill({ status: 204 })
  })

  await page.route('**/api/v1/auth/logout', async (route) => {
    signedOut = true
    await route.fulfill({ status: 204 })
  })

  // Registered last, so a request that matches one of these is recorded and answered 403 exactly
  // as the real endpoint answers a first-password token.
  await page.route(/\/api\/v1\/(profile|accounts\/me|dashboard|view-as|me\/accounts|me\/pinned-pages)(\/|\?|$)/, async (route) => {
    recorded.shellReads.push(new URL(route.request().url()).pathname)
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostPasswordChangeRequired', detail: 'You must choose a new password before you can continue.' }),
    })
  })

  return recorded
}
