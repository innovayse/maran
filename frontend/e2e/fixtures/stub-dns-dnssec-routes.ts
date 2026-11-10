import type { Page, Route } from '@playwright/test'
import { openDnsScreen } from './open-dns-screen'
import type { DnsStubState } from './stub-dns-routes'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { ZoneDnssec } from '../../src/types/dnsDnssec'

/** The DNSSEC endpoints of every zone. */
const DNSSEC_API = /\/api\/v1\/dns-zones\/[^/]+\/dnssec/

/** An instant long past, so the stub's withdrawal can already be finished. */
export const WITHDRAWAL_ELAPSED = '2020-01-01T00:00:00Z'

/** An instant far ahead, so the stub's withdrawal cannot be finished yet. */
export const WITHDRAWAL_PENDING = '2999-01-01T00:00:00Z'

/** A digest as long and unbreakable as a real SHA-256 DS digest. */
export const DS_DIGEST = '2BB183AF5F22588179A53B0A98631FAD1A292118ABCDEF0123456789ABCDEF01'

/** A public key as long and unbreakable as a real ECDSA P-256 DNSKEY. */
export const DNSKEY_PUBLIC_KEY = 'mdsswUyr3DPW132mOi8V9xESWE8jTo0dxCjjnopKl+GqJxpVXckHAeF+KkxLbxILfDLUT0rAK9iUzy1L53eKGQ=='

/** A zone that can be signed and is not: the host can sign, the zone is off, nothing to publish. */
export const OFF_DNSSEC: ZoneDnssec = {
  state: 'off',
  available: true,
  ds: null,
  dnskey: null,
  observed: null,
  withdrawalStartedAt: null,
  withdrawalEarliestFinishAt: null,
  rolloverAutomatic: false,
}

/** A host whose name server cannot sign: the zone is off and the pane must not be offered. */
export const UNAVAILABLE_DNSSEC: ZoneDnssec = { ...OFF_DNSSEC, available: false }

/** A signed zone with its DS record and DNSKEY, and the name server confirming both. */
export const SIGNED_DNSSEC: ZoneDnssec = {
  ...OFF_DNSSEC,
  state: 'signing',
  ds: { keyTag: 20326, algorithm: 13, digestType: 2, digest: DS_DIGEST },
  dnskey: { flags: 257, protocol: 3, algorithm: 13, publicKey: DNSKEY_PUBLIC_KEY },
  observed: { mode: 'signing', modeObserved: 'inForce', publishedKeyTag: 20326, publishedKeyObserved: 'inForce' },
}

/** A zone being unsigned whose waiting period is over, so the withdrawal can be finished. */
export const WITHDRAWING_DNSSEC: ZoneDnssec = {
  ...SIGNED_DNSSEC,
  state: 'withdrawing',
  observed: { mode: 'withdrawing', modeObserved: 'inForce', publishedKeyTag: 20326, publishedKeyObserved: 'inForce' },
  withdrawalStartedAt: '2019-12-29T00:00:00Z',
  withdrawalEarliestFinishAt: WITHDRAWAL_ELAPSED,
}

/** A zone being unsigned whose waiting period has not elapsed. */
export const WITHDRAWING_EARLY_DNSSEC: ZoneDnssec = {
  ...WITHDRAWING_DNSSEC,
  withdrawalStartedAt: '2998-12-30T00:00:00Z',
  withdrawalEarliestFinishAt: WITHDRAWAL_PENDING,
}

/** What a spec seeds the DNSSEC stub with, and reads back after the page acted. */
export interface DnsDnssecStubState {
  /** The signing the stub holds for every zone it is asked about. */
  dnssec: ZoneDnssec
  /**
   * Requests to refuse, by `"<METHOD> <action>"` where the action is `dnssec`, `dnssec/enable`,
   * `dnssec/withdraw` or `dnssec/finish-withdrawal`, with the sentence to answer.
   */
  refusals: Record<string, string>
  /** Every request the stub saw, oldest first. */
  requests: { method: string; url: URL; body: unknown }[]
}

