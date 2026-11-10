import { expect, test } from '../fixtures/guarded-test'
import { stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import type { Page } from '@playwright/test'

/** How a stubbed spend endpoint answers a successful spend. */
interface SpendAnswer {
  /** The response status. */
  status: number
  /** The media type of the body, absent when there is none. */
  contentType?: string
  /** The serialized body, absent when there is none. */
  body?: string
}

/** The password reset's success: no content. */
const RESET_SPENT: SpendAnswer = { status: 204 }

/** The invitation's success: the username the invitation activated. */
const INVITATION_SPENT: SpendAnswer = {
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify({ username: 'olive' }),
}

/** A token that is easy to find in a URL, a header or a request body. */
const TOKEN = 'live-link-token-9f3a1c'

/**
 * Records every request the page makes whose Referer or address names the token, and answers the
 * spend endpoint with success, so the spec sees both what leaked and what was sent on purpose.
 * @param page The page to observe.
 * @param endpoint The spend endpoint, such as `**\/api/v1/auth/reset-password`.
 * @param success How the panel answers a spend: the reset answers 204 with no body, the invitation
 * 200 with the username it activated.
 * @returns The list that fills with the offending addresses, and the body the spend posted.
 */
const watchRequests = async (
  page: Page,
  endpoint: string,
  success: SpendAnswer,
): Promise<{ leaks: string[]; posted: () => string }> => {
  const leaks: string[] = []
  let body = ''
  page.on('request', (request) => {
    const referer = request.headers()['referer'] ?? ''
    // The document load of the link itself carries the token in its address by definition; what the
    // spec watches is every other request naming it and any request, navigation or not, whose Referer does.
    const namesTokenInAddress = request.url().includes(TOKEN) && !request.isNavigationRequest()
    if (namesTokenInAddress || referer.includes(TOKEN)) {
      leaks.push(`${request.method()} ${request.url()} referer=${referer}`)
    }
  })
  await page.route(endpoint, async (route) => {
    body = route.request().postData() ?? ''
    await route.fulfill(success)
  })
  return {
    leaks,
    posted: (): string => {
      return body
    },
  }
}

test('a reset link’s token leaves the address before any request, and is still the one spent', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  const watched = await watchRequests(page, '**/api/v1/auth/reset-password', RESET_SPENT)

  await page.goto(`/reset-password?token=${TOKEN}`)
  await expect(page.getByLabel('Password', { exact: true })).toBeVisible()

  // The address no longer carries it, and no request the page made named it.
  expect(new URL(page.url()).search).toBe('')
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery staple')
  await page.getByLabel('Confirm password').fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Set the password' }).click()

  // Inverse control: the token is not simply lost, it is what the spend posted.
  await expect.poll(() => {return watched.posted()}).toContain(TOKEN)
  expect(watched.leaks).toEqual([])
})

test('an invitation link’s token leaves the address before any request', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  const watched = await watchRequests(page, '**/api/v1/auth/accept-invitation', INVITATION_SPENT)

  await page.goto(`/accept-invitation?token=${TOKEN}`)
  await expect(page.locator('input[autocomplete="new-password"]').first()).toBeVisible()

  expect(new URL(page.url()).search).toBe('')
  expect(watched.leaks).toEqual([])
})

test('a link without a token is left exactly as it is and states the missing token', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/reset-password?lang=hy')

  expect(new URL(page.url()).search).toBe('?lang=hy')
  await expect(page.getByRole('link', { name: 'Back to sign in' }).first()).toBeVisible()
})
