import { expect, test, type Locator, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { stubTasks, stubTaskStream, taskEndFrame, taskFrame } from '../fixtures/stub-tasks-routes'
import type { Account } from '../../src/types/account'
import type { AuditEvent } from '../../src/types/audit'
import type { Backup } from '../../src/types/backup'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

/**
 * Protects how the redesigned data tables behave on a narrow screen (issue #82), on the four list
 * screens that carry the longest values: accounts, backups, the audit journal and tasks.
 *
 * Why it exists: two regressions in the table kit were found only by looking at screenshots, never
 * by the suite. At phone and tablet width (a) a body cell was squeezed until its text wrapped one
 * character per line, and (b) a status badge was squeezed until its label broke onto two lines or
 * was cut off by its cell. The shell clips sideways overflow on its main region, so a table that
 * is too wide for the screen does not scroll the page — it silently loses its right-hand columns —
 * and the only honest proof that a wide table behaves is to measure it.
 *
 * Four measurements per screen, width and language, all on geometry and never on a class name:
 *   0. a long machine identifier (an account name, a domain, an actor's login, an address, a task's
 *      subject) renders on ONE line. A hyphen is a break opportunity, so a name such as
 *      `acme-industrial-holdings` used to fold into three lines and a login into six, and an
 *      identifier read across six lines is a different string to the eye. The cell must take the
 *      identifier's width and the table must scroll inside its container instead;
 *   1. no body cell is narrower than 48 px, and no cell's text wraps to a handful of characters
 *      per line (the average line of any cell holds at least four characters; one character per
 *      line is the defect);
 *   2. every status badge renders on ONE line (height no more than one line plus its own padding
 *      and border) and sits inside its cell, so nothing is clipped;
 *   3. a table wider than its container is scrolled by the container, and the page itself never
 *      scrolls sideways.
 *
 * Widths are the phone (390 x 844) and the tablet (768 x 1024); languages are English and
 * Armenian, whose strings and glyph widths differ most from the default.
 */

/** The viewports the table kit must hold at. */
const VIEWPORTS = [
  { name: 'phone', width: 390, height: 844 },
  { name: 'tablet', width: 768, height: 1024 },
] as const

/** The interface languages measured. */
const LOCALES = ['en', 'hy'] as const

/** The status words each locale renders, so a badge can be found by its text and not by a class. */
const LABELS = {
  en: {
    active: 'Active',
    suspended: 'Suspended',
    completed: 'Completed',
    failed: 'Failed',
    running: 'Running',
    succeeded: 'Succeeded',
  },
  hy: {
    active: 'Ակտիվ',
    suspended: 'Կասեցված',
    completed: 'Ավարտված',
    failed: 'Ձախողված',
    running: 'Կատարվում է',
    succeeded: 'Հաջողվեց',
  },
} as const

/** Every module licensed, so nothing is gated away. */
const MODULES: PanelModule[] = [
  'accounts',
  'sites',
  'databases',
  'sftp',
  'ftp',
  'cron',
  'firewall',
  'backups',
  'monitoring',
  'tasks',
].map((name) => {
  return { name, displayName: name, tier: 'included', isEnabled: true }
})

/** A long name with no break opportunity the layout can rely on, and the domain a customer would type. */
const LONG_ACCOUNT = 'acme-industrial-holdings'
const LONG_DOMAIN = 'storefront-staging-environment.acme-industrial-holdings.example.com'

const ACCOUNTS: Account[] = [
  {
    id: '00000000-0000-0000-0000-0000000000c1',
    name: LONG_ACCOUNT,
    primaryDomain: LONG_DOMAIN,
    planId: THREE_PLANS[1]?.id ?? '',
    status: 'active',
    createdAt: '2026-08-30T09:00:00+00:00',
  },
  {
    id: '00000000-0000-0000-0000-0000000000c2',
    name: 'bob',
    primaryDomain: 'bob.example.com',
    planId: THREE_PLANS[1]?.id ?? '',
    status: 'suspended',
    createdAt: '2026-08-31T09:00:00+00:00',
  },
]

const BACKUPS: Backup[] = [
  {
    id: '77777777-7777-7777-7777-777777777771',
    accountId: ACCOUNTS[0]?.id ?? '',
    orphanedAccountUsername: '',
    status: 'completed',
    kind: 'scheduled',
    sizeBytes: 3_221_225_472,
    sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
    databaseCount: 2,
    startedAt: '2026-09-06T03:00:00Z',
    finishedAt: '2026-09-06T03:04:00Z',
    failureCode: '',
    failureDisplayName: '',
  },
  {
    id: '77777777-7777-7777-7777-777777777772',
    accountId: ACCOUNTS[0]?.id ?? '',
    orphanedAccountUsername: '',
    status: 'failed',
    kind: 'manual',
    sizeBytes: 0,
    sha256: '',
    databaseCount: 0,
    startedAt: '2026-09-07T03:00:00Z',
    finishedAt: '2026-09-07T03:01:00Z',
    failureCode: 'DestinationUnreachable',
    failureDisplayName: 'The backup destination could not be reached',
  },
  {
    id: '77777777-7777-7777-7777-777777777773',
    accountId: '00000000-0000-0000-0000-0000000000ff',
    orphanedAccountUsername: 'removed-customer-account-name',
    status: 'completed',
    kind: 'preDeletion',
    sizeBytes: 1_572_864,
    sha256: 'b1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
    databaseCount: 1,
    startedAt: '2026-09-05T03:00:00Z',
    finishedAt: '2026-09-05T03:02:00Z',
    failureCode: '',
    failureDisplayName: '',
  },
]

const AUDIT_EVENTS: AuditEvent[] = [
  {
    id: '00000000-0000-0000-0000-0000000000e1',
    occurredAt: '2026-09-08T18:14:00+00:00',
    actorUsername: 'administrator-with-a-long-login-name',
    action: 'BackupRestoredFromRemoteDestination',
    actionName: 'BackupRestoredFromRemoteDestination',
    subject: 'customer-account-with-a-rather-long-name',
    ipAddress: '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
    succeeded: true,
  },
  {
    id: '00000000-0000-0000-0000-0000000000e2',
    occurredAt: '2026-09-08T18:13:00+00:00',
    actorUsername: 'admin',
    action: 'LoginFailed',
    actionName: 'LoginFailed',
    subject: 'admin',
    ipAddress: '198.51.100.4',
    succeeded: false,
  },
]

const TASKS: PanelTask[] = [
  {
    id: '99999999-9999-9999-9999-999999999991',
    kind: 'backup.create',
    subject: LONG_ACCOUNT + '-' + LONG_DOMAIN,
    correlationId: null,
    status: 'running',
    percent: 42,
    log: 'Compressing the account home directory\n',
    errorCode: null,
    startedAt: '2026-09-01T10:00:00Z',
    finishedAt: null,
    revision: 3,
  },
  {
    id: '99999999-9999-9999-9999-999999999992',
    kind: 'account.delete',
    subject: 'bob',
    correlationId: null,
    status: 'failed',
    percent: 100,
    log: 'The agent refused the request\n',
    errorCode: 'AgentUnavailable',
    startedAt: '2026-09-01T09:00:00Z',
    finishedAt: '2026-09-01T09:01:00Z',
    revision: 5,
  },
  {
    id: '99999999-9999-9999-9999-999999999993',
    kind: 'site.create',
    subject: LONG_DOMAIN,
    correlationId: null,
    status: 'completed',
    percent: 100,
    log: 'Done\n',
    errorCode: null,
    startedAt: '2026-09-01T08:00:00Z',
    finishedAt: '2026-09-01T08:01:00Z',
    revision: 7,
  },
]

/** One screen under test. */
interface Screen {
  /** The screen's name in the test title. */
  name: string
  /** The path to open. */
  path: string
  /** A value from the stub that must be on screen before measuring. */
  marker: string
  /** Long machine identifiers on screen that must each render on a single line. */
  identifiers: readonly string[]
  /** The status words (keys of {@link LABELS}) this screen renders as badges. */
  badges: ReadonlyArray<keyof (typeof LABELS)['en']>
}

const SCREENS: Screen[] = [
  {
    name: 'accounts list',
    path: '/accounts',
    marker: LONG_ACCOUNT,
    identifiers: [LONG_ACCOUNT, LONG_DOMAIN],
    badges: ['active', 'suspended'],
  },
  {
    name: 'backups list',
    path: '/backups',
    marker: 'removed-customer-account-name',
    identifiers: ['removed-customer-account-name'],
    badges: ['completed', 'failed'],
  },
  {
    name: 'audit journal',
    path: '/settings/audit',
    marker: '198.51.100.4',
    identifiers: [
      'administrator-with-a-long-login-name',
      'customer-account-with-a-rather-long-name',
      'BackupRestoredFromRemoteDestination',
      '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
    ],
    badges: ['succeeded', 'failed'],
  },
  {
    name: 'tasks',
    path: '/tasks',
    marker: 'backup.create',
    identifiers: [LONG_ACCOUNT + '-' + LONG_DOMAIN, LONG_DOMAIN],
    badges: ['running', 'failed', 'completed'],
  },
]

/**
 * Installs the shell and every endpoint the four screens read.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubScreens = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubTasks(page, TASKS)
  await stubTaskStream(page, taskFrame(TASKS[0]) + taskEndFrame('completed'))
  await stubPlans(page, THREE_PLANS)
  await stubAccounts(page, [...ACCOUNTS])
  await stubBackups(page, [...BACKUPS])
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(AUDIT_EVENTS) })
  })
}

/** What one cell measurement reports. */
interface CellFacts {
  /** The cell's text, trimmed, for the failure message. */
  text: string
  /** The cell's box width in px. */
  width: number
  /** How many text lines the cell's content occupies. */
  lines: number
  /** The number of characters on the cell's text. */
  characters: number
}

/**
 * Measures every body cell of the first table on the page.
 * @param page The Playwright page, already loaded.
 * @returns One fact record per body cell that carries text.
 */
const measureCells = async (page: Page): Promise<CellFacts[]> => {
  return page.evaluate(() => {
    const facts: CellFacts[] = []
    for (const cell of document.querySelectorAll('main table tbody td')) {
      const text = (cell.textContent ?? '').replace(/\s+/g, ' ').trim()
      if (text === '') {
        continue
      }
      const tops = new Set<number>()
      const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
      for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
        if ((node.textContent ?? '').trim() === '') {
          continue
        }
        const range = document.createRange()
        range.selectNodeContents(node)
        for (const rect of range.getClientRects()) {
          if (rect.width > 0 && rect.height > 0) {
            tops.add(Math.round(rect.top / 4))
          }
        }
      }
      facts.push({
        text,
        width: cell.getBoundingClientRect().width,
        lines: tops.size,
        characters: text.length,
      })
    }
    return facts
  })
}

