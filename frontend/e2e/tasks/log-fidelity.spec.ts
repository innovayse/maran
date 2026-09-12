import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubTaskStream, stubTasks, taskEndFrame, taskFrame } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

// The shape a suspension attestation actually has, taken from the backend that writes it: several
// lines, one of them the clause `UnmanagedLoginPolicy.Describe` appends. The last line is not
// decoration either — it is a name a caller chose, carrying angle brackets, and it is here because
// the pane's contract is that the log is TEXT.
const ATTESTATION =
  'Suspending acme.\n' +
  "Covered: shell, SFTP and FTPS logins, cron, databases and the panel's own web login,\n" +
  "and 2 login(s) sharing this account's uid that the panel did not create and does not lock.\n" +
  'NOT covered: anything the operator added by hand.\n' +
  'Subject: <b>acme</b>\n'

const SUSPEND: PanelTask = {
  id: '44444444-4444-4444-4444-444444444444',
  kind: 'AccountSuspend',
  subject: 'acme',
  correlationId: 'c0rr-3l4t10n',
  status: 'running',
  percent: 10,
  log: 'Suspending acme.\n',
  errorCode: null,
  startedAt: '2026-09-11T09:00:00Z',
  finishedAt: null,
  revision: 1,
}

/**
 * Opens the live pane of the one task the panel reports, with the stream serving `body`.
 * @param page The Playwright page under test.
 * @param body The raw `text/event-stream` body the task's stream serves.
 * @returns Resolves once the pane is open.
 */
const openLivePane = async (page: Page, body: string): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [SUSPEND])
  await stubTaskStream(page, body)

  await page.goto('/tasks')
  await page.getByRole('button', { name: `Actions for ${SUSPEND.subject}` }).click()
  await page.getByRole('menuitem', { name: 'Live view' }).click()
}

// This is the LAST link of the chain that carries the number of logins sharing an account's uid
// that the panel neither created nor locks: the agent counts it, the contract carries it,
// `UnmanagedLoginPolicy` words it, the suspend handler reports it to the task stream, and this
// panel is where a person finally reads it. Every other link is asserted in the backend suite; this
// one was asserted nowhere, so an edit to `TaskLivePane.vue` could swallow the number with every
// gate green.
//
// What is pinned is NOT a sentence — the backend owns that wording and may edit it. It is that
// whatever the backend put in the log arrives on screen UNCHANGED: the pane's rendered text equals
// the log character for character, newlines included. A test that hunted for one clause would still
// pass against a pane that truncated the rest, collapsed the lines, or showed only the newest one,
// and those are the ways a number gets swallowed without anybody deleting it.
//
// The equality is also the vacuity guard, and deliberately so: it is on the axis that can go blind.
// A pane that has not loaded, an empty `<pre>`, a `noLog` placeholder and a stream frame that never
// arrived all read as a different string and all fail — there is no value of "broken" that this
// line reads as a pass.
test('the whole of what the task reported reaches the screen, character for character', async ({
  page,
}) => {
  await openLivePane(
    page,
    taskFrame({ ...SUSPEND, percent: 80, log: ATTESTATION, revision: 7 }) + taskEndFrame('completed'),
  )

  const pane = page.getByRole('progressbar', { name: 'Progress of this task' })
  // The frame landed and the pane is reading the STREAM, not the listing: the listing said 10.
  await expect(pane).toHaveAttribute('aria-valuenow', '80')

  const log = page.locator('pre')
  await expect(log).toHaveCount(1)
  // Not `toHaveText`, which normalises whitespace: the newlines are half of what is being checked.
  await expect
    .poll(async () => {
      return (await log.textContent()) ?? ''
    })
    .toBe(ATTESTATION)
})

// The pane's own doc comment promises the log is rendered as text and never as markup, and the log
// carries names a caller chose. The bracketed name above is the probe; this is the assertion that
// it stayed a name. The `toHaveCount(0)` is only worth something because the test above proves the
// same characters ARE on screen — a pane rendering nothing at all would satisfy this line alone.
test('a name in the log that looks like markup stays a name', async ({ page }) => {
  await openLivePane(
    page,
    taskFrame({ ...SUSPEND, percent: 80, log: ATTESTATION, revision: 7 }) + taskEndFrame('completed'),
  )

  const log = page.locator('pre')
  await expect(log).toContainText('<b>acme</b>')
  await expect(log.locator('b')).toHaveCount(0)
})
