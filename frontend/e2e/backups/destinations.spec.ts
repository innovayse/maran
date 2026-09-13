import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  stubBackupDestinations,
  stubBackupDestinationsRefusal,
} from '../fixtures/stub-backup-destination-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubModules } from '../fixtures/stub-modules-route'
import type { BackupDestination } from '../../src/types/backupDestination'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

/** The one destination a running panel reconciles at startup: local, and the default. */
const DEFAULT_LOCAL: BackupDestination = {
  id: '77777777-7777-7777-7777-777777777777',
  name: 'This server',
  displayName: 'This server',
  kind: 'local',
  path: '/var/backups/maran',
  isDefault: true,
  createdAt: '2026-09-01T09:00:00Z',
}

// The path is a RECORD of where the agent writes, not an instruction: the agent refuses to be told
// a root. A screen that omitted it would leave an operator with no way to find their copies; one
// that offered to edit it would be stating a root the agent will not honour.
test('the recorded destination is shown with the directory its copies rest in', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinations(page, [DEFAULT_LOCAL])

  await page.goto('/backups/destinations')

  // Positive control on the probe: the row IS rendered and this page object can read it, so a
  // missing path below is a fact about the screen rather than about the locator.
  await expect(page.getByRole('heading', { name: 'This server' })).toBeVisible()
  await expect(page.getByText('/var/backups/maran')).toBeVisible()
  await expect(page.getByText('Default', { exact: true })).toBeVisible()
})

// The other branch of the same row. `path` is `null` when the panel could not establish it — the
// agent was unreachable, or is older than the field it is asked for — and the listing deliberately
// substitutes no default. The row must still appear, saying the path is not established: an
// operator during the incident that took the agent down needs to be told the panel is missing an
// answer, not shown a directory nobody confirmed and not shown a blank line.
test('a path the panel could not establish is said to be unestablished, not left blank', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinations(page, [{ ...DEFAULT_LOCAL, path: null }])

  await page.goto('/backups/destinations')

  // Positive control on the probe: the destination's own row IS on screen and this locator can
  // read it, so the assertions below are about the path row rather than about a page that failed
  // to render at all.
  await expect(page.getByRole('heading', { name: 'This server' })).toBeVisible()
  await expect(page.getByText('Directory on this server')).toBeVisible()
  await expect(
    page.getByText('Not established — the agent could not be asked where it writes.', {
      exact: false,
    }),
  ).toBeVisible()
  // And no guessed directory anywhere: the panel holds no value of its own.
  await expect(page.getByText('/var/backups/maran')).toHaveCount(0)
})

// A panel whose startup reconciliation has not run answers `200 []`. That is an answer about the
// server, not a failure, and the two must not be drawn alike: an error banner over an empty list
// would send an operator looking for a broken request that never happened.
test('a server that has recorded nothing says so instead of reporting a failure', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinations(page, [])

  await page.goto('/backups/destinations')

  await expect(page.getByText('Nothing recorded yet')).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})

// Admin surfaces answer 403, never 404, and the sentence is the backend's own, already localized.
// The SPA holds no copy of it (rules/vue.md): a frontend string here would be a second, untranslated
// statement of a decision the server already made in the user's language.
test("a refusal to read is rendered in the panel's own words", async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinationsRefusal(page, 403, 'Forbidden', 'This screen is for administrators.')

  await page.goto('/backups/destinations')

  await expect(page.getByText('This screen is for administrators.')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'This server' })).toHaveCount(0)
})

// S3 is an open decision: the seam is written and wired to nothing, the agent refuses a remote
// destination and the panel refuses before asking. So the kind is DESCRIBED and marked unavailable
// — absent would be quieter than the backend, which publishes a named refusal — and there is no
// control, because every request the create endpoint can be given is answered with a refusal.
test('remote storage is described, marked unavailable, and offered by nothing', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinations(page, [DEFAULT_LOCAL])

  await page.goto('/backups/destinations')

  await expect(page.getByText('S3-compatible storage')).toBeVisible()
  await expect(page.getByText('Not on this build')).toBeVisible()
  // Positive control on the same badge vocabulary: the local kind IS marked as the one in use, so
  // the assertion above is about which kind carries which mark rather than about the words.
  await expect(page.getByText('In use')).toBeVisible()
  // Nothing on this screen offers to add, edit or remove a destination. A control whose only
  // outcome is a refusal is the same broken promise as a missing control, made the other way.
  // Scoped to the `<main>` landmark: the shell's own chrome (sidebar toggle, theme, locale) is
  // full of buttons, and counting those would make this assertion about the layout.
  await expect(page.getByRole('main').getByRole('button')).toHaveCount(0)
  await expect(page.getByRole('main').getByRole('textbox')).toHaveCount(0)
})

