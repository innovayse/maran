import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { Session } from '../../src/types/auth'

/** The caller's sessions list, with or without a query. The DELETE of one row has a path segment more. */
export const SESSIONS_LIST_PATTERN = /\/api\/v1\/sessions(\?.*)?$/

/** How the sessions endpoint searches and sorts, as the panel does: address and user agent, two instants. */
const SESSION_RULES: PagedRules<Session> = {
  matches: (session, term) => {
    return session.ipAddress.toLowerCase().includes(term) || session.userAgent.toLowerCase().includes(term)
  },
  sorts: {
    issuedAt: (session) => {
      return session.issuedAt
    },
    expiresAt: (session) => {
      return session.expiresAt
    },
  },
  filters: {},
}

/**
 * Fulfils `GET /api/v1/sessions` as a page honouring `page`, `pageSize`, `search` and `sort`, and
 * hands every other method on, so a spec's own DELETE stub still answers. The list is read on every
 * request, so a spec that removes a row from it sees the next read answer without that row — which
 * is what the panel does after a revocation.
 * @param page The Playwright page whose network the route is installed on.
 * @param sessions The devices the panel reports, in its default order (newest first).
 * @returns A function reporting every list request so far, oldest first.
 */
export const stubSessions = async (page: Page, sessions: Session[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []

  await page.route(SESSIONS_LIST_PATTERN, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(sessions, route.request().url(), SESSION_RULES)),
    })
  })

  return (): readonly URL[] => {
    return listRequests
  }
}

/**
 * Refuses `GET /api/v1/sessions` with one RFC 7807 problem body.
 * @param page The Playwright page whose network the route is installed on.
 * @param status The status the panel answers with.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubSessionsProblem = async (page: Page, status: number, detail: string): Promise<void> => {
  await page.route(SESSIONS_LIST_PATTERN, async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}
