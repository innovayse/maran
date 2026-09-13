import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackupDeletion, stubBackups, stubBackupsProblem } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  // A module this bundle has no glyph for, so a spec can tell a chosen icon from the neutral one.
  { name: 'notifications', displayName: 'Notifications', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** A finished copy: an artifact exists, and it has a size and two database dumps. */
const COMPLETED: Backup = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  status: 'completed',
  kind: 'manual',
  sizeBytes: 1_572_864,
  sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
  databaseCount: 2,
  startedAt: '2026-09-06T03:00:00Z',
  finishedAt: '2026-09-06T03:04:00Z',
  failureCode: '',
  failureDisplayName: '',
}

/** A run that produced NO artifact. Its `sizeBytes` is zero, which is not a size of zero. */
const FAILED: Backup = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  status: 'failed',
  kind: 'manual',
  sizeBytes: 0,
  sha256: '',
  databaseCount: 0,
  startedAt: '2026-09-06T04:00:00Z',
  finishedAt: '2026-09-06T04:00:30Z',
  failureCode: 'BackupArchiveTooLarge',
  failureDisplayName: 'The archive grew past what this server will store',
}

test('the backups screen shows the empty state when the panel reports no backups', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [])

  await page.goto('/backups')

  await expect(page.getByText('No backups yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a row names the owning account, its status and how many databases the copy holds', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])

  await page.goto('/backups')

  const row = page.getByRole('row').filter({ hasText: 'Completed' })
  // The status, as its OWN element's whole text. The `hasText` filter above only SELECTS the row,
  // and it cannot assert one: Playwright's text matching is a case-insensitive substring match, so
  // `'Completed'` is satisfied by the wire constant `completed` just as happily as by the word the
  // operator reads. `toHaveText` with a string is exact and case-sensitive, so the machine value
  // fails it — which is the whole difference between a filter and an assertion here.
  await expect(row.getByTestId('backup-status')).toHaveText('Completed')
  await expect(row).toContainText('alice')
  await expect(row).toContainText('1.5 MiB')
  // The databases column, read as the CELL'S WHOLE TEXT rather than as "a 2 appears somewhere in
  // this row". Containment could not fail here: the row carries `2026-09-06` in two timestamp
  // columns, so `toContainText('2')` was satisfied by the date whatever the Databases cell held —
  // an empty cell, the size beside it, any number at all. Column five is Databases (Account,
  // Status, Reason, Size, Databases, Started, Finished, Actions).
  await expect(row.getByRole('cell').nth(4)).toHaveText('2')
  // The identifier is not a fact an operator reads, and printing it where the account's name
  // belongs is what this column exists to prevent.
  await expect(row).not.toContainText(ALICE.id)
})

// A failed run produced no archive. `sizeBytes` is zero for want of a file, and rendering it as
// "0 B" would say an archive exists and is empty — the opposite of the truth, in the column an
// operator uses to decide whether a copy is worth anything.
test('a failed backup shows its failure code and no size at all', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [FAILED])

  await page.goto('/backups')

  const row = page.getByRole('row').filter({ hasText: 'Failed' })
  // Exact and case-sensitive, for the reason spelled out on the completed row above: the raw
  // `failed` the panel sends over the wire satisfies the filter and fails this line.
  await expect(row.getByTestId('backup-status')).toHaveText('Failed')
  // Positive control on the probe: the row IS rendered and this locator can read it.
  await expect(row).toContainText('alice')
  await expect(row).toContainText('BackupArchiveTooLarge')
  await expect(row).not.toContainText('0 B')
})

// This spec used to assert that NO restore control existed anywhere, and that was a true statement
// about a deliberate absence: the agent could restore and no panel endpoint reached it. The panel
// publishes `POST /api/v1/backups/{id}/restore` now, so the true statement has changed rather than
// gone away — a restore is offered exactly where the panel can act, and nowhere else. A failed run
// produced no artifact and would be answered `BackupNotRestorable`, so a control on that row could
// only ever refuse.
test('restore is offered on a completed backup and on no other', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED, FAILED])

  await page.goto('/backups')

  const completedRow = page.getByRole('row').filter({ hasText: 'Completed' })
  const failedRow = page.getByRole('row').filter({ hasText: 'Failed' })

  // Both rows now reach their commands through a per-row menu, so the probe has to open one.
  // Positive control on the failed row: its menu IS rendered, it opens, and this locator CAN find
  // a command inside it, so its silence about restore is a fact about the row rather than about
  // the probe.
  await failedRow.getByRole('button', { name: 'Actions for the backup of alice' }).click()
  await expect(page.getByRole('menuitem', { name: 'Delete' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: /Restore/i })).toHaveCount(0)
  await page.keyboard.press('Escape')

  await completedRow.getByRole('button', { name: 'Actions for the backup of alice' }).click()
  await expect(page.getByRole('menuitem', { name: 'Restore', exact: true })).toBeVisible()
})

