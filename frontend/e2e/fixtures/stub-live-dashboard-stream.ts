import type { Page } from '@playwright/test'
import type { DashboardLayout } from '../../src/types/dashboardLayout'
import type {
  LiveActivity,
  LiveHello,
  LiveMetricsPoint,
  LiveTotals,
} from '../../src/types/liveDashboard'
import { ALERT_DELIVERY_OK, loaded, SYNCHRONIZED_CLOCK } from './stub-dashboard-route'

/*
 * A controllable stand-in for the dashboard's live stream.
 *
 * Why not `page.route`: `route.fulfill` sends a whole body at once and then ends the response, so
 * it cannot show a value changing while the connection stays open, nor a connection that drops and
 * comes back. The stand-in is a replacement `fetch` installed before the page's scripts run: a
 * request for `/api/v1/dashboard/stream` gets a `Response` over a `ReadableStream` the test pushes
 * frames into, ends, or leaves open.
 *
 * BLIND SPOT, stated where it is used: no server is involved, so this proves what the SPA does
 * with frames (drawing, reconnecting, pausing, back-filling) and nothing about the panel's side of
 * the stream (framing on the wire, the lifetime cap, the per-user limit, tenant scoping). Those are
 * the backend integration tests' job, and the owner-run Playwright pass against a real panel.
 */

/** The harness the init script leaves on `window`, as the test drives it. */
interface LiveStreamHarness {
  /** The query string (`?since=…` or empty) of every stream request, in order. */
  opens: string[]
  /** Writes one frame to the stream that is currently open. */
  push: (name: string, data: unknown) => void
  /** Ends the currently open stream cleanly, as a server closing the connection does. */
  end: () => void
}

/**
 * Installs the stand-in. Call before `page.goto`.
 * @param page The Playwright page to install it on.
 * @returns Resolves once the init script is registered.
 */
export const installLiveStream = async (page: Page): Promise<void> => {
  await page.addInitScript(() => {
    const encoder = new TextEncoder()
    // Read and written through Reflect: the lint rules forbid `window.fetch` outside `useApi.ts`,
    // and a spec replacing the global is how the real client is exercised without a server.
    const realFetch = Reflect.get(window, 'fetch')
    let current: ReadableStreamDefaultController<Uint8Array> | null = null

    const harness: LiveStreamHarness = {
      opens: [],
      push: (name, data) => {
        current?.enqueue(encoder.encode(`event: ${name}\ndata: ${JSON.stringify(data)}\n\n`))
      },
      end: () => {
        current?.close()
        current = null
      },
    }
    ;(window as unknown as { __liveStream: LiveStreamHarness }).__liveStream = harness

    const stand: typeof fetch = (input, init) => {
      const raw = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      const url = new URL(raw, window.location.href)
      if (!url.pathname.endsWith('/api/v1/dashboard/stream')) {
        return realFetch.call(window, input, init)
      }

      harness.opens.push(url.search)
      const body = new ReadableStream<Uint8Array>({
        start: (controller): void => {
          current = controller
          // A real aborted fetch rejects the pending read; the stand-in must too, or the page's
          // reader would wait forever on a connection it closed.
          init?.signal?.addEventListener('abort', () => {
            try {
              controller.error(new DOMException('Aborted', 'AbortError'))
            } catch {
              // Already closed by the test: nothing left to abort.
            }
          })
        },
      })
      return Promise.resolve(
        new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
      )
    }
    Reflect.set(window, 'fetch', stand)
  })
}

/**
 * Writes one frame to the open stream.
 * @param page The page the stand-in is installed on.
 * @param name The event name (`hello`, `history`, `metrics`, `services`, `totals`, `activity`, `end`).
 * @param data The payload, serialised as JSON.
 * @returns Resolves once the frame is written.
 */
export const pushFrame = async (page: Page, name: string, data: unknown): Promise<void> => {
  await page.evaluate(
    ([frame, payload]) => {
      ;(window as unknown as { __liveStream: LiveStreamHarness }).__liveStream.push(frame, payload)
    },
    [name, data] as const,
  )
}

/**
 * Ends the open stream as a server closing the connection does (the page then reconnects).
 * @param page The page the stand-in is installed on.
 * @returns Resolves once the stream is closed.
 */
export const endStream = async (page: Page): Promise<void> => {
  await page.evaluate(() => {
    ;(window as unknown as { __liveStream: LiveStreamHarness }).__liveStream.end()
  })
}

/**
 * Reads the query string of every stream request the page has made.
 * @param page The page the stand-in is installed on.
 * @returns The queries, oldest first (`''` for a request without `since`).
 */
export const streamOpens = async (page: Page): Promise<string[]> => {
  return page.evaluate(() => {
    return (window as unknown as { __liveStream: LiveStreamHarness }).__liveStream.opens
  })
}