// No destination on this build holds a credential, and the screen must not become the place one
// first appears. `BackupDestination` names every field the page reads, so a key the panel started
// sending would be dropped rather than printed.
test('a credential the panel sent anyway is never rendered', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinations(page, [
    { ...DEFAULT_LOCAL, accessKeyId: 'AKIAEXAMPLESECRET', secretAccessKey: 'AKIAEXAMPLESECRET' },
  ])

  await page.goto('/backups/destinations')

  // Positive control: the row from that same payload IS on screen, so the absence below is the
  // screen dropping an undeclared member rather than the row failing to render at all.
  await expect(page.getByRole('heading', { name: 'This server' })).toBeVisible()
  await expect(page.getByText('AKIAEXAMPLESECRET')).toHaveCount(0)
})

// 250-odd stubbed specs cannot see an unreachable route: a spec navigates by URL and an operator
// navigates by clicking. This one clicks its way in.
test('an operator reaches the destinations screen through the shell', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackups(page, [])
  await stubBackupDestinations(page, [DEFAULT_LOCAL])

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'Backups' }).click()
  // Positive control: the backups screen itself IS reached by clicking, so a failure below is
  // about the destinations screen's way in rather than about the shell or the locator.
  await expect(page).toHaveURL(/\/backups$/)

  await page.getByRole('link', { name: 'Backup destinations' }).click()

  await expect(page).toHaveURL(/\/backups\/destinations$/)
  await expect(
    page.getByRole('heading', { level: 1, name: 'Backup destinations', exact: true }),
  ).toBeVisible()
})

/**
 * The destination this panel seeds and names itself, as a Russian-speaking operator's panel
 * receives it: the row still stores the English name — translating it at write time would freeze
 * the boot language into the database — and the backend localizes the display half only.
 */
const SEEDED_IN_RUSSIAN: BackupDestination = {
  ...DEFAULT_LOCAL,
  name: 'Local storage',
  displayName: 'Локальное хранилище',
}

/** A destination an operator named. The backend echoes the typed words in both halves. */
const OPERATOR_NAMED: BackupDestination = {
  ...DEFAULT_LOCAL,
  id: '88888888-8888-8888-8888-888888888888',
  name: 'Ночной диск',
  displayName: 'Ночной диск',
}

// The defect this closes: the heading read `Local storage` inside a Russian interface. The stored
// name is asserted to have SURVIVED as a field, because it is what `psql` prints and what a support
// ticket names, and the heading no longer shows it.
test('the seeded destination is headed in the operator language with its stored name kept', async ({
  page,
}) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinations(page, [SEEDED_IN_RUSSIAN])

  await page.goto('/backups/destinations')

  // Positive control on the probe: the row IS rendered in the Russian interface and this locator
  // reads it by a language-independent value, so the assertions below are about the text.
  await expect(page.getByText('/var/backups/maran')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Локальное хранилище' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Local storage' })).toHaveCount(0)
  await expect(page.getByText('Сохранённое название')).toBeVisible()
  await expect(page.getByText('Local storage')).toBeVisible()
})

// The other half of the same ruling: only the panel's OWN row is localized. A destination an
// operator named is shown in the words they typed, and gains no second line restating them.
test('a destination an operator named is not relabelled and gains no stored-name row', async ({
  page,
}) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubBackupDestinations(page, [OPERATOR_NAMED])

  await page.goto('/backups/destinations')

  await expect(page.getByRole('heading', { name: 'Ночной диск' })).toBeVisible()
  // Positive control on the probe: the same locator DOES find a stored-name row when one is due,
  // proving the absence asserted next is a fact about this row rather than a blind locator.
  await expect(page.getByText('Сохранённое название')).toHaveCount(0)
  await stubBackupDestinations(page, [SEEDED_IN_RUSSIAN])
  await page.reload()
  await expect(page.getByText('Сохранённое название')).toBeVisible()
})
