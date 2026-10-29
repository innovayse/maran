import type { Page } from '@playwright/test'
import type { InvitationState, InvitationStatus } from '../../src/types/invitation'

/** What a spec can read back about the invitation endpoints after driving the page. */
export interface InvitationStub {
  /** How many `POST .../link` calls the panel has answered; each one retires the previous link. */
  readonly linkRequests: () => number
  /** How many `POST .../resend` calls the panel has answered. */
  readonly resendRequests: () => number
  /** Moves the invitation to another state, as if the owner had used it or it had lapsed. */
  readonly setState: (state: InvitationState) => void
}

/** When every stubbed invitation says it stops working. */
const EXPIRES_AT = '2026-11-02T09:00:00+00:00'

/**
 * Answers the three invitation endpoints for one account without a backend: the status read, the
 * link creation and the resend.
 *
 * Every created link is different (`token-1`, `token-2`, ...) because the real panel mints a new
 * token each time and retires the old one — a stub returning the same link would let a spec pass
 * while the page failed to replace what it was showing. Links come back root-relative, as the
 * panel's do when no public address is configured, so the page's job of making them absolute is
 * exercised too.
 * @param page The Playwright page whose network the routes are installed on.
 * @param accountId The account whose invitation is being stubbed.
 * @param initial The state the invitation starts in.
 * @returns Handles for reading the call counts and changing the state.
 */
export const stubInvitation = async (
  page: Page,
  accountId: string,
  initial: InvitationState,
): Promise<InvitationStub> => {
  let state: InvitationState = initial
  let links = 0
  let resends = 0

  const status = (): InvitationStatus => {
    return { state, expiresAt: state === 'pending' ? EXPIRES_AT : null }
  }

  await page.route(`**/api/v1/invitations/${accountId}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(status()) })
  })

  await page.route(`**/api/v1/invitations/${accountId}/link`, async (route) => {
    links += 1
    state = 'pending'
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: { 'Cache-Control': 'no-store' },
      body: JSON.stringify({ link: `/accept-invitation?token=token-${links}`, expiresAt: EXPIRES_AT }),
    })
  })

  await page.route(`**/api/v1/invitations/${accountId}/resend`, async (route) => {
    resends += 1
    state = 'pending'
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  return {
    linkRequests: (): number => {
      return links
    },
    resendRequests: (): number => {
      return resends
    },
    setState: (next: InvitationState): void => {
      state = next
    },
  }
}