// The other two things the screen must not imply. Remote destinations are refused structurally at
// both the agent's boundary and the panel's, and the create endpoint takes no destination at all —
// so a screen that named a bucket, a region or a provider would be advertising a choice that does
// not exist.
//
// This spec used to forbid the bare word "destination" as well. That was a true statement about the
// screen when it was written and this feature made it a stale one: the heading now links to
// `/backups/destinations`, where the one place copies rest is READ and nothing is offered. The
// forbidden list is therefore narrowed to what the spec always meant — a remote PROVIDER — and the
// link is asserted positively, so the guarantee is sharpened rather than dropped. Forbidding a word
// nobody is allowed to type is a silence; forbidding a provider is a rule.
test('the backups screen offers no remote destination and names none', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])

  await page.goto('/backups')

  // Positive control on the probe, again: the account picker IS on the page.
  await expect(page.getByText('Account').first()).toBeVisible()

  for (const forbidden of ['S3', 'Bucket', 'bucket', 'Region', 'region', 'Endpoint URL']) {
    await expect(page.locator('body')).not.toContainText(forbidden)
  }

  // The one thing this screen may say about where copies live is a link to the screen that reads
  // it. A link is not a choice: it opens a page with no control on it.
  await expect(page.getByRole('link', { name: 'Backup destinations' })).toBeVisible()
  // And the create form still offers nothing to choose a destination with, which is the half the
  // word ban was really policing.
  await expect(page.getByRole('combobox', { name: /destination/i })).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: /destination/i })).toHaveCount(0)
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
test('the backups screen renders the backend RFC 7807 detail verbatim when the list request fails', async ({
  page,
}) => {
  const backendDetail = 'The backup service is temporarily unavailable. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackupsProblem(page, backendDetail)

  await page.goto('/backups')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No backups yet')).toHaveCount(0)
})

// Deleting a backup destroys the only copy of an account's files that the panel holds. The request
// must not leave the page on the first click — and the count is what proves it, where clicking a
// confirmation would only prove a confirmation exists.
test('deleting a backup asks first, sends nothing until it is confirmed, then removes the row', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])
  const deletions = await stubBackupDeletion(page)

  await page.goto('/backups')
  await page.getByRole('button', { name: 'Actions for the backup of alice' }).click()

  // Positive control: the menu opened and holds the command. Without it a menu that never opened
  // would read the same as a confirmation that never appeared.
  const remove = page.getByRole('menuitem', { name: 'Delete' })
  await expect(remove).toBeVisible()
  await remove.click()

  // The confirmation is the thing under test, and it is asserted on the dialog by its accessible
  // name as well as by its words: a dialog asking the wrong question, or naming the wrong row,
  // would satisfy a bare "this text is visible somewhere" check.
  const dialog = page.getByRole('dialog', { name: /^Delete the backup of alice from / })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText(
    'Delete this backup? The copy is gone, and nothing here can bring it back.',
  )
  expect(deletions()).toEqual(0)

  await dialog.getByRole('button', { name: 'Yes, delete it' }).click()

  await expect(page.getByText('No backups yet')).toBeVisible()
  expect(deletions()).toEqual(1)
})

test('the sidebar links to the backups screen and the page opens with one heading of its own', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [])

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'Backups' }).click()

  await expect(page).toHaveURL(/\/backups$/)
  // `exact` because a substring match on a page whose empty state reads "No backups yet" would
  // match that too, and a strict-mode collision here would hide the count assertion below.
  await expect(page.getByRole('heading', { level: 1, name: 'Backups', exact: true })).toBeVisible()
  // Two `<h1>` per signed-in page is this shell's filed structural defect: the sidebar brand and
  // the page heading. This page must add no third — it contributes exactly one of its own.
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(2)
})

