import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubTaskStream, stubTasks, taskEndFrame, taskFrame } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

// #222: a failed task reads as a sentence in the operator's language with the code as the detail,
// and its progress bar stops looking active.

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

const FAILED_CERTIFICATE: PanelTask = {
  id: '55555555-5555-5555-5555-555555555555',
  kind: 'CertificateIssue',
  subject: 'shop.example.com',
  correlationId: 'c0rr-cert',
  status: 'failed',
  percent: 20,
  log: 'registering the account\n',
  logIncluded: true,
  errorCode: 'AcmeAccountRejected',
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: '2026-09-03T09:01:00Z',
  revision: 4,
}

/** The row's menu trigger and its live-view item, as each interface language names them. */
const LABELS: Record<'en' | 'ru', { actions: string; liveView: string }> = {
  en: { actions: `Actions for ${FAILED_CERTIFICATE.subject}`, liveView: 'Live view' },
  ru: { actions: `Действия для ${FAILED_CERTIFICATE.subject}`, liveView: 'Наблюдать' },
}

/**
 * Opens the tasks screen on the failed task's live pane.
 * @param page The page under test.
 * @param language The interface language already persisted, which names the controls clicked.
 * @returns Resolves once the pane was asked for.
 */
const openFailure = async (page: Page, language: 'en' | 'ru' = 'en'): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [FAILED_CERTIFICATE])
  await stubTaskStream(page, taskFrame(FAILED_CERTIFICATE) + taskEndFrame('failed'))
  await page.goto('/tasks')
  await page.getByRole('button', { name: LABELS[language].actions }).click()
  await page.getByRole('menuitem', { name: LABELS[language].liveView }).click()
}

test('a known failure code reads as a sentence, with the code beneath it', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openFailure(page)

  await expect(page.getByText("The certificate authority refused this server's registration.")).toBeVisible()
  await expect(page.getByText('Code: AcmeAccountRejected', { exact: true })).toBeVisible()
  await expect(page.getByText('The operation failed with the code')).toHaveCount(0)
})

test('the sentence is in the operator language', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await openFailure(page, 'ru')

  await expect(page.getByText('Код: AcmeAccountRejected', { exact: true })).toBeVisible()
  await expect(page.locator('body')).not.toContainText("refused this server's registration")
})

test('a failed task draws no progress bar, in the pane or in its row', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openFailure(page)

  await expect(page.getByTestId('task-failed-progress')).toHaveText('Stopped at 20%')
  await expect(page.getByRole('progressbar')).toHaveCount(0)
  // The figure stays in the row as a record of how far it got.
  await expect(page.getByRole('row').filter({ hasText: FAILED_CERTIFICATE.subject })).toContainText('20%')
})
