import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubTaskStream, stubTasks, taskEndFrame, taskFrame } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

// #222, #272: a failed task reads as the panel's own sentence — already in the operator's language,
// because the backend resolves it from the stored code each time the task is read — with the code as
// the detail, and its progress bar stops looking active. The SPA holds no failure text of its own.

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
  failure: {
    code: 'AcmeAccountRejected',
    message: "The certificate authority refused this server's registration.",
  },
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: '2026-09-03T09:01:00Z',
  revision: 4,
}

/** The row's menu trigger and its live-view item, as each interface language names them. */
/** The same failure as the panel words it in Russian and in Armenian. */
const SENTENCES: Record<'ru' | 'hy', string> = {
  ru: 'Центр сертификации отклонил регистрацию этого сервера.',
  hy: 'Վկայագրման կենտրոնը մերժեց այս սերվերի գրանցումը։',
}

/**
 * The failed task as the panel sends it to a reader of one language.
 * @param language The reader's language.
 * @returns The task, its failure sentence in that language.
 */
const failedIn = (language: 'ru' | 'hy'): PanelTask => {
  return {
    ...FAILED_CERTIFICATE,
    failure: { code: 'AcmeAccountRejected', message: SENTENCES[language] },
  }
}

const LABELS: Record<'en' | 'ru' | 'hy', { actions: string; liveView: string }> = {
  en: { actions: `Actions for ${FAILED_CERTIFICATE.subject}`, liveView: 'Live view' },
  ru: { actions: `Действия для ${FAILED_CERTIFICATE.subject}`, liveView: 'Наблюдать' },
  hy: { actions: `Գործողություններ ${FAILED_CERTIFICATE.subject}-ի համար`, liveView: 'Դիտել' },
}

/**
 * Opens the tasks screen on the failed task's live pane.
 * @param page The page under test.
 * @param language The interface language already persisted, which names the controls clicked.
 * @param task The failed task the list and its stream carry.
 * @returns Resolves once the pane was asked for.
 */
const openFailure = async (
  page: Page,
  language: 'en' | 'ru' | 'hy' = 'en',
  task: PanelTask = FAILED_CERTIFICATE,
): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [task])
  await stubTaskStream(page, taskFrame(task) + taskEndFrame('failed'))
  await page.goto('/tasks')
  await page.getByRole('button', { name: LABELS[language].actions }).click()
  await page.getByRole('menuitem', { name: LABELS[language].liveView }).click()
}

test('a failure reads as the panel sentence, with the code beneath it', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openFailure(page)

  await expect(
    page.getByText("The certificate authority refused this server's registration.", { exact: true }),
  ).toBeVisible()
  await expect(page.getByText('Code: AcmeAccountRejected', { exact: true })).toBeVisible()
})

test('the sentence is the one the panel sent in the operator language', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await openFailure(page, 'ru', failedIn('ru'))

  await expect(page.getByText(SENTENCES.ru, { exact: true })).toBeVisible()
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

test('the sentence is in Armenian, with the code beneath it', async ({ page }) => {
  await setPersistedLocale(page, 'hy')
  await openFailure(page, 'hy', failedIn('hy'))

  await expect(page.getByText(SENTENCES.hy, { exact: true })).toBeVisible()
  await expect(page.getByText('Կոդ՝ AcmeAccountRejected', { exact: true })).toBeVisible()
  await expect(page.getByTestId('task-failed-progress')).toHaveText('Կանգ է առել 20%-ում')
})

// Any code reads as whatever sentence the panel sent for it — including one this SPA has never heard
// of, built in a backend file far from the task (#272): there is no table here for it to be missing
// from.
test('a code the SPA has never heard of reads as the panel sentence verbatim', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openFailure(page, 'en', {
    ...FAILED_CERTIFICATE,
    failure: { code: 'BackupTruncated', message: 'The backup ended before it was complete.' },
  })

  await expect(page.getByText('The backup ended before it was complete.', { exact: true })).toBeVisible()
  await expect(page.getByText('Code: BackupTruncated', { exact: true })).toBeVisible()
})
