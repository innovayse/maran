import type { Page } from '@playwright/test'
import type { MembershipOffer } from '../../src/types/membershipOffer'

/** The collection endpoint the offers are listed from, with or without a query. */
const OFFERS_PATTERN = /\/api\/v1\/me\/membership-offers(\?.*)?$/

/** The accept and decline endpoints of one offer. */
const ANSWER_PATTERN = /\/api\/v1\/me\/membership-offers\/[^/]+\/(accept|decline)(\?.*)?$/

/** What the stubbed panel did with the answers a page sent, for a spec to assert. */
export interface MembershipOffersSent {
  /** The ids of the offers accepted, in order. */
  accepted: string[]
  /** The ids of the offers declined, in order. */
  declined: string[]
}

/** How the stub behaves; every field is optional. */
export interface MembershipOffersBehaviour {
  /** When set, every accept and decline is refused with this status and sentence instead of being made. */
  refuseWith?: { status: number; detail: string }
  /** Called with the offer when an accept is made, so a spec can let the account switcher's stub follow. */
  onAccepted?: (offer: MembershipOffer) => void
}

/**
 * Fulfils the invitee's offer surface from one catalogue: the list, an accept and a decline. An
 * answered offer leaves the catalogue, so the page's re-read no longer shows it; an id the stub does
 * not hold is a 404, as the panel answers a foreign or expired one.
 * @param page The Playwright page whose network the routes are installed on.
 * @param offers The offers addressed to the signed-in person; mutated as they are answered.
 * @param behaviour Optional refusal and accept hook.
 * @returns What the page sent: the accepted and declined offer ids, in order.
 */
export const stubMembershipOffers = async (
  page: Page,
  offers: MembershipOffer[],
  behaviour: MembershipOffersBehaviour = {},
): Promise<MembershipOffersSent> => {
  const sent: MembershipOffersSent = { accepted: [], declined: [] }

  await page.route(OFFERS_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(offers) })
  })

  await page.route(ANSWER_PATTERN, async (route) => {
    const segments = new URL(route.request().url()).pathname.split('/')
    const verb = segments[segments.length - 1]
    const id = segments[segments.length - 2] ?? ''
    const index = offers.findIndex((offer) => {
      return offer.id === id
    })

    const refuse = behaviour.refuseWith
    if (refuse !== undefined || index < 0) {
      const detail = refuse?.detail ?? 'This offer could not be found.'
      await route.fulfill({
        status: refuse?.status ?? 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'MembershipOfferNotFound', title: detail, detail, status: refuse?.status ?? 404 }),
      })
      return
    }

    const [offer] = offers.splice(index, 1)
    if (verb === 'accept') {
      sent.accepted.push(id)
      if (offer !== undefined) {
        behaviour.onAccepted?.(offer)
      }
    } else {
      sent.declined.push(id)
    }
    await route.fulfill({ status: 204 })
  })

  return sent
}
