import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccountResources } from '../fixtures/stub-account-resources-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { loaded, stubDashboard, SYNCHRONIZED_CLOCK } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { stubTasks, stubTaskStream } from '../fixtures/stub-tasks-routes'
import type { Account, AccountDetail } from '../../src/types/account'
import type { Dashboard } from '../../src/types/dashboard'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

/*
 * Issue #244: every label in the panel reads in sentence case. Field labels, table headers,
 * badges, page titles and their eyebrows used to be set in capitals by CSS with wide caps
 * tracking, so the task list showed `ԱՎԱՐՏՎԱԾ` where the translation says `Ավարտված`.
 *
 * Measured on the computed style, never on a class name: `text-transform` must be `none`, and the
 * letter-spacing must not be the positive tracking capitals were given. The body's own -0.005em and
 * the title's -0.02em (`tracking-title`) are optical tightening of mixed-case text, not caps
 * tracking, so the check is "no positive letter-spacing" rather than the keyword `normal`, which no
 * text in the panel computes to. `innerText` reflects CSS transforms, so the badge's text proves
 * what the reader actually sees.
 */

/** The three interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

/** The completed-task badge as each locale's tasks translation writes it. */
const COMPLETED: Record<(typeof LOCALES)[number], string> = {
  en: 'Completed',
  ru: 'Завершена',
  hy: 'Ավարտված',
}

/** The desktop and the phone widths the issue names. */
const VIEWPORTS = [
  { name: '1440', size: { width: 1440, height: 900 } },
  { name: '390', size: { width: 390, height: 844 } },
] as const

const MODULES: PanelModule[] = ['accounts', 'tasks'].map((name) => {
  return { name, displayName: name, tier: 'included', isEnabled: true }
})

const ACCOUNT: Account = {
  id: '00000000-0000-0000-0000-0000000000d1',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: THREE_PLANS[0]?.id ?? '',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

const ACCOUNT_DETAIL: AccountDetail = {
  ...ACCOUNT,
  planDisplayName: THREE_PLANS[0]?.displayName ?? '',
  ownerEmail: 'alice@example.com',
}

const TASK: PanelTask = {
  id: '44444444-4444-4444-4444-444444444444',
  kind: 'backup.create',
  subject: 'alice',
  correlationId: 'c0rr-s3nt3nc3',
  status: 'completed',
  percent: 100,
  log: '',
  logIncluded: true,
  failure: null,
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: '2026-09-03T09:01:00Z',
  revision: 3,
}

const DASHBOARD: Dashboard = {
  isAdministrator: true,
  resources: loaded({
    cpuPercent: 12,
    memoryUsedBytes: 1024 * 1024 * 1024,
    memoryTotalBytes: 4 * 1024 * 1024 * 1024,
    diskUsedBytes: 10 * 1024 * 1024 * 1024,
    diskReservedBytes: 2 * 1024 * 1024 * 1024,
    diskAvailableBytes: 38 * 1024 * 1024 * 1024,
    diskTotalBytes: 50 * 1024 * 1024 * 1024,
    networkRxBytes: 1_000_000,
    networkTxBytes: 2_000_000,
    loadAverage1m: 0.1,
    loadAverage5m: 0.2,
    loadAverage15m: 0.3,
  }),
  services: loaded([{ service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' }]),
  counts: {
    accounts: loaded(1),
    sites: loaded(2),
    databases: loaded(3),
  },
  attention: { certificatesExpiringSoon: loaded(0), failedBackups: loaded(0), bannedAddresses: loaded(0), failedTasks: loaded(0), clock: SYNCHRONIZED_CLOCK },
  recentAudit: loaded([]),
}

/** One screen under test. */
interface Screen {
  /** What the screen is called in the test title. */
  name: string
  /** The path to open. */
  path: string
  /** The kinds of text the screen must show, so an empty match cannot pass the check. */
  expects: ('label' | 'th' | 'badge' | 'h1' | 'eyebrow')[]
}

const SCREENS: Screen[] = [
  { name: 'account create form', path: '/accounts/new', expects: ['label', 'h1'] },
  { name: 'accounts list', path: '/accounts', expects: ['th', 'badge', 'h1'] },
  { name: 'tasks list', path: '/tasks', expects: ['th', 'badge', 'h1'] },
  { name: 'system status', path: '/', expects: ['h1', 'eyebrow'] },
  { name: 'account detail', path: `/accounts/${ACCOUNT.id}`, expects: ['badge', 'h1', 'eyebrow'] },
]

/** The selector for each kind of text the check covers. */
const SELECTORS: Record<Screen['expects'][number], string> = {
  label: 'main label',
  th: 'main th',
  badge: 'main [data-ui="badge"]',
  h1: 'main h1',
  eyebrow: 'main [data-ui="eyebrow"]',
}

/** What one element's computed type says, for the failure message. */
interface Offender {
  kind: string
  text: string
  textTransform: string
  letterSpacing: string
}

/**
 * Registers every stub the five screens read.
 * @param page The Playwright page to stub.
 * @param locale The interface language to start in.
 * @returns Resolves once every route is registered.
 */
const stubEverything = async (page: Page, locale: (typeof LOCALES)[number]): Promise<void> => {
  await setPersistedLocale(page, locale)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubDashboard(page, DASHBOARD)
  await stubTasks(page, [TASK])
  await stubTaskStream(page, '')
  await stubPlans(page, THREE_PLANS)
  await stubAccounts(page, [ACCOUNT])
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT_DETAIL) })
  })
  await stubAccountResources(page, { accountId: ACCOUNT.id, resources: [] })
  await stubInvitation(page, ACCOUNT.id, 'pending')
}

/**
 * Collects every element of the covered kinds whose computed type sets it in capitals or tracks it.
 * @param page The Playwright page, already loaded.
 * @returns The offenders, empty when the screen reads in sentence case.
 */
const capitalised = async (page: Page): Promise<Offender[]> => {
  return page.evaluate((selectors) => {
    const offenders: Offender[] = []
    for (const [kind, selector] of Object.entries(selectors)) {
      for (const element of document.querySelectorAll(selector)) {
        const style = getComputedStyle(element)
        const spacing = style.letterSpacing === 'normal' ? 0 : Number.parseFloat(style.letterSpacing)
        if (style.textTransform !== 'none' || spacing > 0) {
          offenders.push({
            kind,
            text: (element.textContent ?? '').trim().slice(0, 40),
            textTransform: style.textTransform,
            letterSpacing: style.letterSpacing,
          })
        }
      }
    }
    return offenders
  }, SELECTORS)
}

for (const viewport of VIEWPORTS) {
  test.describe(`sentence case at ${viewport.name} px`, () => {
    test.use({ viewport: viewport.size })

    for (const locale of LOCALES) {
      for (const screen of SCREENS) {
        test(`${screen.name} sets no text in capitals (${locale})`, async ({ page }) => {
          await stubEverything(page, locale)
          await page.goto(screen.path)
          await expect(page.locator('main h1')).toBeVisible()

          for (const kind of screen.expects) {
            await expect(page.locator(SELECTORS[kind]).first()).toBeAttached()
          }

          expect(await capitalised(page)).toEqual([])
        })
      }

      test(`the completed task badge reads as translated (${locale})`, async ({ page }) => {
        await stubEverything(page, locale)
        await page.goto('/tasks')

        const badge = page.locator('main td [data-ui="badge"]').first()
        await expect(badge).toBeVisible()
        expect(await badge.evaluate((element) => {return (element as HTMLElement).innerText.trim()})).toBe(COMPLETED[locale])
      })
    }
  })
}
