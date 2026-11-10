import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { AccountFeatureName } from '../../src/types/accountFeature'
import type {
  ChangeSubUserFeaturesRequest,
  GrantableFeature,
  InviteSubUserRequest,
  OfferMembershipRequest,
  SubUser,
} from '../../src/types/subUser'

/** The collection endpoint the list and the invite form both talk to, with or without a query. */
const SUB_USERS_PATTERN = /\/api\/v1\/me\/sub-users(\?.*)?$/

/** The grantable-features endpoint. */
const FEATURES_PATTERN = /\/api\/v1\/me\/sub-users\/features(\?.*)?$/

/** The single sub-user endpoint, which answers the removal. */
const SUB_USER_PATTERN = '**/api/v1/me/sub-users/*'

/** The features endpoint of one sub-user. A `*` never spans a `/`, so this is narrower than the one above. */
const SUB_USER_FEATURES_PATTERN = '**/api/v1/me/sub-users/*/features'

/** The resend endpoint of one sub-user. */
const SUB_USER_INVITATION_PATTERN = '**/api/v1/me/sub-users/*/invitation'

/** The endpoint that offers membership to an existing login. */
const EXISTING_PATTERN = '**/api/v1/me/sub-users/existing'

/** The id the stub gives a freshly invited person. */
const INVITED_ID = '99999999-9999-4999-8999-999999999999'

/** The id the stub gives a freshly stored offer. */
const OFFER_ID = '88888888-8888-4888-8888-888888888888'

/** What a spec may arrange about the existing-login flow. */
export interface SubUsersStubOptions {
  /** The identifiers that name a real, offerable login; every other identifier is a silent no-op. */
  knownLogins?: string[]
  /** When set, an offer is refused with 409 and this sentence in `detail`, as a full team would be. */
  offerRefusal?: string
  /** When set, a resend is refused with 409 and this sentence in `detail`. */
  resendRefusal?: string
}

/** What the page sent, recorded in order. */
export interface SubUsersSent {
  /** The invitations posted. */
  invitations: InviteSubUserRequest[]
  /** The feature changes put. */
  changes: ChangeSubUserFeaturesRequest[]
  /** The ids deleted (a removal or a cancelled offer). */
  removals: string[]
  /** The offers posted to `existing`. */
  offers: OfferMembershipRequest[]
  /** The ids whose invitation was resent. */
  resends: string[]
}

/** The features the panel offers, as it would label them in English. */
export const stubbedFeatures: GrantableFeature[] = [
  { feature: 'mail', featureDisplayName: 'Mail' },
  { feature: 'databases', featureDisplayName: 'Databases' },
  { feature: 'cron', featureDisplayName: 'Cron jobs' },
  { feature: 'backups', featureDisplayName: 'Backups' },
]

/** How the sub-users endpoint searches and sorts, as the panel does: login and address. */
const SUB_USER_RULES: PagedRules<SubUser> = {
  matches: (user, term) => {
    return user.username.toLowerCase().includes(term) || user.email.toLowerCase().includes(term)
  },
  sorts: {
    username: (user) => {
      return user.username
    },
    email: (user) => {
      return user.email
    },
    createdAt: (user) => {
      return user.createdAt
    },
  },
  filters: {},
}

/**
 * Derives a stable identity in the UUID shape the contract demands from a login name, so every
 * sub-user of a spec has its own id and the same name always has the same one.
 * @param username The login name.
 * @returns A version-4-shaped UUID.
 */
const subUserIdFor = (username: string): string => {
  // Two 32-bit FNV-1a runs with different seeds give twelve hex digits, enough to tell a spec's few
  // sub-users apart.
  const run = (seed: number): string => {
    let hash = seed
    for (const character of username) {
      hash = Math.imul(hash ^ (character.codePointAt(0) ?? 0), 0x01000193) >>> 0
    }
    return hash.toString(16).padStart(8, '0')
  }
  return `5b000000-0000-4000-8000-${run(0x811c9dc5)}${run(0x2545f491).slice(0, 4)}`
}

/**
 * Builds a sub-user row for a spec.
 * @param username The login name.
 * @param features The feature ids the person holds.
 * @param state Whether they have accepted their invitation.
 * @returns The row.
 */
export const subUserRow = (username: string, features: AccountFeatureName[], state: SubUser['state'] = 'active'): SubUser => {
  return {
    id: subUserIdFor(username),
    username,
    email: `${username}@example.com`,
    state,
    features,
    createdAt: '2026-11-01T10:00:00Z',
    lastLoginAt: state === 'active' ? '2026-11-02T08:00:00Z' : null,
    isOffer: false,
  }
}

