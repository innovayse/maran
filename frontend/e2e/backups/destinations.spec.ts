import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  stubBackupDestinationEncryption,
  stubBackupDestinations,
  stubBackupDestinationsRefusal,
} from '../fixtures/stub-backup-destination-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubBackupOptions } from '../fixtures/stub-backup-options-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { BackupDestination } from '../../src/types/backupDestination'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
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
  encryptArchives: false,
}

// The path is a RECORD of where the agent writes, not an instruction: the agent refuses to be told
// a root. A screen that omitted it would leave an operator with no way to find their copies; one
// that offered to edit it would be stating a root the agent will not honour.
test('the recorded destination is shown with the directory its copies rest in', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [DEFAULT_LOCAL])

  await page.goto('/backups/destinations')

  // Positive control on the probe: the row IS rendered and this page object can read it, so a
  // missing path below is a fact about the screen rather than about the locator.
  await expect(page.getByRole('table').getByRole('cell', { name: /^This server( |$)/ })).toBeVisible()
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
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [{ ...DEFAULT_LOCAL, path: null }])

  await page.goto('/backups/destinations')

  // Positive control on the probe: the destination's own row IS on screen and this locator can
  // read it, so the assertions below are about the path row rather than about a page that failed
  // to render at all.
  await expect(page.getByRole('table').getByRole('cell', { name: /^This server( |$)/ })).toBeVisible()
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
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [])

  await page.goto('/backups/destinations')

  await expect(page.getByText('Nothing recorded yet')).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})

// Admin surfaces answer 403, never 404, and the sentence is the backend's own, already localized.
// The SPA holds no copy of it (rules/vue.md): a frontend string here would be a second, untranslated
// statement of a decision the server already made in the user's language.
test("a refusal to read is rendered in the panel's own words", async ({ page, guard }) => {
  guard.allow(403, /\/api\/v1\/backup-destinations(\?.*)?$/)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinationsRefusal(page, 403, 'Forbidden', 'This screen is for administrators.')

  await page.goto('/backups/destinations')

  await expect(page.getByText('This screen is for administrators.')).toBeVisible()
  await expect(page.getByRole('table').getByRole('cell', { name: /^This server( |$)/ })).toHaveCount(0)
})

// S3 is an open decision: the seam is written and wired to nothing, the agent refuses a remote
// destination and the panel refuses before asking. So the kind is DESCRIBED and marked unavailable
// — absent would be quieter than the backend, which publishes a named refusal — and there is no
// control, because every request the create endpoint can be given is answered with a refusal.
test('remote storage is described, marked unavailable, and offered by nothing', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [DEFAULT_LOCAL])

  await page.goto('/backups/destinations')

  await expect(page.getByText('S3-compatible storage')).toBeVisible()
  await expect(page.getByText('Not on this build')).toBeVisible()
  // Positive control on the same badge vocabulary: the local kind IS marked as the one in use, so
  // the assertion above is about which kind carries which mark rather than about the words.
  await expect(page.getByText('In use')).toBeVisible()
  // Nothing on this screen offers to add, edit or remove a destination. A control whose only
  // outcome is a refusal is the same broken promise as a missing control, made the other way. The
  // list's own controls — search, filters, paging — read; the only buttons in `<main>` are those,
  // and the only field is the search box, which is a `searchbox` and not a `textbox`.
  const main = page.getByRole('main')
  await expect(main.getByRole('button', { name: /add|new|edit|remove|delete|save/i })).toHaveCount(0)
  await expect(main.getByRole('textbox')).toHaveCount(0)
  // Positive control on the probe: the list's own filter control IS a button this locator reads.
  await expect(main.getByTestId('filter-trigger')).toBeVisible()
})

// No destination on this build holds a credential, and the screen must not become the place one
// first appears. The contract (`BackupDestinationDto`) names no credential member and the guard
// refuses a body that carries one, so this is checked on what the screen does: it offers no field
// that asks for a key or a secret, and it opens by reading and sending nothing.
// UNOBSERVED HERE: a credential form that opens only after a click this spec does not make.
test('the screen offers no place to type a storage credential and sends nothing but reads', async ({ page }) => {
  const writes: string[] = []
  page.on('request', (request) => {
    // The shell renews its own session with a POST on any screen; that is not this screen sending.
    if (request.url().includes('/api/') && !request.url().includes('/api/v1/auth/refresh') && request.method() !== 'GET') {
      writes.push(`${request.method()} ${request.url()}`)
    }
  })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [DEFAULT_LOCAL])

  await page.goto('/backups/destinations')

  // Positive control: the row IS on screen, and the list's own search field IS a control the
  // locators below can find, so their silence is about credentials and not about a blank page.
  await expect(page.getByRole('table').getByRole('cell', { name: /^This server( |$)/ })).toBeVisible()
  await expect(page.getByRole('searchbox')).toBeVisible()
  await expect(page.getByLabel(/access key|secret|password|token|credential/i)).toHaveCount(0)
  await expect(page.locator('input[type="password"]')).toHaveCount(0)
  expect(writes).toEqual([])
})