/**
 * Answers a request with a JSON body.
 * @param route The intercepted request.
 * @param body The body.
 * @param status The status; 200 when left out.
 * @returns Resolves once the response is sent.
 */
const json = async (route: Route, body: unknown, status = 200): Promise<void> => {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/**
 * Answers a request with an RFC 7807 problem body, the shape the SPA renders verbatim.
 * @param route The intercepted request.
 * @param detail The panel's localized sentence.
 * @returns Resolves once the response is sent.
 */
const problem = async (route: Route, detail: string): Promise<void> => {
  await route.fulfill({
    status: 409,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code: 'ZoneSigningWithdrawalTooEarly', title: 'Refused', detail }),
  })
}

/**
 * Installs the DNSSEC API on a page, backed by mutable state, so a spec seeds what the panel holds,
 * drives the page and reads back what changed. A transition moves the stub's signing the way the
 * panel does (enable gives a key and a DS record, finish removes both), and moves the DNSSEC state of
 * the zones the DNS stub serves so the zone page and the delete confirmation read the same fact.
 * Install it AFTER {@link openDnsScreen}: a route registered later is asked first.
 * @param page The Playwright page whose network the routes are installed on.
 * @param zones The DNS stub's live state, whose zones follow the transitions.
 * @param seed What the panel holds; anything left out starts as a zone that can be signed and is not.
 * @returns The live state.
 */
export const stubDnsDnssec = async (
  page: Page,
  zones: DnsStubState,
  seed: Partial<DnsDnssecStubState> = {},
): Promise<DnsDnssecStubState> => {
  const state: DnsDnssecStubState = {
    dnssec: OFF_DNSSEC,
    refusals: {},
    requests: [],
    // A copy: a spec changes the live state, and the seed's objects are shared fixtures.
    ...structuredClone(seed),
  }

  /**
   * Moves the DNSSEC state of every zone the DNS stub holds to the stub's current one.
   * @returns Nothing.
   */
  const follow = (): void => {
    zones.zones = zones.zones.map((zone) => {
      return { ...zone, dnssecState: state.dnssec.state }
    })
  }

  // The zones start in the state the signing says, so a spec states it once.
  follow()

  await page.route(DNSSEC_API, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const body: unknown = request.postData() === null ? undefined : request.postDataJSON()
    state.requests.push({ method, url, body })

    const action = url.pathname.slice(url.pathname.indexOf('/dnssec') + 1)
    const refusal = state.refusals[`${method} ${action}`]
    if (refusal !== undefined) {
      return problem(route, refusal)
    }

    switch (`${method} ${action}`) {
      case 'GET dnssec':
        return json(route, state.dnssec)
      case 'POST dnssec/enable':
        state.dnssec = { ...SIGNED_DNSSEC }
        follow()
        return json(route, state.dnssec)
      case 'POST dnssec/withdraw':
        state.dnssec = { ...WITHDRAWING_EARLY_DNSSEC }
        follow()
        return json(route, state.dnssec)
      case 'POST dnssec/finish-withdrawal':
        if ((body as { dsRecordRemoved?: boolean } | undefined)?.dsRecordRemoved !== true) {
          return problem(route, 'Confirm that the DS record was removed at the registrar.')
        }
        state.dnssec = { ...OFF_DNSSEC }
        follow()
        return json(route, state.dnssec)
      default:
        return route.fulfill({ status: 404 })
    }
  })

  return state
}

/**
 * Signs a person in and installs both the DNS stubs and the DNSSEC stub.
 * @param page The Playwright page under test.
 * @param dnssec What the panel holds for DNSSEC.
 * @param zones What the panel holds for zones and the rest of DNS.
 * @param user The signed-in user; the administrator when omitted.
 * @returns Both live states.
 */
export const openDnsDnssecScreen = async (
  page: Page,
  dnssec: Partial<DnsDnssecStubState> = {},
  zones: Partial<DnsStubState> = {},
  user?: AuthenticatedUser,
): Promise<{ dns: DnsStubState; dnssec: DnsDnssecStubState }> => {
  const dns = await openDnsScreen(page, zones, user)
  return { dns, dnssec: await stubDnsDnssec(page, dns, dnssec) }
}
