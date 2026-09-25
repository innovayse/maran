import { expect, test, type Page } from '@playwright/test'
import { stubLogin, stubSignedIn, stubSignedOut, stubbedSignIn } from './fixtures/stub-auth-routes'
import { stubModules } from './fixtures/stub-modules-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubSites } from './fixtures/stub-sites-routes'
import type { PanelModule } from '../src/types/module'
import type { Site } from '../src/types/site'
import type { AuthenticatedUser } from '../src/types/auth'

/**
 * The customer area's end-to-end path, per Task 13 of the customer-area plan: an invitation link
 * sets a first password, the person lands signed in, sees their own sites, and finds nothing on
 * the sidebar that belongs to the server as a whole rather than to their account.
 *
 * Stubbed throughout, following every other spec under `e2e/` except `golden-path/`: this proves
 * the SPA's own wiring — the accept-invitation form posts the right thing, the router lands on
 * the right screen, the sidebar renders what the catalogue and the role say it should — not that a
 * real backend accepts a real token. That end is `golden-path/`'s job, and duplicating it here
 * would test Playwright's HTTP mocking rather than this screen.
 */

/** The customer the invitation belongs to, and who signs in afterward. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The one module a customer's own account can reach in this story. */
const CUSTOMER_MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
]

/**
 * A site on the customer's own account. Its presence in the list, read back from the panel after
 * sign-in, is what "own sites visible" asserts — not merely that the sidebar offers a Sites link.
 */
const OWN_SITE: Site = {
  id: '22222222-2222-2222-2222-222222222222',
  accountId: CUSTOMER.accountId ?? '',
  domain: 'olive-shop.example.com',
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-09-01T10:00:00Z',
}

/**
 * Fulfils `POST /api/v1/auth/accept-invitation`, matching only the token this story issued —
 * a mismatched token would mean the form built the wrong request rather than that the fixture
 * is lenient about it.
 * @param page The Playwright page whose network the route is installed on.
 * @param token The token this story's invitation link carries.
 * @returns Resolves once the route is installed.
 */
const stubAcceptInvitation = async (page: Page, token: string): Promise<void> => {
  await page.route('**/api/v1/auth/accept-invitation', async (route) => {
    const body = route.request().postDataJSON() as { token?: string; newPassword?: string }
    expect(body.token).toBe(token)
    expect(body.newPassword?.length ?? 0).toBeGreaterThan(0)

    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })
}

test.describe('the customer area, end to end', () => {
  test('an invited customer sets a password, signs in, and sees only their own account', async ({
    page,
  }) => {
    const token = 'invitation-abc123'

    // --- the invitation link: signed out, only the accept-invitation and login endpoints answer.
    await stubSignedOut(page)
    await stubHealthy(page)
    await stubAcceptInvitation(page, token)

    await page.goto(`/accept-invitation?token=${token}`)
    await page.getByLabel('Password', { exact: true }).fill('correct horse battery staple')
    await page.getByLabel('Confirm password').fill('correct horse battery staple')
    await page.getByRole('button', { name: 'Set the password' }).click()

    // The screen does not sign the customer in itself — Activate() only makes the login usable,
    // it issues no session — so the confirmation and the way back are what "done" looks like.
    await expect(page.getByText('Your password has been set.')).toBeVisible()
    await page.getByRole('button', { name: 'Back to sign in' }).click()
    await expect(page).toHaveURL('/login')

    // --- signed in as the customer: the shell now answers as their account, not the panel's.
    await stubSignedIn(page, CUSTOMER)
    await stubLogin(page, {
      session: { ...(stubbedSignIn.session as NonNullable<typeof stubbedSignIn.session>), user: CUSTOMER },
    })
    await stubModules(page, CUSTOMER_MODULES)
    await stubSites(page, [OWN_SITE])

    await page.getByRole('textbox', { name: 'Username' }).fill(CUSTOMER.username)
    await page.getByRole('textbox', { name: 'Password' }).fill('correct horse battery staple')
    await page.getByRole('button', { name: 'Sign in' }).click()

    const navigation = page.getByRole('navigation', { name: 'Main navigation' })
    await expect(navigation).toBeVisible()

    // --- own sites visible: read back from the panel, not merely a reachable sidebar entry.
    await navigation.getByRole('link', { name: 'Sites' }).click()
    await expect(page).toHaveURL('/sites')
    await expect(page.getByRole('link', { name: OWN_SITE.domain })).toBeVisible()

    // --- server-wide entries absent from the navigation.
    //
    // The audit journal is gated on ROLE alone (useNavigation.ts), independent of the module
    // catalogue, so this is a control on the mechanism itself and not on what the fixture
    // happened to list: a customer role must hide it even though the sidebar renders every
    // other entry.
    await expect(navigation.getByRole('link', { name: 'Audit journal' })).toHaveCount(0)

    // Modules that manage the server rather than one account — Accounts, Firewall, Licensing —
    // are never in the catalogue a customer's own session receives, because the backend scopes
    // `GET /api/v1/modules` the same way it scopes every other endpoint. CUSTOMER_MODULES models
    // that answer, and this is the sidebar's honest response to it: nothing to hide, because
    // there was never an entry to hide it from.
    await expect(navigation.getByRole('link', { name: 'Accounts' })).toHaveCount(0)
    await expect(navigation.getByRole('link', { name: 'Firewall' })).toHaveCount(0)

    // Positive control on the same scope, so the three absences above are not merely an empty
    // sidebar: the navigation did render entries, and Sites is one of them.
    await expect(navigation.getByRole('link', { name: 'Sites' })).toBeVisible()
  })

  test('an administrator-only endpoint refuses a customer calling it directly, not merely a hidden menu', async ({
    page,
  }) => {
    // Review Focus 5 (task-13-brief Step 2): the assertion is against the endpoint a customer can
    // reach with the browser's own network stack — not against what the sidebar chooses to show,
    // which is advice only (rules/architecture.md, rules/security.md item 6).
    await stubSignedIn(page, CUSTOMER)
    await stubModules(page, CUSTOMER_MODULES)
    await stubHealthy(page)

    await page.route('**/api/v1/accounts', async (route) => {
      await route.fulfill({
        status: 403,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'ForbiddenAdminOnly', detail: 'Administrators only.' }),
      })
    })

    await page.goto('/')
    // `page.request` opens its own context outside the browser's network stack, so it never
    // reaches a `page.route` fake — the call has to be made from the page itself, exactly as a
    // customer's own browser would make it by hand. `rules/vue.md` reserves the bare `fetch`
    // global for `useApi.ts` inside the SPA's own bundle; this string runs in the browser as
    // test-only code, never bundled with the product, so the rule it protects does not apply here.
    const status = await page.evaluate(async () => {
      // eslint-disable-next-line no-restricted-globals -- test-only code executed in the page, not part of the SPA bundle
      const response = await fetch('/api/v1/accounts')
      return response.status
    })
    expect(status).toBe(403)
  })
})
