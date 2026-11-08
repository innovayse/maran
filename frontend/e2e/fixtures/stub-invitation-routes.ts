import type { Page } from '@playwright/test'
import type { InvitationState, InvitationStatus, InvitationSubject } from '../../src/types/invitation'

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
 * Answers the three invitation endpoints for one account (or reseller) without a backend: the status read, the
 * link creation and the resend.
 *
 * Every created link is different (`token-1`, `token-2`, ...) because the real panel mints a new
 * token each time and retires the old one — a stub returning the same link would let a spec pass
 * while the page failed to replace what it was showing. Links come back root-relative, as the
 * panel's do when no public address is configured, so the page's job of making them absolute is
 * exercised too.
 * @param page The Playwright page whose network the routes are installed on.
 * @param accountId The account (or reseller, see `kind`) whose invitation is being stubbed.
 * @param initial The state the invitation starts in.
 * @param kind Whether the id is a hosting account's or a reseller's; a reseller's routes sit under `resellers/`.
 * @returns Handles for reading the call counts and changing the state.
 */
export const stubInvitation = async (
  page: Page,
  accountId: string,
  initial: InvitationState,
  kind: InvitationSubject['kind'] = 'account',
): Promise<InvitationStub> => {
  const base = kind === 'reseller' ? `**/api/v1/invitations/resellers/${accountId}` : `**/api/v1/invitations/${accountId}`
  let state: InvitationState = initial
  let links = 0
  let resends = 0

  const status = (): InvitationStatus => {
    return state === 'pending' ? { state, expiresAt: EXPIRES_AT } : { state }
  }

  await page.route(base, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(status()) })
  })

  await page.route(`${base}/link`, async (route) => {
    links += 1
    state = 'pending'
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: { 'Cache-Control': 'no-store' },
      body: JSON.stringify({ link: `/accept-invitation?token=token-${links}`, expiresAt: EXPIRES_AT }),
    })
  })

  await page.route(`${base}/resend`, async (route) => {
    resends += 1
    state = 'pending'
    await route.fulfill({ status: 204 })
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
