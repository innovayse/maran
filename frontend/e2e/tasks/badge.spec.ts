import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import {
  stubDeferredTaskStream,
  stubTaskStream,
  stubTasks,
  taskFrame,
} from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

/**
 * A task the listing reports as already finished, so the badge starts at nothing. The stream is
 * what will say otherwise: the module sends the first frame whatever the revision, so a pane opened
 * on a row the listing fetched some time ago is corrected immediately rather than staying stale.
 */
const SETTLED: PanelTask = {
  id: '33333333-3333-3333-3333-333333333333',
  kind: 'CertificateOrder',
  subject: 'alice.example.com',
  correlationId: null,
  status: 'completed',
  percent: 100,
  log: 'ordered\n',
  errorCode: null,
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: '2026-09-03T09:02:00Z',
  revision: 4,
}

/** The same task as the STREAM reports it: still running, and further along than the row said. */
const RUNNING: PanelTask = {
  ...SETTLED,
  status: 'running',
  percent: 40,
  finishedAt: null,
  revision: 5,
}

/** The header badge, found by the accessible name it carries rather than by a class. */
const badge = (page: Page): Locator => {
  return page.getByRole('link', { name: /background tasks running/i })
}

// THE proposition. The badge lives in the shell header and the task arrives on a stream the tasks
// page opened; both read the same store, so the count moves without a navigation, a reload, or a
// second fetch of its own.
//
// What would break it: a badge that counted the listing rather than the store's array; a badge with
// its own request; a page that kept stream frames in local component state instead of merging them
// into the store; a `merge` that refused to raise a task's status; or a badge rendered by the tasks
// page rather than by the shell. The "absent first" assertion is what makes the second one mean
// something — without it the test would pass on a badge that was always showing.
//
// The URL assertion is not decoration either: it is what distinguishes "the badge rose" from "the
// badge rose because the app navigated somewhere that fetched again".
test('a task arriving in the stream raises the header badge without navigating', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [SETTLED])
  await stubTaskStream(page, taskFrame(RUNNING))

  await page.goto('/tasks')
  await expect(page.getByRole('heading', { level: 1, name: 'Background tasks' })).toBeVisible()

  // Nothing is running, so nothing is drawn. A badge reading "0" is a control that is never right.
  await expect(badge(page)).toHaveCount(0)

  const urlBefore = page.url()
  await page.getByRole('button', { name: `Watch the task for ${SETTLED.subject}` }).click()

  await expect(badge(page)).toBeVisible()
  await expect(badge(page)).toHaveText('1')
  expect(page.url()).toBe(urlBefore)
})

// The other direction, and the reason the shell opens the streams at all: an operator who starts
// something long and walks away to another screen still watches the count fall when it finishes.
// This spec never visits the tasks page, so it fails if the streams were opened by that page rather
// than by the shell.
//
// The stream is HELD until the badge has been observed running. Answering it immediately would let
// the finishing frame land before the badge was ever drawn, and the final assertion would then pass
// without the fall it claims to measure ever having happened — an assertion that cannot fail.
test('the shell watches a running task from any screen and the badge falls when it ends', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [RUNNING])
  const stream = await stubDeferredTaskStream(page, taskFrame({ ...SETTLED, revision: 6 }))

  // Never /tasks: whatever opens this stream is the shell.
  await page.goto('/')
  await expect(badge(page)).toHaveText('1')

  stream.release()

  await expect(badge(page)).toHaveCount(0)
})

// The listing answers 404 to a caller the surface does not exist for, rather than an empty 200, so
// a customer is not told there is an administrator-only feed they were refused. The badge must be
// silent about that — it is not an error the shell shouts on every screen.
test('a caller the tasks surface does not exist for gets no badge and no shell error', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await page.route(/\/api\/v1\/tasks(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'TaskNotFound', detail: 'Not found.' }),
    })
  })

  await page.goto('/')

  // Named, not "some level-1 heading": the shell's own brand mark is an <h1> too, so an unnamed
  // query matches two elements and the assertion says nothing about the screen having rendered.
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()
  await expect(badge(page)).toHaveCount(0)
  await expect(page.getByRole('banner').getByText('Not found.')).toHaveCount(0)
})