// Three identical glyphs in a column of three rows tell the reader nothing the labels do not.
// `notifications` is in the catalogue precisely as the neutral case to compare against.
test('the backups entry draws its own glyph rather than the neutral one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [])

  await page.goto('/')

  const navigation = page.getByRole('navigation')
  const backupsGlyph = await navigation.getByRole('link', { name: 'Backups' }).locator('svg').innerHTML()
  const neutralGlyph = await navigation
    .getByRole('link', { name: 'Notifications' })
    .locator('svg')
    .innerHTML()

  expect(backupsGlyph).not.toEqual(neutralGlyph)
})

// A row menu that only a mouse can open takes the row's commands away from anyone who does not use
// one. The claim is the whole ARIA menu-button contract on a REAL row: the trigger opens from the
// keyboard, arrow keys move between the commands, Escape closes, and focus comes back to the
// trigger rather than being dropped at the top of the document — which is the failure a reader of
// the component cannot see, because the component's `close()` looks correct either way.
test('a row menu opens, moves and closes from the keyboard, and focus returns to its trigger', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])

  await page.goto('/backups')

  const trigger = page.getByRole('button', { name: 'Actions for the backup of alice' })
  await expect(trigger).toBeVisible()
  await trigger.focus()
  // Positive control on the axis that can go blind: the trigger really did take focus, so the key
  // presses below are being delivered to it and not to the document.
  await expect(trigger).toBeFocused()
  await expect(trigger).toHaveAttribute('aria-expanded', 'false')

  // Enter opens on the FIRST command, which is the pattern's contract.
  await page.keyboard.press('Enter')
  await expect(trigger).toHaveAttribute('aria-expanded', 'true')
  const restore = page.getByRole('menuitem', { name: 'Restore', exact: true })
  const remove = page.getByRole('menuitem', { name: 'Delete' })
  await expect(restore).toBeFocused()

  // Real focus moves between the items, rather than staying on the trigger with `aria-activedescendant`.
  await page.keyboard.press('ArrowDown')
  await expect(remove).toBeFocused()
  await page.keyboard.press('ArrowUp')
  await expect(restore).toBeFocused()

  await page.keyboard.press('Escape')
  await expect(page.getByRole('menu')).toHaveCount(0)
  await expect(trigger).toHaveAttribute('aria-expanded', 'false')
  // THE CLAIM. A menu that closes without handing focus back leaves a keyboard user at the top of
  // the document, several dozen Tabs from the row they were working on.
  await expect(trigger).toBeFocused()
})

// `UiTable` scrolls horizontally inside its own container so a wide table never moves the page
// sideways, and the backups table at eight columns is wide enough to do it. A menu positioned
// inside that container is cut off at its edge — the defect the panel's `Teleport` to `body`
// exists to prevent — and no amount of reading the component says whether it still holds on a real
// row, in a window narrow enough for the container to actually scroll. 520px is that window,
// measured rather than guessed: at 900 the eight columns still fit and the vacuity guard below
// caught the run reporting on a table that was not scrolling at all.
test('a row menu in a horizontally scrolled table is neither clipped nor lost by the scroll', async ({
  page,
}) => {
  await page.setViewportSize({ width: 520, height: 700 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])

  await page.goto('/backups')

  const trigger = page.getByRole('button', { name: 'Actions for the backup of alice' })
  // The table has to exist before it can be measured — an evaluate that runs before the row is
  // painted reports "no table" and would read exactly like a table that does not overflow.
  await expect(trigger).toBeVisible()

  // Vacuity guard on the axis that can go blind: if the table ever stops overflowing at this
  // width, every assertion below would pass while observing nothing at all.
  const overflow = await page.evaluate(() => {
    const scroller = document.querySelector('table')?.parentElement ?? null
    if (scroller === null) {
      return null
    }
    scroller.scrollLeft = scroller.scrollWidth
    return { max: scroller.scrollWidth - scroller.clientWidth, scrolled: scroller.scrollLeft }
  })
  expect(overflow).not.toBeNull()
  expect(overflow?.max).toBeGreaterThan(0)

  await trigger.click()

  const remove = page.getByRole('menuitem', { name: 'Delete' })
  await expect(remove).toBeVisible()

  // Clipping is not visibility: a clipped panel is still "visible" to the DOM. The panel's own box
  // has to sit inside the viewport on every side, which is what a container-clipped menu fails.
  const panelBox = await page.getByRole('menu').boundingBox()
  expect(panelBox).not.toBeNull()
  expect(panelBox?.x ?? -1).toBeGreaterThanOrEqual(0)
  expect(panelBox?.y ?? -1).toBeGreaterThanOrEqual(0)
  expect((panelBox?.x ?? 0) + (panelBox?.width ?? 0)).toBeLessThanOrEqual(520)
  expect((panelBox?.y ?? 0) + (panelBox?.height ?? 0)).toBeLessThanOrEqual(700)

  // And it survives a scroll under it: the panel follows its trigger instead of being dismissed.
  // No `force` — the command must be a real, hit-testable target after the scroll.
  await page.evaluate(() => {
    const scroller = document.querySelector('table')?.parentElement ?? null
    scroller?.scrollBy({ left: -40 })
    scroller?.dispatchEvent(new Event('scroll', { bubbles: false }))
  })
  await expect(remove).toBeVisible()
  await remove.click()
  await expect(page.getByRole('dialog', { name: /^Delete the backup of alice from / })).toBeVisible()
})