/**
 * Counts the lines an identifier occupies on screen.
 * @param page The Playwright page, already loaded.
 * @param identifier The identifier's exact text, as it appears inside one text node of a body cell.
 * @returns The most line boxes (by vertical position) any one occurrence of it occupies, and those
 *   boxes' tops, or `lines: 0` when no body cell carries it. Occurrences are measured one by one: the
 *   same domain can sit in two rows, and two rows are not one folded identifier.
 */
const measureIdentifier = async (
  page: Page,
  identifier: string,
): Promise<{ lines: number; tops: number[] }> => {
  return page.evaluate((needle) => {
    let widest: number[] = []
    for (const cell of document.querySelectorAll('main table tbody td')) {
      const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
      for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
        const content = node.textContent ?? ''
        const start = content.indexOf(needle)
        if (start < 0) {
          continue
        }
        const tops = new Set<number>()
        // Measure the identifier's own characters, not the whole node: a node can carry a label
        // before it, and only the identifier is required to stay on one line.
        const range = document.createRange()
        range.setStart(node, start)
        range.setEnd(node, start + needle.length)
        for (const rect of range.getClientRects()) {
          if (rect.width > 0 && rect.height > 0) {
            tops.add(Math.round(rect.top))
          }
        }
        if (tops.size > widest.length) {
          widest = [...tops]
        }
      }
    }
    return { lines: widest.length, tops: widest }
  }, identifier)
}

