import { expect, test, type Page } from '@playwright/test'
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
  // The old stand-in, `PhpVersionInstall`, became a real, labelled kind (issue #82); this one is defined by no backend.
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
  await page.getByRole('button', { name: `Actions for ${INSTALL.subject}` }).click()
  await page.getByRole('menuitem', { name: 'Live view' }).click()

  const pane = page.getByRole('progressbar', { name: 'Progress of this task' })
  await expect(pane).toHaveAttribute('aria-valuenow', '90')
  await expect(page.getByText('configuring')).toBeVisible()
  await expect(page.getByText(INSTALL.correlationId ?? '')).toBeVisible()
})

// A failed task carries a machine-stable code. The pane leads with a sentence (#222) — the generic
// one for a code this panel has no sentence for — and keeps the code beneath it, because the code is
// what an operator quotes.
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
  await page.getByRole('button', { name: `Actions for ${INSTALL.subject}` }).click()
  await page.getByRole('menuitem', { name: 'Live view' }).click()

  await expect(page.getByText('Code: SitesPhpVersionUnavailable', { exact: true })).toBeVisible()
  await expect(page.getByText('The operation failed. The code below says why', { exact: false })).toBeVisible()
  // Exact, because `failed` is the status on the wire and `Failed` is the badge's translated label:
  // a loose `getByText` matches a substring case-insensitively and would read the raw constant as
  // the fix. Nothing else in this test observes the wording — the code assertion above it is about
  // `errorCode`, which is machine-stable and untranslated by design.
  await expect(page.getByText('Failed', { exact: true }).first()).toBeVisible()
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

// `BackupCreate` is a machine-stable name, not a sentence, so it is this panel's chrome to
// translate — the same ground the status beside it is translated on. The operator read the constant
// verbatim until this was rendered through the locale files.
test('a task kind is shown in the operator’s language, not as the name the panel stores', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [{ ...INSTALL, kind: 'BackupCreate', subject: 'acme' }])
  await stubTaskStream(page, '')

  await page.goto('/tasks')

  const row = page.getByRole('row').filter({ hasText: 'acme' })
  await expect(row).toContainText('Taking a backup')
  await expect(row).not.toContainText('BackupCreate')
})

// The kinds are string constants rather than an enum precisely so a module compiled after this
// bundle can record one it has no word for. Such a kind reaches the screen as the panel sent it —
// never as a dotted locale key, which is what a plain lookup would have rendered.
test('a kind this bundle has no word for is shown as the panel sent it', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [INSTALL])
  await stubTaskStream(page, '')

  await page.goto('/tasks')

  const row = page.getByRole('row').filter({ hasText: INSTALL.subject })
  await expect(row).toContainText('FutureUnlistedKind')
  await expect(row).not.toContainText('tasks.kinds.')
})

/** A finished backup of another account, earlier in the month, so the filters have something to leave out. */
const BACKUP: PanelTask = {
  ...INSTALL,
  id: '44444444-4444-4444-4444-444444444444',
  kind: 'BackupCreate',
  subject: 'acme',
  status: 'completed',
  percent: 100,
  startedAt: '2026-08-20T09:00:00Z',
  finishedAt: '2026-08-20T09:03:00Z',
}

/** A failed certificate order, so the failure notice has something to count. */
const FAILED: PanelTask = {
  ...INSTALL,
  id: '55555555-5555-5555-5555-555555555555',
  kind: 'CertificateIssue',
  subject: 'shop.example.com',
  status: 'failed',
  errorCode: 'AcmeValidationFailed',
  startedAt: '2026-09-02T09:00:00Z',
  finishedAt: '2026-09-02T09:01:00Z',
}

/**
 * Opens the tasks screen over a set of tasks, newest first as the panel orders them.
 * @param page The Playwright page.
 * @param tasks The tasks the stubbed panel holds.
 * @returns Every list request the page made, oldest first.
 */
const openTasks = async (page: Page, tasks: PanelTask[]): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTaskStream(page, '')
  return await stubTasks(page, tasks)
}

test('the tasks list is a server page: the search reaches the panel and narrows the rows', async ({ page }) => {
  const requests = await openTasks(page, [INSTALL, BACKUP])

  await page.goto('/tasks')
  await expect(page.getByRole('cell', { name: 'acme', exact: true })).toBeVisible()

  await page.getByRole('searchbox', { name: 'Search tasks' }).fill('php')

  await expect(page).toHaveURL(/search=php/)
  await expect(page.getByRole('cell', { name: 'acme', exact: true })).toHaveCount(0)
  await expect(page.getByRole('cell', { name: INSTALL.subject, exact: true })).toBeVisible()
  expect(requests().at(-1)?.searchParams.get('search')).toBe('php')
})