/**
 * Builds a pending-offer row for a spec: invited, never signed in, and flagged as an offer.
 * @param identifier What the owner typed, which is all an offer row shows.
 * @param features The feature ids on offer.
 * @returns The row.
 */
export const offerRow = (identifier: string, features: AccountFeatureName[] = []): SubUser => {
  return { ...subUserRow(identifier, features, 'invited'), email: identifier, isOffer: true }
}

/**
 * Fulfils the whole sub-users surface from one catalogue: the paged list, the feature labels, an
 * invite (the new person joins the catalogue as `invited`, so the page's re-read shows them), a
 * feature change, a resend, an offer to an existing login and a removal. Every write is recorded so
 * a spec can assert what the page sent. An offer answers 202 with the same body whether or not the
 * identifier is in `knownLogins`; only a known one adds a row, as the real panel does.
 * @param page The Playwright page whose network the routes are installed on.
 * @param users The sub-users the stub knows, in their default order.
 * @param options What the stub should arrange about offers and resends.
 * @returns What the page sent, in order.
 */
export const stubSubUsers = async (
  page: Page,
  users: SubUser[],
  options: SubUsersStubOptions = {},
): Promise<SubUsersSent> => {
  const sent: SubUsersSent = { invitations: [], changes: [], removals: [], offers: [], resends: [] }

  await page.route(SUB_USERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as InviteSubUserRequest
      sent.invitations.push(submitted)
      const created: SubUser = {
        ...subUserRow(submitted.username, submitted.features, 'invited'),
        id: INVITED_ID,
        email: submitted.email,
      }
      users.push(created)
      // The real answer is 202 with an echo of the input, identical for a taken name (issue #341).
      await route.fulfill({
        status: 202,
        contentType: 'application/json',
        body: JSON.stringify({ username: submitted.username, email: submitted.email }),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(users, route.request().url(), SUB_USER_RULES)),
    })
  })

  await page.route(SUB_USER_PATTERN, async (route) => {
    if (route.request().method() !== 'DELETE') {
      await route.fallback()
      return
    }
    const id = new URL(route.request().url()).pathname.split('/').slice(-1)[0] ?? ''
    sent.removals.push(id)
    const index = users.findIndex((user) => {
      return user.id === id
    })
    if (index >= 0) {
      users.splice(index, 1)
    }
    await route.fulfill({ status: 204 })
  })

  await page.route(SUB_USER_FEATURES_PATTERN, async (route) => {
    if (route.request().method() !== 'PUT') {
      await route.fallback()
      return
    }
    const id = new URL(route.request().url()).pathname.split('/').slice(-2)[0] ?? ''
    const submitted = route.request().postDataJSON() as ChangeSubUserFeaturesRequest
    sent.changes.push(submitted)
    const target = users.find((user) => {
      return user.id === id
    })
    if (target === undefined) {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SubUserNotFound', title: 'Not found', detail: 'That sub-user does not exist.' }),
      })
      return
    }
    target.features = submitted.features
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(target) })
  })

  await page.route(SUB_USER_INVITATION_PATTERN, async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').slice(-2)[0] ?? ''
    sent.resends.push(id)
    if (options.resendRefusal !== undefined) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SubUserNotPending', title: 'Conflict', detail: options.resendRefusal }),
      })
      return
    }
    await route.fulfill({ status: 204 })
  })

  await page.route(EXISTING_PATTERN, async (route) => {
    const submitted = route.request().postDataJSON() as OfferMembershipRequest
    sent.offers.push(submitted)
    if (options.offerRefusal !== undefined) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SubUserLimitReached', title: 'Conflict', detail: options.offerRefusal }),
      })
      return
    }
    if (options.knownLogins?.includes(submitted.identifier) === true) {
      users.push({ ...offerRow(submitted.identifier, submitted.features), id: OFFER_ID })
    }
    // The real answer is 202 with an echo of the input, identical for every identifier.
    await route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify({ identifier: submitted.identifier }) })
  })

  // Registered last, so it wins over the removal's `*` segment, which would otherwise also match
  // the word "features" in `/me/sub-users/features`.
  await page.route(FEATURES_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(stubbedFeatures) })
  })

  return sent
}

/**
 * Answers the whole surface with 403 and the panel's own sentence, as it does for a team member and
 * for an administrator, so a spec can assert the page renders it verbatim and offers no form.
 * @param page The Playwright page whose network the routes are installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the routes are installed.
 */
export const stubSubUsersForbidden = async (page: Page, detail: string): Promise<void> => {
  await page.route(/\/api\/v1\/me\/sub-users(\/.*)?(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'SubUsersOwnerOnly', title: 'Forbidden', detail }),
    })
  })
}
