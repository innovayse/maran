import type { Page } from '@playwright/test'
import type { FirstRunChecklist } from '../../src/types/firstRun'
import type { PanelNotification } from '../../src/types/notification'

/** The feed endpoints: the list, the badge figure, a dismissal and dismiss-all. */
const FEED_PATTERN = /\/api\/v1\/notifications\/feed(\/[^?]*)?(\?.*)?$/

/** The checklist endpoint the dashboard's first-run card reads. */
const FIRST_RUN_PATTERN = /\/api\/v1\/dashboard\/first-run(\?.*)?$/

/** The most the badge figure reports before it says "more", as the panel caps it. */
const COUNT_CAP = 99

/** What the stubbed panel holds for the signed-in administrator's feed. */
export interface NotificationFeedState {
  /** The entries the administrator has not dismissed, newest first; dismissals remove from it. */
  entries: PanelNotification[]
  /** Ids the SPA asked to dismiss, in order, so a spec can assert what was sent. */
  dismissed: string[]
  /** How many times the whole feed was dismissed. */
  dismissAllCalls: number
  /** When set, a dismissal is refused with this localized message (a 409 problem). */
  refuseDismissWith: string | null
  /** When set, reading the list fails with this status (the badge figure still answers). */
  failListWith: number | null
}

/**
 * Builds one feed entry, defaulting everything a spec does not care about.
 * @param overrides The fields the spec names.
 * @returns The entry.
 */
export const feedEntry = (overrides: Partial<PanelNotification> & { id: string }): PanelNotification => {
  return {
    source: 'task',
    severity: 'critical',
    title: 'A task failed',
    detail: null,
    deepLink: null,
    occurredAt: '2026-11-06T10:00:00+00:00',
    ...overrides,
  }
}

/**
 * Builds the state of a feed holding the given entries.
 * @param entries The entries, newest first.
 * @returns The state, ready for {@link stubNotificationFeed}.
 */
export const feedState = (entries: PanelNotification[]): NotificationFeedState => {
  return { entries, dismissed: [], dismissAllCalls: 0, refuseDismissWith: null, failListWith: null }
}

/**
 * Fulfils the feed the way the panel does: newest first, the badge figure capped at 99 with a flag,
 * a dismissal removing the entry (idempotent for an unknown id) and dismiss-all removing all.
 * @param page The Playwright page whose network the route is installed on.
 * @param state What the panel holds; mutated by dismissals.
 * @returns Resolves once the route is installed.
 */
export const stubNotificationFeed = async (page: Page, state: NotificationFeedState): Promise<void> => {
  await page.route(FEED_PATTERN, async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname.replace('/api/v1/notifications/feed', '')
    const json = async (status: number, body: unknown, contentType = 'application/json'): Promise<void> => {
      await route.fulfill({ status, contentType, body: JSON.stringify(body) })
    }

    if (request.method() === 'POST' && path === '/dismiss-all') {
      state.dismissAllCalls += 1
      state.entries = []
      await route.fulfill({ status: 204 })
      return
    }

    const dismissal = /^\/([^/]+)\/dismiss$/.exec(path)
    if (request.method() === 'POST' && dismissal !== null) {
      if (state.refuseDismissWith !== null) {
        await json(409, { title: state.refuseDismissWith, detail: state.refuseDismissWith, status: 409 }, 'application/problem+json')
        return
      }
      const id = decodeURIComponent(dismissal[1] ?? '')
      state.dismissed.push(id)
      state.entries = state.entries.filter((entry) => {
        return entry.id !== id
      })
      await route.fulfill({ status: 204 })
      return
    }

    if (path === '/unread-count') {
      await json(200, { count: Math.min(state.entries.length, COUNT_CAP), capped: state.entries.length > COUNT_CAP })
      return
    }

    if (state.failListWith !== null) {
      await json(state.failListWith, { title: 'The feed could not be read.', detail: 'The feed could not be read.', status: state.failListWith }, 'application/problem+json')
      return
    }
    const query = new URL(request.url()).searchParams
    const page = Number(query.get('page') ?? '1')
    const pageSize = Number(query.get('pageSize') ?? '25')
    await json(200, {
      items: state.entries.slice((page - 1) * pageSize, page * pageSize),
      total: state.entries.length,
      page,
      pageSize,
    })
  })
}

/**
 * Answers the feed with nothing in it: the quiet panel the shell's other specs run against, so the
 * bell's own reads do not log a failed request.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
export const stubQuietNotificationFeed = async (page: Page): Promise<void> => {
  await stubNotificationFeed(page, feedState([]))
}

/** A checklist whose every step is done, which the dashboard draws no card for. */
export const COMPLETE_CHECKLIST: FirstRunChecklist = {
  complete: true,
  steps: [
    { key: 'plan', done: { state: 'loaded', data: true }, routeName: 'plans' },
    { key: 'account', done: { state: 'loaded', data: true }, routeName: 'accounts' },
    { key: 'site', done: { state: 'loaded', data: true }, routeName: 'sites' },
    { key: 'backupDestination', done: { state: 'loaded', data: true }, routeName: 'backupDestinations' },
    { key: 'certificate', done: { state: 'loaded', data: true }, routeName: 'certificates' },
  ],
}

/**
 * Fulfils the first-run checklist with a fixed answer.
 * @param page The Playwright page whose network the route is installed on.
 * @param checklist What the panel answers.
 * @returns Resolves once the route is installed.
 */
export const stubFirstRun = async (page: Page, checklist: FirstRunChecklist): Promise<void> => {
  await page.route(FIRST_RUN_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(checklist) })
  })
}
