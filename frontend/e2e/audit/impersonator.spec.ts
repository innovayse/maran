import { expect, test } from '../fixtures/guarded-test'
import { stubAudit, viewedAs } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { ALERT_DELIVERY_OK, loaded, ONE_SHOT, stubDashboard, SYNCHRONIZED_CLOCK, UNAVAILABLE } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { DEFAULT_LAYOUT, installLiveStream, stubDashboardLayout } from '../fixtures/stub-live-dashboard-stream'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { AuditEvent } from '../../src/types/audit'
import type { Dashboard } from '../../src/types/dashboard'
import type { Page } from '@playwright/test'

// An entry written while an administrator was viewing the panel as someone else names that
// administrator on the journal and on the dashboard's latest-activity card (issue #327).

/** An entry its actor wrote for themselves: no operator, so no second line. */
const SELF_ACTED: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e1',
  occurredAt: '2026-09-10T09:00:00+00:00',
  actorUsername: 'carol',
  impersonatorUsername: null,
  action: 'SiteCreated',
  actionName: 'Site created',
  subject: 'carol.example',
  subjectName: 'carol.example',
  ipAddress: '203.0.113.21',
  succeeded: true,
  outcomeName: 'Succeeded',
}

/** An entry written by the administrator `operator` while viewing as `dave`. */
const VIEWED_AS: AuditEvent = viewedAs(
  {
    ...SELF_ACTED,
    id: '00000000-0000-0000-0000-0000000000e2',
    occurredAt: '2026-09-10T08:00:00+00:00',
    actorUsername: 'dave',
    subject: 'dave.example',
    subjectName: 'dave.example',
    ipAddress: '198.51.100.22',
  },
  'operator',
)

/** The locales the panel ships, with the words each uses to reach the journal and to say "via". */
const LOCALES = [
  { code: 'en', navigation: 'Main navigation', expand: 'Show the Monitoring pages', journal: 'Audit journal', via: 'via operator' },
  { code: 'ru', navigation: 'Основная навигация', expand: 'Показать разделы «Мониторинг»', journal: 'Журнал аудита', via: 'через operator' },
  { code: 'hy', navigation: 'Հիմնական նավիգացիա', expand: 'Ցույց տալ «Մոնիտորինգ» բաժինները', journal: 'Աուդիտի մատյան', via: 'operator-ի միջոցով' },
] as const

/**
 * Signs an administrator in with the shell's endpoints answered and the journal holding the two entries.
 * @param page The Playwright page.
 * @param locale The interface language to start in.
 * @returns Resolves once the routes are installed.
 */
const openShell = async (page: Page, locale: 'en' | 'ru' | 'hy'): Promise<void> => {
  await setPersistedLocale(page, locale)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  // The panel opens on the system status screen before the journal is reached by a click.
  await stubDashboard(page, ONE_SHOT)
  await stubDashboardLayout(page, DEFAULT_LAYOUT)
  await installLiveStream(page)
  await stubAudit(page, [SELF_ACTED, VIEWED_AS])
}

for (const locale of LOCALES) {
  test(`the journal names the operator under the actor, in ${locale.code}`, async ({ page }) => {
    await openShell(page, locale.code)

    await page.goto('/')
    const navigation = page.getByRole('navigation', { name: locale.navigation })
    await navigation.getByRole('button', { name: locale.expand }).click()
    await navigation.getByRole('link', { name: locale.journal }).click()

    const viewedRow = page.getByRole('row').filter({ hasText: VIEWED_AS.ipAddress })
    await expect(viewedRow.getByText('dave', { exact: true })).toBeVisible()
    await expect(viewedRow.getByText(locale.via, { exact: true })).toBeVisible()

    // The control on the same table: the row loaded, and an entry its actor wrote has no such line.
    const selfRow = page.getByRole('row').filter({ hasText: SELF_ACTED.ipAddress })
    await expect(selfRow.getByText('carol', { exact: true })).toBeVisible()
    await expect(selfRow.getByText(locale.via)).toHaveCount(0)
  })
}

test('searching for the operator finds the entry written while viewing as someone else', async ({ page }) => {
  await openShell(page, 'en')

  await page.goto('/')
  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await navigation.getByRole('button', { name: 'Show the Monitoring pages' }).click()
  await navigation.getByRole('link', { name: 'Audit journal' }).click()
  await expect(page.getByRole('row').filter({ hasText: SELF_ACTED.ipAddress })).toBeVisible()

  await page.getByRole('searchbox', { name: 'Search the journal' }).fill('operator')

  await expect(page.getByRole('row').filter({ hasText: VIEWED_AS.ipAddress })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: SELF_ACTED.ipAddress })).toHaveCount(0)
})

test('the dashboard card names the operator on an entry written while viewing as someone', async ({ page }) => {
  const dashboard: Dashboard = {
    audience: 'administrator',
    resources: UNAVAILABLE,
    services: UNAVAILABLE,
    counts: { accounts: loaded(1), sites: loaded(1), databases: loaded(1) },
    attention: {
      certificatesExpiringSoon: loaded(0),
      failedBackups: loaded(0),
      sitesOnEndOfLifePhp: loaded(0),
      alertDelivery: ALERT_DELIVERY_OK,
      bannedAddresses: loaded(0),
      failedTasks: loaded(0),
      clock: SYNCHRONIZED_CLOCK,
    },
    recentAudit: loaded([VIEWED_AS, SELF_ACTED]),
  }
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubDashboard(page, dashboard)
  // The server's dashboard reads the person's layout and opens its live stream; neither is the subject.
  await stubDashboardLayout(page, DEFAULT_LAYOUT)
  await installLiveStream(page)

  await page.goto('/')

  // One line, for the one entry that has an operator: the self-acted entry beside it prints none.
  await expect(page.getByText('via operator', { exact: true })).toHaveCount(1)
})