// The answer "no" must leave the copy where it is. The DELETE count is what says so: a dialog that
// closed is equally consistent with a request already on its way to the panel.
test('dismissing the delete confirmation sends no request, and confirm is not the default answer', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])
  const deletions = await stubBackupDeletion(page)

  await page.goto('/backups')
  await page.getByRole('button', { name: 'Actions for the backup of alice' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()

  const dialog = page.getByRole('dialog', { name: /^Delete the backup of alice from / })
  await expect(dialog).toBeVisible()

  // Opened from a row menu: focus must be INSIDE the dialog rather than left on the trigger behind
  // it, and it must not be on the confirm button — a reflexive Enter must not answer "yes".
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
  expect(focus.text).not.toEqual('Yes, delete it')

  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(deletions()).toEqual(0)

  // And the row is still there, which is the outcome the operator asked for.
  await expect(page.getByRole('button', { name: 'Actions for the backup of alice' })).toBeVisible()
})

/**
 * The failed run as a Russian-speaking operator's panel receives it: the same machine-stable code,
 * and the sentence in the language the request asked for. Both halves are the backend's — the SPA
 * holds no text for a server outcome — so the fixture states them exactly as the live API does.
 */
const FAILED_IN_RUSSIAN: Backup = {
  ...FAILED,
  failureCode: 'AgentSystemFailure',
  failureDisplayName: 'Серверу не удалось создать копию',
}

// The defect this closes: the cell printed the identifier and nothing else, so a row read
// "Failed AgentSystemFailure". The assertion names the sentence rather than checking the cell is
// non-empty, because a non-empty check passes against the identifier it exists to replace.
test('a failed row is named in words and still carries the code a ticket quotes', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [FAILED])

  await page.goto('/backups')

  const row = page.getByRole('row').filter({ hasText: 'Failed' })
  // Positive control on the probe: the row IS rendered and this locator can read it, so a missing
  // sentence below is a fact about the screen rather than about the locator.
  await expect(row).toContainText('alice')
  await expect(row).toContainText('The archive grew past what this server will store')
  await expect(row).toContainText('BackupArchiveTooLarge')
})

// The whole point of the display name is that an operator who chose Russian stops reading English
// identifiers as if they were sentences. So the assertion is made in Russian, on the exact text the
// live panel returns for this code, and the code is asserted to have SURVIVED beside it.
test('a Russian panel names the failure in Russian and keeps the English code beside it', async ({
  page,
}) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [FAILED_IN_RUSSIAN])

  await page.goto('/backups')

  const row = page.getByRole('row').filter({ hasText: 'alice' })
  // Positive control on the probe: the row IS rendered in the Russian interface and this locator
  // can read it — the account name is language-independent, so it proves the reach, not the text.
  await expect(row).toContainText('alice')
  await expect(row).toContainText('Серверу не удалось создать копию')
  await expect(row).toContainText('AgentSystemFailure')
})
