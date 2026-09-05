import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import {
  stubTaskStream,
  stubTasks,
  stubTasksNotFound,
  taskEndFrame,
  taskFrame,
} from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

const INSTALL: PanelTask = {
  id: '33333333-3333-3333-3333-333333333333',
  kind: 'PhpVersionInstall',
  subject: 'php8.4',
  correlationId: 'c0rr-3l4t10n',
  status: 'running',
  percent: 40,
  log: 'fetching packages\n',
  errorCode: null,
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: null,
  revision: 5,
}

test('the tasks screen shows the empty state when the panel reports no tasks', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [])

  await page.goto('/tasks')

  await expect(page.getByText('No tasks yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('the tasks table shows what each task is, what it acts on and where it has got to', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [INSTALL])
  await stubTaskStream(page, '')

  await page.goto('/tasks')

  const row = page.getByRole('row').filter({ hasText: INSTALL.kind })
  await expect(row).toContainText(INSTALL.subject)
  await expect(row).toContainText('Running')
  await expect(row).toContainText('40')
})

// The live pane is fed by the stream and by nothing else once it is open, so this is what proves
// the frames are being decoded rather than the row simply being re-rendered: the log and the
// percent in the pane come from a frame that the listing never carried. What would break it: a
// second SSE parser that mis-split frames, a pane bound to the listing's copy of the row, or a
// `merge` that dropped a frame whose revision had moved.
test('opening a task shows what its stream reports, not what the listing said', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [INSTALL])
  await stubTaskStream(
    page,
    taskFrame({
      ...INSTALL,
      percent: 90,
      log: 'fetching packages\nconfiguring\n',
      revision: 9,
    }),
  )

  await page.goto('/tasks')
  await page.getByRole('button', { name: `Watch the task for ${INSTALL.subject}` }).click()

  const pane = page.getByRole('progressbar', { name: 'Progress of this task' })
  await expect(pane).toHaveAttribute('aria-valuenow', '90')
  await expect(page.getByText('configuring')).toBeVisible()
  await expect(page.getByText(INSTALL.correlationId ?? '')).toBeVisible()
})

// A failed task carries a machine-stable code, and the panel shows it as one: it is what an
// operator quotes, never a sentence the frontend translated (rules/vue.md).
test('a task that failed shows the code it failed with', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [INSTALL])
  await stubTaskStream(
    page,
    taskFrame({
      ...INSTALL,
      status: 'failed',
      errorCode: 'SitesPhpVersionUnavailable',
      finishedAt: '2026-09-03T09:05:00Z',
      revision: 9,
    }) + taskEndFrame('failed'),
  )

  await page.goto('/tasks')
  await page.getByRole('button', { name: `Watch the task for ${INSTALL.subject}` }).click()

  await expect(page.getByText('SitesPhpVersionUnavailable', { exact: false })).toBeVisible()
  await expect(page.getByText('Failed').first()).toBeVisible()
})

// rules/vue.md: the backend owns its error text, and the module's answer to a caller this surface
// does not exist for is 404 with a message — not an empty 200. The page renders it verbatim rather
// than showing an empty state, which would say something different and untrue.
test('the tasks screen renders the panel’s refusal verbatim rather than an empty list', async ({
  page,
}) => {
  const backendDetail = 'That resource is not available.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasksNotFound(page, backendDetail)

  await page.goto('/tasks')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByText('No tasks yet')).toHaveCount(0)
})