/** The `hello` frame of an administrator's stream. */
export const HELLO: LiveHello = {
  protocol: 1,
  audience: 'administrator',
  intervalMs: 2000,
  serverTime: '2026-11-07T10:00:00+00:00',
  maxLifetimeSec: 900,
}

/**
 * Builds a host reading, with values no default could coincide with.
 * @param secondsAfterStart How many seconds after 10:00:00 the reading was taken.
 * @param overrides The members to set; the rest are fixed, distinct figures.
 * @returns The reading.
 */
export const reading = (secondsAfterStart: number, overrides: Partial<LiveMetricsPoint> = {}): LiveMetricsPoint => {
  const GIB = 1024 * 1024 * 1024
  const instant = new Date(Date.UTC(2026, 10, 7, 10, 0, secondsAfterStart)).toISOString()
  return {
    t: instant,
    cpu: { total: 37.4, load: [0.21, 0.34, 0.55] },
    memory: { used: 3 * GIB, total: 8 * GIB },
    disk: { used: 44 * GIB, total: 98 * GIB },
    network: { rxBps: 3 * 1024, txBps: 1024 },
    ...overrides,
  }
}

/**
 * Builds the `totals` frame.
 * @param overrides The members to set; the rest hold a server with 3 accounts, 7 sites and 4 databases and nothing needing attention.
 * @returns The frame.
 */
export const totals = (overrides: Partial<LiveTotals> = {}): LiveTotals => {
  return {
    counts: { accounts: loaded(3), sites: loaded(7), databases: loaded(4) },
    attention: {
      certificatesExpiringSoon: loaded(0),
      failedBackups: loaded(0),
      sitesOnEndOfLifePhp: loaded(0),
      licenceExpiring: loaded(0),
      alertDelivery: ALERT_DELIVERY_OK,
      bannedAddresses: loaded(0),
      failedTasks: loaded(0),
      clock: SYNCHRONIZED_CLOCK,
    },
    ...overrides,
  }
}

/** The `activity` frame every spec sends unless it is about activity: one failed sign-in. */
export const ACTIVITY: LiveActivity = {
  recentAudit: loaded([
    {
      id: '33333333-3333-3333-3333-333333333333',
      occurredAt: '2026-11-07T09:58:00+00:00',
      actorUsername: 'admin',
      impersonatorUsername: null,
      action: 'SignInFailed',
      actionName: 'Sign-in failed',
      subject: 'admin',
      subjectName: 'admin',
      ipAddress: '203.0.113.9',
      succeeded: false,
      outcomeName: 'Failed',
    },
  ]),
}

/** The layout the panel answers a fresh administrator with: every widget and shortcut, in the panel's order. */
export const DEFAULT_LAYOUT: DashboardLayout = {
  widgets: [
    { key: 'attention', hidden: false },
    { key: 'firstRun', hidden: false },
    { key: 'cpu', hidden: false },
    { key: 'memory', hidden: false },
    { key: 'disk', hidden: false },
    { key: 'network', hidden: false },
    { key: 'services', hidden: false },
    { key: 'totals', hidden: false },
    { key: 'activity', hidden: false },
  ],
  actions: [
    { key: 'createAccount', hidden: false },
    { key: 'addSite', hidden: false },
    { key: 'createDatabase', hidden: false },
    { key: 'runBackup', hidden: false },
    { key: 'banAddress', hidden: false },
    { key: 'addCronJob', hidden: false },
  ],
}

/** What {@link stubDashboardLayout} lets a spec read back. */
export interface DashboardLayoutStub {
  /** Every body the page PUT, oldest first. */
  saved: DashboardLayout[]
}

/**
 * Fulfils `GET` and `PUT /api/v1/me/dashboard-layout`: a PUT replaces what the next GET answers,
 * as the panel does, and is recorded for the spec to assert on.
 * @param page The Playwright page whose network the route is installed on.
 * @param initial What the panel holds to begin with.
 * @returns The recorder.
 */
export const stubDashboardLayout = async (page: Page, initial: DashboardLayout): Promise<DashboardLayoutStub> => {
  const stub: DashboardLayoutStub = { saved: [] }
  let held = initial

  await page.route(/\/api\/v1\/me\/dashboard-layout(\?.*)?$/, async (route) => {
    if (route.request().method() === 'PUT') {
      held = route.request().postDataJSON() as DashboardLayout
      stub.saved.push(held)
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(held) })
  })

  return stub
}

/**
 * Answers what an administrator's dashboard opens besides its one-shot read: the layout the panel
 * holds (the panel's default) and a live stream that stays open without ever delivering a frame, so
 * the one-shot figures remain what the screen shows. A spec about the stream itself installs its own
 * stand-in and pushes frames; a spec about anything else only needs the reads answered.
 * @param page The Playwright page the reads are answered on.
 * @returns The recorder of layout saves.
 */
export const stubIdleLiveDashboard = async (page: Page): Promise<DashboardLayoutStub> => {
  await installLiveStream(page)
  return stubDashboardLayout(page, DEFAULT_LAYOUT)
}
