import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubTaskStream, stubTasks } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

// The status word on a task is the panel's own: the row carries it, so the bundle holds no copy of
// the vocabulary (issue #330).

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** A name no bundle contains, so a badge printing it can only have read it off the row. */
const TASK: PanelTask = {
  id: '33333333-3333-3333-3333-333333333333',
  kind: 'FutureUnlistedKind',
  subject: 'php8.4',
  correlationId: null,
  status: 'completed',
  statusName: 'Panel-named done',
  percent: 100,
  log: '',
  logIncluded: true,
  failure: null,
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: '2026-09-03T09:01:00Z',
  revision: 3,
}

test('the status badge of a task prints the name the row carries', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [TASK])
  await stubTaskStream(page, '')

  await page.goto('/tasks')

  const row = page.getByRole('row').filter({ hasText: TASK.subject })
  await expect(row.getByText('Panel-named done', { exact: true })).toBeVisible()
})

test('a link naming a status the panel does not list is not applied once the list arrives', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [TASK])
  await stubTaskStream(page, '')

  await page.goto('/tasks?status=bogus')

  await expect(page.getByRole('row').filter({ hasText: TASK.subject })).toBeVisible()
  await expect(page.getByTestId('active-filter-chips')).toHaveCount(0)
})
