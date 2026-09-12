import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { AuditEvent } from '../../src/types/audit'

/**
 * The exact row the defect was measured on in a live browser: a Russian screen rendering the
 * machine constant `BackupRestored` in its Action column. The backend now sends the localized
 * name beside the constant, and this spec pins the Russian VALUE — an assertion on "some text"
 * would pass against the raw constant, which IS the defect.
 */
const RESTORED: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000c1',
  occurredAt: '2026-09-08T18:14:00+00:00',
  actorUsername: 'liveadmin',
  action: 'BackupRestored',
  actionName: 'Аккаунт восстановлен из резервной копии',
  subject: 'livecust',
  ipAddress: '127.0.0.1',
  succeeded: true,
}

/**
 * An action this panel build has no name for: the backend falls back to the constant, and the
 * cell must show the constant once — never a resx key, never an empty cell.
 */
const MARKETPLACE: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000c2',
  occurredAt: '2026-09-08T18:13:00+00:00',
  actorUsername: 'liveadmin',
  action: 'SomeMarketplaceModuleAction',
  actionName: 'SomeMarketplaceModuleAction',
  subject: 'livecust',
  ipAddress: '127.0.0.1',
  succeeded: false,
}

/**
 * Fulfils the endpoints a Russian admin session needs, plus `GET /api/v1/audit` with a chosen
 * journal.
 * @param page The Playwright page whose network the routes are installed on.
 * @param events The entries the panel reports.
 * @returns Resolves once the routes are installed.
 */
const stubRussianJournal = async (page: Page, events: AuditEvent[]): Promise<void> => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(events) })
  })
}

test('the action column shows the backend-localized russian name beside the machine code', async ({ page }) => {
  await stubRussianJournal(page, [RESTORED])

  await page.goto('/settings/audit')

  // The VALUE, in Russian — the half that was measured missing.
  await expect(page.getByText('Аккаунт восстановлен из резервной копии')).toBeVisible()
  // The machine constant stays visible beside it: it is what an administrator greps a log by.
  await expect(page.getByText('BackupRestored')).toBeVisible()
  // The subject the fixed producer records — an account name, not a GUID.
  await expect(page.getByText('livecust')).toBeVisible()
})

test('an action the build has no name for renders as its constant exactly once', async ({ page }) => {
  await stubRussianJournal(page, [MARKETPLACE])

  await page.goto('/settings/audit')

  // Once, not twice: name equal to constant means one line is enough — and never a dotted key.
  await expect(page.getByText('SomeMarketplaceModuleAction')).toHaveCount(1)
})
