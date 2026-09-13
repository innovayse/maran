import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import {
  stubCronEntries,
  stubCronEntriesProblem,
  stubCronEntryEnabled,
  stubCronEntryMutations,
  stubCronEntryOutput,
  stubCronEnvironment,
} from '../fixtures/stub-cron-routes'
import type { Account } from '../../src/types/account'
import type { CronEntry, CronEntryOutput } from '../../src/types/cronEntry'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const NIGHTLY: CronEntry = {
  entryId: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  schedule: { minute: '30', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '2' },
  command: '/usr/bin/php /home/alice/cleanup.php',
  enabled: true,
}

const RAN: CronEntryOutput = {
  entryId: NIGHTLY.entryId,
  output: 'cleaned 12 files\n',
  lastExitCode: 0,
  lastRunAtUnix: 1_772_000_000,
}

const openCronPage = async (page: Page, entries: CronEntry[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEntries(page, entries)
  await stubCronEnvironment(page, [])
  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
}

/** Opens one row's actions menu. The trigger is icon-only, so its accessible name is what finds it. */
const openRowActions = async (page: Page, entry: CronEntry): Promise<void> => {
  await page.getByRole('button', { name: `Actions for ${entry.command}` }).click()
}

test('the cron table shows an entry as the crontab holds it', async ({ page }) => {
  await openCronPage(page, [NIGHTLY])

  const row = page.getByRole('row').filter({ hasText: NIGHTLY.command })
  await expect(row).toBeVisible()
  // The five fields written the way a crontab line writes them, so an operator can compare the row
  // against the server without translating anything in their head.
  await expect(row).toContainText('30 3 * * 2')
  await expect(row).toContainText('On')
})

// The switch is sent explicitly, never as a toggle the server resolves: a toggle applied to a row
// the operator last saw seconds ago switches whatever it finds. What would break this: sending
// `enabled: true` for an entry that is already on, sending no account, or a menu that dismissed
// itself before the item could be chosen — which is exactly the `UiDropdown` scroll-into-view bug
// that was fixed, and this spec would catch its return.
test('turning an entry off sends the state explicitly, with the account that owns it', async ({
  page,
}) => {
  await openCronPage(page, [NIGHTLY])
  await stubCronEntryEnabled(page)

  await openRowActions(page, NIGHTLY)

  const switched = page.waitForRequest((request) => {
    return request.method() === 'POST' && request.url().includes('/enabled')
  })
  await page.getByRole('menuitem', { name: 'Turn off' }).click()

  const request = await switched
  expect(request.url()).toContain(`/api/v1/cron-entries/${NIGHTLY.entryId}/enabled`)
  expect(request.postDataJSON()).toEqual({ accountId: ALICE.id, enabled: false })
})

// Removal is destructive, so it asks first, in the panel's one confirmation dialog. What would
// break it: a menu item that removed immediately (the confirmation would never appear), or a
// DELETE that omitted `?accountId=`, which the module needs because it has no row to infer the
// account from.
test('removing an entry asks first and then names the account in the request', async ({ page }) => {
  await openCronPage(page, [NIGHTLY])
  await stubCronEntryMutations(page)

  await openRowActions(page, NIGHTLY)
  await page.getByRole('menuitem', { name: 'Remove' }).click()

  // Asserted on the dialog, and by its accessible name as well as its text: a dialog that appeared
  // asking the wrong question would satisfy a bare "some text is visible" check.
  const dialog = page.getByRole('dialog', { name: `Remove the scheduled task ${NIGHTLY.command}` })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText(
    'Remove this entry? Its command file and its last run go with it.',
  )

  const deleted = page.waitForRequest((request) => {
    return request.method() === 'DELETE'
  })
  await dialog.getByRole('button', { name: 'Yes, remove it' }).click()

  const request = await deleted
  expect(request.url()).toContain(`/api/v1/cron-entries/${NIGHTLY.entryId}`)
  expect(request.url()).toContain(`accountId=${ALICE.id}`)
})

test('the last-run dialog shows what the run left behind', async ({ page }) => {
  await openCronPage(page, [NIGHTLY])
  await stubCronEntryOutput(page, RAN)

  await openRowActions(page, NIGHTLY)
  await page.getByRole('menuitem', { name: 'Last run' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText('cleaned 12 files')
  // The exit code as the value in its own field, not as a digit somewhere in the dialog. The
  // dialog also prints the last-run instant, whose formatting carries a `0` on any plausible
  // clock, so `toContainText('0')` passed with the exit-code field showing anything at all —
  // including "Not reported", which is the state the sibling test below exists to keep distinct.
  await expect(dialog.locator('dd').first()).toHaveText('0')
})

// The module answers 200 with a `null` BODY for an entry that has never run — `Result<CronEntryOutputDto?>`
// through `ToActionResult`. Every field of a reading has a plausible default (an empty string is a
// run that printed nothing, zero is a successful exit, epoch is a real instant), so a panel that
// flattened that null into an empty reading would tell somebody their job ran when it never has.
// What would break this: dropping the `?? null` in `useCronApi.getOutput`, or the dialog treating
// "no reading" and "a reading with nothing in it" as one state — either way the exit-code row would
// appear instead of the sentence.
test('an entry that has never run says so, rather than showing a run that never happened', async ({
  page,
}) => {
  await openCronPage(page, [NIGHTLY])
  await stubCronEntryOutput(page, null)

  await openRowActions(page, NIGHTLY)
  await page.getByRole('menuitem', { name: 'Last run' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('has not run yet')
  await expect(dialog).not.toContainText('Exit code')
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
test('the cron screen renders the panel’s RFC 7807 detail verbatim when the read fails', async ({
  page,
}) => {
  const backendDetail = 'That account is not available on this server.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEnvironment(page, [])
  await stubCronEntriesProblem(page, backendDetail)

  await page.goto('/cron')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('Nothing scheduled yet')).toHaveCount(0)
})

// The answer "no" must leave the crontab exactly as it was. Counting the DELETEs is what says so:
// a dialog that closes is compatible with a request already on its way.
test('dismissing the removal confirmation sends no request, and confirm is not the default answer', async ({
  page,
}) => {
  const deletes: string[] = []
  await openCronPage(page, [NIGHTLY])
  await stubCronEntryMutations(page)
  page.on('request', (request) => {
    if (request.method() === 'DELETE') {
      deletes.push(request.url())
    }
  })

  await openRowActions(page, NIGHTLY)
  await page.getByRole('menuitem', { name: 'Remove' }).click()

  const dialog = page.getByRole('dialog', { name: `Remove the scheduled task ${NIGHTLY.command}` })
  await expect(dialog).toBeVisible()

  // Focus is inside the dialog rather than left on the row menu's trigger behind it, and it is not
  // on the confirm button: a reflexive Enter must not be the answer "yes".
  const focus = await page.evaluate(() => {
    const panel = document.querySelector('[role="dialog"]')
    const active = document.activeElement
    return {
      inside: panel !== null && active !== null && panel.contains(active),
      label: active?.getAttribute('aria-label') ?? '',
      text: active?.textContent?.trim() ?? '',
    }
  })
  expect(focus.inside).toBe(true)
  expect(focus.label).not.toContain('Actions for')
  expect(focus.text).not.toEqual('Yes, remove it')

  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(deletes).toEqual([])
  await expect(page.getByRole('row').filter({ hasText: NIGHTLY.command })).toBeVisible()
})