// 250-odd stubbed specs cannot see an unreachable route: a spec navigates by URL and an operator
// navigates by clicking. This one clicks its way in.
test('an operator reaches the destinations screen through the shell', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackups(page, [])
  await stubBackupDestinations(page, [DEFAULT_LOCAL])
  await stubLandingReads(page)

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
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [SEEDED_IN_RUSSIAN])

  await page.goto('/backups/destinations')

  // Positive control on the probe: the row IS rendered in the Russian interface and this locator
  // reads it by a language-independent value, so the assertions below are about the text.
  await expect(page.getByText('/var/backups/maran')).toBeVisible()
  await expect(page.getByRole('table').getByText('Локальное хранилище')).toBeVisible()
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
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [OPERATOR_NAMED])

  await page.goto('/backups/destinations')

  await expect(page.getByRole('table').getByText('Ночной диск')).toBeVisible()
  // Positive control on the probe: the same locator DOES find a stored-name row when one is due,
  // proving the absence asserted next is a fact about this row rather than a blind locator.
  await expect(page.getByText('Сохранённое название')).toHaveCount(0)
  await stubBackupDestinations(page, [SEEDED_IN_RUSSIAN])
  await page.reload()
  await expect(page.getByText('Сохранённое название')).toBeVisible()
})

/** A second destination, so the list has something to search, filter and sort between. */
const REMOTE: BackupDestination = {
  id: '99999999-9999-9999-9999-999999999999',
  name: 'Off-site',
  displayName: 'Off-site',
  kind: 's3',
  path: null,
  isDefault: false,
  createdAt: '2026-09-02T09:00:00Z',
  encryptArchives: false,
}

// The destinations are a server page like every other list: the search, the kind filter and the
// sort reach the panel as query parameters, and the screen shows what it answers.
test('the destinations list searches, filters by kind and sorts through the panel', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  const requests = await stubBackupDestinations(page, [DEFAULT_LOCAL, REMOTE])

  await page.goto('/backups/destinations')
  const table = page.getByRole('table')
  await expect(table.getByText('Off-site')).toBeVisible()
  await expect(table.getByRole('cell', { name: /^This server( |$)/ })).toBeVisible()

  await page.getByRole('searchbox', { name: 'Search destinations' }).fill('off')
  await expect(page).toHaveURL(/search=off/)
  await expect(table.getByRole('cell', { name: /^This server( |$)/ })).toHaveCount(0)
  await expect(table.getByText('Off-site')).toBeVisible()
  expect(requests().at(-1)?.searchParams.get('search')).toBe('off')

  await page.getByRole('button', { name: 'Clear the search' }).click()
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Kind of storage' }).click()
  await page.getByRole('option', { name: "This server's disk" }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/kind=local/)
  await expect(table.getByText('Off-site')).toHaveCount(0)
  await expect(page.getByRole('button', { name: "Remove filter: Kind of storage: This server's disk" })).toBeVisible()
  expect(requests().at(-1)?.searchParams.get('kind')).toBe('local')

  await page.getByRole('button', { name: "Remove filter: Kind of storage: This server's disk" }).click()
  await page.getByRole('columnheader', { name: 'Recorded' }).getByRole('button').click()
  await expect(page).toHaveURL(/sort=createdAt/)
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('createdAt')
})

// A search matching nothing is not the empty server: it offers the way back to every destination.
test('a search matching no destination offers the way back to all of them', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [DEFAULT_LOCAL])

  await page.goto('/backups/destinations?search=nothing')

  await expect(page.getByText('No destinations match')).toBeVisible()
  await expect(page.getByText('Nothing recorded yet')).toHaveCount(0)
  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page.getByRole('table').getByRole('cell', { name: /^This server( |$)/ })).toBeVisible()
})

// Issue #160. Switching encryption on is the weighty answer: the key stays in the panel, so the
// dialog must name the custody before anything is sent, and abandoning it must send nothing.
test('turning encryption on asks first, warns about the key, and sends only after the confirmation', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [DEFAULT_LOCAL])
  const sent = await stubBackupDestinationEncryption(page)

  await page.goto('/backups/destinations')

  const toggle = page.getByRole('switch', { name: 'Encrypt new backups' })
  await expect(toggle).toHaveAttribute('aria-checked', 'false')

  await toggle.click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByText('recovery archive', { exact: false })).toBeVisible()
  expect(sent()).toHaveLength(0)

  // Abandoning the question changes nothing and sends nothing.
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await expect(dialog).toHaveCount(0)
  expect(sent()).toHaveLength(0)
  await expect(toggle).toHaveAttribute('aria-checked', 'false')

  await toggle.click()
  await page.getByRole('dialog').getByRole('button', { name: 'Turn encryption on' }).click()
  await expect(toggle).toHaveAttribute('aria-checked', 'true')
  expect(sent()).toEqual([{ enabled: true }])
})

// The other direction: an encrypted destination's switch starts on, and switching off asks too.
test('turning encryption off sends enabled false after the confirmation', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubBackupDestinations(page, [{ ...DEFAULT_LOCAL, encryptArchives: true }])
  const sent = await stubBackupDestinationEncryption(page)

  await page.goto('/backups/destinations')

  const toggle = page.getByRole('switch', { name: 'Encrypt new backups' })
  await expect(toggle).toHaveAttribute('aria-checked', 'true')

  await toggle.click()
  await page.getByRole('dialog').getByRole('button', { name: 'Turn encryption off' }).click()
  await expect(toggle).toHaveAttribute('aria-checked', 'false')
  expect(sent()).toEqual([{ enabled: false }])
})
