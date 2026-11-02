import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { ChangeSubUserFeaturesRequest, GrantableFeature, InviteSubUserRequest, SubUser } from '../../src/types/subUser'

/** The collection endpoint the list and the invite form both talk to, with or without a query. */
const SUB_USERS_PATTERN = /\/api\/v1\/me\/sub-users(\?.*)?$/

/** The grantable-features endpoint. */
const FEATURES_PATTERN = /\/api\/v1\/me\/sub-users\/features(\?.*)?$/

/** The single sub-user endpoint, which answers the removal. */
const SUB_USER_PATTERN = '**/api/v1/me/sub-users/*'

/** The features endpoint of one sub-user. A `*` never spans a `/`, so this is narrower than the one above. */
const SUB_USER_FEATURES_PATTERN = '**/api/v1/me/sub-users/*/features'

/** The id the stub gives a freshly invited person. */
const INVITED_ID = '99999999-9999-4999-8999-999999999999'

/** The features the panel offers, as it would label them in English. */
export const stubbedFeatures: GrantableFeature[] = [
  { id: 'mail', label: 'Mail' },
  { id: 'databases', label: 'Databases' },
  { id: 'cron', label: 'Cron jobs' },
  { id: 'backups', label: 'Backups' },
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
 * Builds a sub-user row for a spec.
 * @param username The login name.
 * @param features The feature ids the person holds.
 * @param state Whether they have accepted their invitation.
 * @returns The row.
 */
export const subUserRow = (username: string, features: string[], state: SubUser['state'] = 'active'): SubUser => {
  return {
    id: `00000000-0000-4000-8000-${username.padStart(12, '0').slice(-12)}`,
    username,
    email: `${username}@example.com`,
    state,
    features,
    createdAt: '2026-11-01T10:00:00Z',
    lastLoginAt: state === 'active' ? '2026-11-02T08:00:00Z' : null,
  }
}

/**
 * Fulfils the whole sub-users surface from one catalogue: the paged list, the feature labels, an
 * invite (the new person joins the catalogue as `invited`, so the page's re-read shows them), a
 * feature change and a removal. Every write is recorded so a spec can assert what the page sent.
 * @param page The Playwright page whose network the routes are installed on.
 * @param users The sub-users the stub knows, in their default order.
 * @returns What the page sent: the invitations and the feature changes, in order.
 */
export const stubSubUsers = async (
  page: Page,
  users: SubUser[],
): Promise<{ invitations: InviteSubUserRequest[]; changes: ChangeSubUserFeaturesRequest[]; removals: string[] }> => {
  const sent = {
    invitations: [] as InviteSubUserRequest[],
    changes: [] as ChangeSubUserFeaturesRequest[],
    removals: [] as string[],
  }

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
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(created) })
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
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
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