/**
 * Reports whether a badge sits on one line and inside its cell.
 * @param badge The badge element, found by its status word.
 * @returns Its box, one line's height and the cell's box.
 */
const measureBadge = async (
  badge: Locator,
): Promise<{
  height: number
  oneLine: number
  left: number
  right: number
  cellLeft: number
  cellRight: number
}> => {
  return badge.evaluate((element) => {
    const style = getComputedStyle(element)
    const fontSize = parseFloat(style.fontSize)
    const lineHeight = style.lineHeight === 'normal' ? fontSize * 1.3 : parseFloat(style.lineHeight)
    const oneLine =
      lineHeight +
      parseFloat(style.paddingTop) +
      parseFloat(style.paddingBottom) +
      parseFloat(style.borderTopWidth) +
      parseFloat(style.borderBottomWidth)
    const box = element.getBoundingClientRect()
    const cell = element.closest('td')?.getBoundingClientRect() ?? box
    return {
      height: box.height,
      oneLine,
      left: box.left,
      right: box.right,
      cellLeft: cell.left,
      cellRight: cell.right,
    }
  })
}

for (const viewport of VIEWPORTS) {
  for (const locale of LOCALES) {
    for (const screen of SCREENS) {
      test(`${screen.name} table holds up at ${viewport.name} width in ${locale}`, async ({ page }) => {
        await page.setViewportSize({ width: viewport.width, height: viewport.height })
        await setPersistedLocale(page, locale)
        await stubScreens(page)

        await page.goto(screen.path)
        const table = page.getByRole('main').getByRole('table')
        await expect(table).toBeVisible()
        await expect(table.getByText(screen.marker).first()).toBeVisible()

        // 0. Every long machine identifier is one unit: one line, in every table, width and language.
        for (const identifier of screen.identifiers) {
          const facts = await measureIdentifier(page, identifier)
          expect(facts.lines, `"${identifier}" is not on screen in a table cell`).toBeGreaterThan(0)
          expect(
            facts.lines,
            `"${identifier}" is folded over ${facts.lines} lines (tops ${facts.tops.join(', ')})`,
          ).toBe(1)
        }

        // 1. No cell is squeezed into a one-character column.
        const cells = await measureCells(page)
        expect(cells.length).toBeGreaterThan(0)
        for (const cell of cells) {
          expect(cell.width, `cell "${cell.text}" is only ${cell.width}px wide`).toBeGreaterThanOrEqual(48)
          if (cell.characters >= 8) {
            expect(
              cell.characters / cell.lines,
              `cell "${cell.text}" wraps ${cell.characters} characters over ${cell.lines} lines`,
            ).toBeGreaterThanOrEqual(4)
          }
        }
        // 2. Every status badge is a single line and sits inside its cell.
        let badgeCount = 0
        for (const key of screen.badges) {
          const badges = table.getByRole('cell').getByText(LABELS[locale][key], { exact: true })
          for (let index = 0; index < (await badges.count()); index += 1) {
            badgeCount += 1
            const facts = await measureBadge(badges.nth(index))
            expect(
              facts.height,
              `badge "${LABELS[locale][key]}" is ${facts.height}px tall`,
            ).toBeLessThanOrEqual(facts.oneLine + 1)
            expect(
              facts.left,
              `badge "${LABELS[locale][key]}" starts outside its cell`,
            ).toBeGreaterThanOrEqual(facts.cellLeft - 1)
            expect(facts.right, `badge "${LABELS[locale][key]}" ends outside its cell`).toBeLessThanOrEqual(
              facts.cellRight + 1,
            )
          }
        }
        expect(badgeCount, 'the screen rendered no status badge to measure').toBeGreaterThan(0)

        // 3. A table wider than its container is scrolled by the container, never by the page.
        const scrolling = await table.evaluate((element) => {
          const container = element.parentElement as HTMLElement
          return {
            tableWidth: element.getBoundingClientRect().width,
            clientWidth: container.clientWidth,
            scrollWidth: container.scrollWidth,
            overflowX: getComputedStyle(container).overflowX,
            documentWidth: document.documentElement.scrollWidth,
            viewportWidth: window.innerWidth,
          }
        })
        expect(scrolling.documentWidth, 'the page scrolls sideways').toBeLessThanOrEqual(
          scrolling.viewportWidth,
        )
        if (scrolling.tableWidth > scrolling.clientWidth + 1) {
          expect(scrolling.scrollWidth, 'the wide table does not scroll its container').toBeGreaterThan(
            scrolling.clientWidth,
          )
          expect(['auto', 'scroll']).toContain(scrolling.overflowX)
        }
      })
    }
  }
}

