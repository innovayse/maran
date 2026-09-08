import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubTaskStream, stubTasks } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

// `PanelTask.percent` is an integer the module clamps to 0-100 and never omits, so the Progress
// column always carries a percentage and has to say so. The column used to render a bare `100`,
// and every assertion in the suite that touched it used `toContainText`, which passes on the bare
// number and on the fixed cell alike. This spec reads the CELL'S WHOLE TEXT instead.

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

/** A finished task, whose percent the module sets to 100 when it closes the row. */
const DONE: PanelTask = {
  id: '33333333-3333-3333-3333-333333333333',
  kind: 'BackupCreate',
  subject: 'alice',
  correlationId: null,
  status: 'completed',
  percent: 100,
  log: 'done\n',
  errorCode: null,
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: '2026-09-03T09:04:00Z',
  revision: 7,
}

/** A task still running, so a mid-range value is checked as well as the boundary. */
const RUNNING: PanelTask = {
  id: '44444444-4444-4444-4444-444444444444',
  kind: 'BackupRestore',
  subject: 'bob',
  correlationId: null,
  status: 'running',
  percent: 40,
  log: 'restoring\n',
  errorCode: null,
  startedAt: '2026-09-03T09:10:00Z',
  finishedAt: null,
  revision: 3,
}

test('the progress column names its unit rather than printing a bare number', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [DONE, RUNNING])
  await stubTaskStream(page, '')

  await page.goto('/tasks')

  // Positive control: the table and both rows are on screen, so the cell assertions below are
  // reading rendered rows rather than passing against an empty page.
  await expect(page.getByRole('table')).toBeVisible()
  await expect(page.getByRole('row')).toHaveCount(3)

  const doneRow = page.getByRole('row').filter({ hasText: DONE.subject })
  const runningRow = page.getByRole('row').filter({ hasText: RUNNING.subject })

  // The whole cell text, not a substring: `100` is contained in `100%`, so a containment check
  // cannot tell the defect from the fix.
  await expect(doneRow.getByRole('cell').nth(3)).toHaveText('100%')
  await expect(runningRow.getByRole('cell').nth(3)).toHaveText('40%')
})
