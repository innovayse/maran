import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from './fixtures/set-locale'
import { stubSignedIn } from './fixtures/stub-auth-routes'
import { stubDashboard } from './fixtures/stub-dashboard-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubModules } from './fixtures/stub-modules-route'
import type { Dashboard } from '../src/types/dashboard'
import type { PanelModule } from '../src/types/module'

// #212: a counted message chooses its form from the count. Russian has three forms and the choice
// is made on the last digit, so 1 and 5 land in different forms and a message that ignored the count
// ("1 задач завершились с ошибкой") is caught by either half.

const LICENSED: PanelModule[] = [
  { name: 'accounts', displayName: 'Аккаунты', tier: 'included', isEnabled: true },
]

/**
 * A dashboard whose four attention counts are all the same number.
 * @param count The number every attention row reports.
 * @returns The dashboard to stub.
 */
const dashboardWith = (count: number): Dashboard => {
  return {
    isAdministrator: true,
    resources: null,
    services: [],
    counts: {
    accounts: { state: 'counted', count: 1 },
    sites: { state: 'counted', count: 1 },
    databases: { state: 'counted', count: 1 },
  },
    attention: {
      certificatesExpiringSoon: count,
      failedBackups: count,
      bannedAddresses: count,
      failedTasks: count,
    },
    recentAudit: [],
  }
}

/**
 * Opens the Russian system status page with the given attention counts.
 * @param page The page under test.
 * @param count The number every attention row reports.
 * @returns Resolves once the page has been requested.
 */
const openInRussian = async (page: Page, count: number): Promise<void> => {
  await setPersistedLocale(page, 'ru')
  await stubModules(page, LICENSED)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubDashboard(page, dashboardWith(count))
  await page.goto('/')
}

test('Needs attention uses the singular form for 1 in Russian', async ({ page }) => {
  await openInRussian(page, 1)

  await expect(page.getByRole('link', { name: '1 задача завершилась с ошибкой' })).toBeVisible()
  await expect(page.getByRole('link', { name: '1 адрес заблокирован' })).toBeVisible()
  await expect(page.getByRole('link', { name: '1 резервная копия не удалась' })).toBeVisible()
  await expect(page.getByRole('link', { name: '1 сертификат скоро истекает' })).toBeVisible()
})

test('Needs attention uses the many form for 5 in Russian', async ({ page }) => {
  await openInRussian(page, 5)

  await expect(page.getByRole('link', { name: '5 задач завершились с ошибкой' })).toBeVisible()
  await expect(page.getByRole('link', { name: '5 адресов заблокировано' })).toBeVisible()
  await expect(page.getByRole('link', { name: '5 резервных копий не удалось' })).toBeVisible()
  await expect(page.getByRole('link', { name: '5 сертификатов скоро истекают' })).toBeVisible()
})

test('Needs attention uses the singular form for 1 in English', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await stubModules(page, LICENSED)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubDashboard(page, dashboardWith(1))
  await page.goto('/')

  await expect(page.getByRole('link', { name: '1 task failed' })).toBeVisible()
  await expect(page.getByRole('link', { name: '1 address banned' })).toBeVisible()
})