test('a column heading sorts the tasks by subject on the server', async ({ page }) => {
  const requests = await openTasks(page, [INSTALL, BACKUP])

  await page.goto('/tasks')
  const heading = page.getByRole('columnheader', { name: 'Subject' })
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/sort=subject/)
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  await expect(page.getByRole('row').nth(1)).toContainText('acme')
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('subject')
})

test('status, operation and a date window applied in the sheet become chips and reach the panel', async ({ page }) => {
  const requests = await openTasks(page, [INSTALL, BACKUP, FAILED])

  await page.goto('/tasks')
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Operation' }).click()
  // The stub names a kind by its machine name, as the panel does for one it has no word for.
  await page.getByRole('option', { name: 'BackupCreate' }).click()
  await dialog.getByLabel('Started from').fill('2026-08-01')
  await dialog.getByLabel('Started until').fill('2026-08-31')
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/kind=BackupCreate/)
  await expect(page).toHaveURL(/from=2026-08-01/)
  const expectedFrom = await page.evaluate(() => {
    return new Date(2026, 7, 1).toISOString()
  })
  const sent = requests().at(-1)
  expect(sent?.searchParams.get('kind')).toBe('BackupCreate')
  expect(sent?.searchParams.get('from')).toBe(expectedFrom)
  expect(sent?.searchParams.get('to')).toMatch(/^2026-(08-31|09-01)T/)
  await expect(page.getByRole('cell', { name: 'acme', exact: true })).toHaveCount(1)
  await expect(page.getByRole('cell', { name: INSTALL.subject, exact: true })).toHaveCount(0)

  const chips = page.getByTestId('active-filter-chips')
  await expect(chips).toContainText('Operation: BackupCreate')
  await page.getByRole('button', { name: 'Clear all' }).click()
  await expect(page).toHaveURL(/\/tasks$/)
})

test('the failure notice is the panel count, and its action narrows the list to failed tasks', async ({ page }) => {
  const requests = await openTasks(page, [INSTALL, BACKUP, FAILED])

  await page.goto('/tasks')
  await expect(page.getByText('1 task failed.')).toBeVisible()

  await page.getByRole('button', { name: 'Show failed tasks' }).click()

  await expect(page).toHaveURL(/status=failed/)
  expect(requests().at(-1)?.searchParams.get('status')).toBe('failed')
  await expect(page.getByRole('cell', { name: FAILED.subject, exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'acme', exact: true })).toHaveCount(0)
})

test('an inverted window is refused by the panel and its sentence is shown', async ({ page }) => {
  await openTasks(page, [INSTALL])
  await page.route(/\/api\/v1\/tasks\?.*from=/, async (route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'TaskDateRangeInverted', title: 'Invalid', detail: 'The start is after the end.' }),
    })
  })

  await page.goto('/tasks?from=2026-09-30&to=2026-09-01')

  await expect(page.getByText('The start is after the end.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a search matching nothing is the no-results state, not the empty list', async ({ page }) => {
  await openTasks(page, [INSTALL])

  await page.goto('/tasks?search=nothing')

  await expect(page.getByText('No tasks match')).toBeVisible()
  await expect(page.getByText('No tasks yet')).toHaveCount(0)
  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page.getByRole('cell', { name: INSTALL.subject, exact: true })).toBeVisible()
})

test('the tasks list pages on the server and keeps the page in the URL', async ({ page }) => {
  const catalogue = Array.from({ length: 12 }, (_, index): PanelTask => {
    return { ...BACKUP, id: `00000000-0000-0000-0000-0000000000${String(index).padStart(2, '0')}` }
  })
  const requests = await openTasks(page, catalogue)

  await page.goto('/tasks?pageSize=10')
  await expect(page.getByRole('cell', { name: 'acme', exact: true })).toHaveCount(10)

  await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()

  await expect(page).toHaveURL(/page=2/)
  await expect(page.getByRole('cell', { name: 'acme', exact: true })).toHaveCount(2)
  expect(requests().at(-1)?.searchParams.get('page')).toBe('2')
})
