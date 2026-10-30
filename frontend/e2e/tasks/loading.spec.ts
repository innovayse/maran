import { test, type Page } from '@playwright/test'
import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubTasks, stubTasksNotFound } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

// The tasks table while the listing is on the way (#243): drawn from its own column schema.

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

const INSTALL: PanelTask = {
  id: '33333333-3333-3333-3333-333333333333',
  kind: 'FutureUnlistedKind',
  subject: 'php8.4',
  correlationId: 'c0rr-3l4t10n',
  status: 'running',
  percent: 40,
  log: 'fetching packages\n',
  logIncluded: true,
  errorCode: null,
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: null,
  revision: 5,
}

/**
 * Signs in with the tasks module licensed.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
}

/** How the tasks list screen is opened and answered. */
const TASKS_LIST_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubTasks(page, [INSTALL])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubTasksNotFound(page, 'The tasks could not be read.')
  },
  pattern: /\/api\/v1\/tasks(\?.*)?$/,
  path: '/tasks',
  table: 'Background tasks',
  rowText: 'php8.4',
  failureText: 'The tasks could not be read.',
}

test('tasks list: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, TASKS_LIST_SCENARIO)
})

test('tasks list: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, TASKS_LIST_SCENARIO)
})

test('tasks list: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, TASKS_LIST_SCENARIO)
})
