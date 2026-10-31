import { expect, test, type Locator, type Page, type Request } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { RUNNING_STREAM_PATTERN, TASK_STREAM_PATTERN, stubTasks } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

/** The running-tasks read the shell used before issue #274; nothing may call it any more. */
const RUNNING_READ_PATTERN = /\/api\/v1\/tasks\/running(\?.*)?$/

/**
 * Builds one running task.
 * @param n Distinguishes the task's id and subject.
 * @returns The task.
 */
const runningTask = (n: number): PanelTask => {
  return {
    id: `0000000${n}-0000-0000-0000-000000000000`,
    kind: 'BackupCreate',
    subject: `site-${n}.example.com`,
    correlationId: null,
    status: 'running',
    percent: 10 * n,
    log: '',
    logIncluded: true,
    errorCode: null,
    startedAt: '2026-10-31T09:00:00Z',
    finishedAt: null,
    revision: 1,
  }
}

/** Three tasks in flight at once: N > 1 is the whole point. */
const RUNNING: PanelTask[] = [runningTask(1), runningTask(2), runningTask(3)]

/**
 * The header badge, found by the accessible name it carries rather than by a class.
 * @param page The page.
 * @returns The badge.
 */
const badge = (page: Page): Locator => {
  return page.getByRole('link', { name: /background tasks? running/i })
}

/** What the browser asked for while a spec ran. */
interface Traffic {
  /** Every request to the running-tasks stream, oldest first. */
  tray: Request[]
  /** How many running-tasks streams were open at the same moment, at most. */
  maxOpenTrays: number
  /** Requests to a per-task stream. */
  perTask: number
  /** Requests to the old running-tasks read. */
  runningReads: number
}

/**
 * Records the task traffic of a page: every tray request, how many were open at once, and any
 * per-task stream or running read.
 * @param page The page to observe.
 * @returns The live record.
 */
const observeTraffic = (page: Page): Traffic => {
  const traffic: Traffic = { tray: [], maxOpenTrays: 0, perTask: 0, runningReads: 0 }
  let open = 0

  /**
   * Counts one tray connection as closed.
   * @param request The request that ended.
   * @returns Nothing.
   */
  const closed = (request: Request): void => {
    if (RUNNING_STREAM_PATTERN.test(request.url())) {
      open -= 1
    }
  }

  page.on('request', (request) => {
    const url = request.url()
    if (RUNNING_STREAM_PATTERN.test(url)) {
      traffic.tray.push(request)
      open += 1
      traffic.maxOpenTrays = Math.max(traffic.maxOpenTrays, open)
    } else if (TASK_STREAM_PATTERN.test(url)) {
      traffic.perTask += 1
    } else if (RUNNING_READ_PATTERN.test(url)) {
      traffic.runningReads += 1
    }
  })
  page.on('requestfinished', closed)
  page.on('requestfailed', closed)
  return traffic
}

/**
 * Signs in and stubs the shell and the three running tasks.
 * @param page The page.
 * @returns The traffic record.
 */
const stubShell = async (page: Page): Promise<Traffic> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, RUNNING)
  return observeTraffic(page)
}

// THE proposition of issue #274: the browser's connections no longer grow with the work in flight.
// With three tasks running, the badge reaches three from ONE tray request, and no per-task stream
// and no running read is made. Before the change this page opened three per-task streams.
//
// The clock is frozen so the stub's stream, which ends as soon as its body is sent, is not
// reconnected behind the count; running it afterwards shows the reconnect does happen and is still
// never a second concurrent connection.
test('with three running tasks the shell opens exactly one running-tasks stream', async ({ page }) => {
  await page.clock.install()
  const traffic = await stubShell(page)

  await page.goto('/')

  await expect(badge(page)).toHaveText('3')
  expect(traffic.tray).toHaveLength(1)
  expect(traffic.perTask).toBe(0)
  expect(traffic.runningReads).toBe(0)

  // Positive control for the counter: a reconnect is a request it sees.
  await page.clock.runFor(1_000)
  await expect.poll(() => {
    return traffic.tray.length
  }).toBe(2)
  await expect(badge(page)).toHaveText('3')
  expect(traffic.maxOpenTrays).toBe(1)
  expect(traffic.perTask).toBe(0)
})

// The panel names each subscriber's frames in the language the stream was opened in, so a switch of
// language must reopen the tray at once, asking in the new language — not wait for a reconnect,
// which the frozen clock rules out here.
test('switching the language reopens the running-tasks stream in the new language', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.clock.install()
  const traffic = await stubShell(page)
  await page.goto('/')
  await expect(badge(page)).toHaveText('3')

  const russianTray = page.waitForRequest((request): boolean => {
    return RUNNING_STREAM_PATTERN.test(request.url()) && request.headers()['accept-language']?.startsWith('ru') === true
  })
  await page.getByRole('banner').getByTestId('locale-switcher').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: 'Русский', exact: true }).click()
  await russianTray

  expect(traffic.tray).toHaveLength(2)
  expect(traffic.maxOpenTrays).toBe(1)
})

// Signing out tears the shell down, and the tray with it: no stream, and no reconnect waiting to
// happen, survives on the sign-in screen. The page clock is taken over once the shell is up and run
// a minute on, past every reconnect wait.
test('signing out closes the running-tasks stream and nothing reconnects it', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const traffic = await stubShell(page)
  await page.route('**/api/v1/auth/logout', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  await page.goto('/')
  await expect(badge(page)).toHaveText('3')
  await page.clock.install()
  await page.getByRole('complementary').getByRole('button', { name: 'Sign out', exact: true }).click()
  await expect(page).toHaveURL('/login')

  const before = traffic.tray.length
  await page.clock.runFor(60_000)
  expect(traffic.tray).toHaveLength(before)
})
