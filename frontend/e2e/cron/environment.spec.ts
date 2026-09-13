import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import type { Account } from '../../src/types/account'
import type { CronEnvironmentVariable } from '../../src/types/cronEnvironmentVariable'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const PATH_VARIABLE: CronEnvironmentVariable = { name: 'PATH', value: '/usr/local/bin:/usr/bin' }

const openCronPage = async (page: Page, variables: CronEnvironmentVariable[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEntries(page, [])
  await stubCronEnvironment(page, variables)
  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
}

test('the environment editor shows the assignments the agent manages', async ({ page }) => {
  await openCronPage(page, [PATH_VARIABLE])

  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue('PATH')
  await expect(page.getByRole('textbox', { name: 'Value' })).toHaveValue(PATH_VARIABLE.value)
})

// R13, the half that is this panel's: the hint appears. What would break it — a hint keyed on the
// wrong name, or one that never renders — and the PATH assertion below is what stops it from
// passing by warning about everything.
test('a reserved name is hinted before the operator spends a round trip on it', async ({ page }) => {
  await openCronPage(page, [])

  await page.getByRole('button', { name: 'Add a variable' }).click()
  await page.getByRole('textbox', { name: 'Name' }).fill('MAILTO')

  await expect(page.getByText('MAILTO is managed by the server', { exact: false })).toBeVisible()
})

test('an ordinary name is not hinted, so the warning means something when it appears', async ({
  page,
}) => {
  await openCronPage(page, [])

  await page.getByRole('button', { name: 'Add a variable' }).click()
  await page.getByRole('textbox', { name: 'Name' }).fill('PATH')

  await expect(page.getByText('is managed by the server', { exact: false })).toHaveCount(0)
})

// R13, the half that is NOT this panel's, and the more important one: the hint is advice, never the
// decision. `CronEnvironmentVariableValidator` refuses `MAILTO` and the agent refuses it again; a
// client that also refused would be a second copy of an authorization rule, free to be wrong on its
// own. So the request goes, carrying the reserved name, and the server's answer is what the
// operator sees. What would break this: adding a guard in `CronEnvironmentEditor.save` — the PUT
// would never be made and `waitForRequest` would time out.
test('the reserved-name hint does not block the request — the server is what refuses it', async ({
  page,
}) => {
  await openCronPage(page, [])

  await page.getByRole('button', { name: 'Add a variable' }).click()
  await page.getByRole('textbox', { name: 'Name' }).fill('MAILTO')
  await page.getByRole('textbox', { name: 'Value' }).fill('ops@example.com')

  const written = page.waitForRequest((request) => {
    return request.method() === 'PUT' && request.url().includes('/api/v1/cron-environment')
  })
  await page.getByRole('button', { name: 'Save environment' }).click()

  const body = (await written).postDataJSON() as {
    accountId: string
    variables: CronEnvironmentVariable[]
  }
  expect(body.accountId).toBe(ALICE.id)
  expect(body.variables).toEqual([{ name: 'MAILTO', value: 'ops@example.com' }])
})

// The endpoint is a PUT and the verb is the warning: a name absent from the body is removed from
// the crontab. The screen has to say so, or an operator removing one row would not know they had
// rewritten the whole preamble.
test('the editor states that saving replaces the whole set', async ({ page }) => {
  await openCronPage(page, [PATH_VARIABLE])

  await expect(page.getByText('Saving replaces the whole set', { exact: false })).toBeVisible()
})