test('the audit journal at phone width is wider than its container, so the container scrolling is really exercised', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubScreens(page)

  await page.goto('/settings/audit')
  const table = page.getByRole('main').getByRole('table')
  await expect(table.getByText('198.51.100.4')).toBeVisible()

  const widths = await table.evaluate((element) => {
    const container = element.parentElement as HTMLElement
    return { tableWidth: element.getBoundingClientRect().width, clientWidth: container.clientWidth }
  })
  expect(widths.tableWidth).toBeGreaterThan(widths.clientWidth)
})

/**
 * Presses Tab until the given element holds focus.
 * @param page The Playwright page, already loaded.
 * @param target The element expected to be reachable by keyboard.
 * @returns True when the element received focus within a bounded number of presses.
 */
const tabTo = async (page: Page, target: Locator): Promise<boolean> => {
  for (let press = 0; press < 60; press += 1) {
    await page.keyboard.press('Tab')
    if (await target.evaluate((element) => {
      return element === document.activeElement
    })) {
      return true
    }
  }
  return false
}

test('an overflowing table is a labelled scroll region that takes the design focus ring when tabbed to', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubScreens(page)

  await page.goto('/settings/audit')
  const table = page.getByRole('main').getByRole('table')
  await expect(table.getByText('198.51.100.4')).toBeVisible()

  const scroller = page.getByRole('main').getByRole('region')
  await expect(scroller).toHaveCount(1)
  await expect(scroller).toHaveAccessibleName(/\S/)
  await expect(scroller).toHaveAttribute('tabindex', '0')

  expect(await tabTo(page, scroller), 'the overflowing table is not reachable with Tab').toBe(true)
  const ring = await scroller.evaluate((element) => {
    const style = getComputedStyle(element)
    return { boxShadow: style.boxShadow, outlineStyle: style.outlineStyle }
  })
  expect(ring.boxShadow, 'the scroll region shows no design focus ring').not.toBe('none')
  expect(ring.outlineStyle, 'the browser outline is drawn instead of the design ring').toBe('none')
})

test('a table that fits its container is not a Tab stop and is not announced as a region', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await stubScreens(page)

  await page.goto('/accounts')
  const table = page.getByRole('main').getByRole('table')
  await expect(table.getByText('bob').first()).toBeVisible()

  const facts = await table.evaluate((element) => {
    const container = element.parentElement as HTMLElement
    return {
      fits: container.scrollWidth <= container.clientWidth,
      tabindex: container.getAttribute('tabindex'),
      role: container.getAttribute('role'),
    }
  })
  expect(facts.fits, 'the accounts table is expected to fit at 1920px').toBe(true)
  expect(facts.tabindex).toBeNull()
  expect(facts.role).toBeNull()
  await expect(page.getByRole('main').getByRole('region')).toHaveCount(0)
})
