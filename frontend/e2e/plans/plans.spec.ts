import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import {
  MANAGED_PLANS,
  stubManagedPlans,
  type PlanRefusals,
  type PlanRequests,
} from '../fixtures/stub-plans-route'
import type { PanelModule } from '../../src/types/module'

/** Catalogue every spec here starts from: the accounts module licensed, which is what owns plans. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

/** Starter has three accounts on it, so it is the plan whose edits and deletion reach customers. */
const STARTER = MANAGED_PLANS[0]
/** Business has nobody on it, so deleting it is allowed. */
const BUSINESS = MANAGED_PLANS[1]

/**
 * Installs the shell and the plan endpoints, then opens a path.
 * @param page The Playwright page under test.
 * @param path Where to go.
 * @param refusals Writes the stubbed server refuses.
 * @returns What the stubbed endpoints received.
 */
const open = async (page: Page, path: string, refusals: PlanRefusals = {}): Promise<PlanRequests> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  const requests = await stubManagedPlans(page, undefined, refusals)
  await page.goto(path)
  return requests
}

/**
 * Fills the plan form's seven limits and name with a valid plan.
 * @param page The Playwright page under test.
 * @param name The plan's name.
 * @returns Resolves once every field is filled.
 */
const fillValidPlan = async (page: Page, name: string): Promise<void> => {
  await page.getByLabel('Plan name').fill(name)
  await page.getByLabel('Disk quota (MB)').fill('10240')
  await page.getByLabel('Sites', { exact: true }).fill('10')
  await page.getByLabel('Databases', { exact: true }).fill('5')
  await page.getByLabel('SFTP logins').fill('4')
  await page.getByLabel('FTPS logins').fill('0')
  await page.getByLabel('Cron entries').fill('7')
  await page.getByLabel('PHP workers per pool').fill('8')
}

test('the list shows every plan with its limits as numbers, and "None" where a plan sells none', async ({
  page,
}) => {
  await open(page, '/plans')

  await expect(page.getByRole('heading', { level: 1, name: 'Plans' })).toBeVisible()
  const rows = page.getByRole('row')
  // The header row plus the three plans.
  await expect(rows).toHaveCount(4)

  const starter = page.getByRole('row', { name: /Starter/ })
  await expect(starter.getByRole('cell').nth(1)).toHaveText('3')
  await expect(starter.getByRole('cell').nth(2)).toHaveText('5120')
  // Starter sells no FTPS logins and no cron entries: the word, not a bare zero.
  await expect(starter.getByRole('cell').nth(6)).toHaveText('None')
  await expect(starter.getByRole('cell').nth(7)).toHaveText('None')
  const business = page.getByRole('row', { name: /Business/ })
  await expect(business.getByRole('cell').nth(6)).toHaveText('10')

  // A figure is mono and right-aligned, so a column lines up on the digit.
  const align = await starter.getByRole('cell').nth(2).evaluate((cell) => {
    const style = getComputedStyle(cell)
    return { textAlign: style.textAlign, family: style.fontFamily }
  })
  expect(align.textAlign).toBe('right')
  expect(align.family).toContain('JetBrains Mono')
})

test('the sidebar offers Plans in the Hosting group and it leads to the screen', async ({ page }) => {
  await open(page, '/')

  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('link', { name: 'Plans' }).click()

  await expect(page).toHaveURL('/plans')
  await expect(page.getByRole('heading', { level: 1, name: 'Plans' })).toBeVisible()
})

test('an empty panel says there are no plans and offers to define the first', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubManagedPlans(page, [])
  await page.goto('/plans')

  await expect(page.getByText('No plans yet')).toBeVisible()
  await page.getByRole('link', { name: 'New plan' }).last().click()
  await expect(page).toHaveURL('/plans/new')
})

test('submitting the empty form shows the panel own field messages and sends nothing', async ({ page }) => {
  const requests = await open(page, '/plans/new')

  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page.getByText('Name is required.')).toBeVisible()
  // Seven limits, seven messages.
  await expect(page.getByText('Enter a whole number.')).toHaveCount(7)
  await expect(page.getByLabel('Plan name')).toHaveAttribute('aria-invalid', 'true')
  expect(requests.created).toEqual([])
})

test('the form mirrors the server bounds: zero disk, a letter and too long a name are caught', async ({
  page,
}) => {
  const requests = await open(page, '/plans/new')

  await fillValidPlan(page, 'x'.repeat(65))
  await page.getByLabel('Disk quota (MB)').fill('0')
  await page.getByLabel('Sites', { exact: true }).fill('ten')
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page.getByText('Name must be at most 64 characters.')).toBeVisible()
  await expect(page.getByText('Enter a number from 1 to 1073741824.')).toBeVisible()
  await expect(page.getByText('Enter a whole number.')).toBeVisible()
  expect(requests.created).toEqual([])

  // Zero is a real product for FTPS logins and cron entries, so it must NOT be flagged there.
  await expect(page.getByLabel('FTPS logins')).not.toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByLabel('Cron entries')).not.toHaveAttribute('aria-invalid', 'true')
})

test('a valid plan is posted as typed and shows up in the list', async ({ page }) => {
  const requests = await open(page, '/plans/new')

  await fillValidPlan(page, '  Agency  ')
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page).toHaveURL('/plans')
  expect(requests.created).toEqual([
    {
      name: 'Agency',
      diskQuotaMb: 10240,
      maxSites: 10,
      maxDatabases: 5,
      maxSftpUsers: 4,
      maxFtpUsers: 0,
      maxCronEntries: 7,
      maxPhpWorkersPerPool: 8,
    },
  ])
  await expect(page.getByRole('row', { name: /Agency/ })).toBeVisible()
})

test('a refused create renders the server sentence verbatim and keeps what was typed', async ({ page }) => {
  const detail = 'Another plan already has this name.'
  await open(page, '/plans/new', { create: detail })

  await fillValidPlan(page, 'Starter')
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page).toHaveURL('/plans/new')
  await expect(page.getByLabel('Plan name')).toHaveValue('Starter')
})

test('editing a plan starts from its figures and puts the whole plan', async ({ page }) => {
  const requests = await open(page, `/plans/${BUSINESS.id}`)

  await expect(page.getByRole('heading', { level: 1, name: 'Edit plan' })).toBeVisible()
  await expect(page.getByLabel('Plan name')).toHaveValue('Business')
  await expect(page.getByLabel('Disk quota (MB)')).toHaveValue('25600')
  // Nobody is on Business, so the notice says the edit changes nothing for anyone.
  await expect(page.getByText('No account is on this plan yet')).toBeVisible()

  await page.getByLabel('Disk quota (MB)').fill('51200')
  await page.getByRole('button', { name: 'Save plan' }).click()

  await expect(page).toHaveURL('/plans')
  expect(requests.updated).toHaveLength(1)
  expect(requests.updated[0]?.id).toBe(BUSINESS.id)
  expect(requests.updated[0]?.body).toEqual({
    name: 'Business',
    diskQuotaMb: 51200,
    maxSites: 25,
    maxDatabases: 10,
    maxSftpUsers: 10,
    maxFtpUsers: 10,
    maxCronEntries: 20,
    maxPhpWorkersPerPool: 10,
  })
  await expect(page.getByRole('row', { name: /Business/ }).getByRole('cell').nth(2)).toHaveText('51200')
})

test('editing a plan with accounts on it says the edit reaches them, and warns when the disk is lowered', async ({
  page,
}) => {
  await open(page, `/plans/${STARTER.id}`)

  await expect(page.getByText('3 accounts are on this plan. The new limits apply to all of them at once.')).toBeVisible()
  await expect(page.getByText('If any account already uses more than the new figure')).toBeVisible()
  // Not lowered yet, so no warning under the disk field.
  await expect(page.getByText('The disk quota goes down')).toHaveCount(0)

  await page.getByLabel('Disk quota (MB)').fill('1024')

  await expect(page.getByText('The disk quota goes down from 5120 MB to 1024 MB.')).toBeVisible()
})

test('a refused quota cut renders the server sentence verbatim and changes nothing on screen', async ({
  page,
}) => {
  const detail =
    'The plan was not changed: an account on it already uses more disk than the new quota allows.'
  const requests = await open(page, `/plans/${STARTER.id}`, { update: detail })

  await page.getByLabel('Disk quota (MB)').fill('1024')
  await page.getByRole('button', { name: 'Save plan' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page).toHaveURL(`/plans/${STARTER.id}`)
  expect(requests.updated).toHaveLength(1)
})

test('an unknown plan id says the plan was not found', async ({ page }) => {
  await open(page, '/plans/99999999-9999-9999-9999-999999999999')

  await expect(page.getByText('Plan not found')).toBeVisible()
})

test('deleting asks first, names the plan, and removes it only when confirmed', async ({ page }) => {
  const requests = await open(page, '/plans')

  await page.getByRole('button', { name: 'Delete Business' }).click()

  const dialog = page.getByRole('dialog', { name: 'Delete plan Business' })
  await expect(dialog).toContainText('No account is on it, so no customer is affected.')
  expect(requests.deleted).toEqual([])

  // Declining sends nothing and leaves the row.
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(requests.deleted).toEqual([])
  await expect(page.getByRole('row', { name: /Business/ })).toBeVisible()

  await page.getByRole('button', { name: 'Delete Business' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()

  await expect(page.getByRole('row', { name: /Business/ })).toHaveCount(0)
  expect(requests.deleted).toEqual([BUSINESS.id])
})

test('a plan accounts are on says so in the dialog, and the server refusal is shown verbatim', async ({
  page,
}) => {
  const detail =
    'This plan was not deleted: accounts are still on it. Move those accounts to another plan or delete them first.'
  const requests = await open(page, '/plans', { remove: detail })

  await page.getByRole('button', { name: 'Delete Starter' }).click()
  const dialog = page.getByRole('dialog', { name: 'Delete plan Starter' })
  await expect(dialog).toContainText('3 accounts are on this plan, so the panel will refuse to delete it')
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  expect(requests.deleted).toEqual([STARTER.id])
  // The plan is still there: the server changed nothing and so does the list.
  await expect(page.getByRole('row', { name: /Starter/ })).toBeVisible()
})

test('a customer who opens the screen reads the server refusal in place of the list', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  const refusal = 'You do not have permission to manage plans.'
  await page.route('**/api/v1/plans', async (route) => {
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'Forbidden', title: 'Forbidden', detail: refusal }),
    })
  })
  await page.goto('/plans')

  await expect(page.getByText(refusal, { exact: true })).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('the Plans screen has no horizontal scroll at 390 px in English and Armenian', async ({ browser }) => {
  for (const locale of ['en', 'hy'] as const) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } })
    const page = await context.newPage()
    await setPersistedLocale(page, locale)
    await open(page, '/plans')
    await expect(page.getByRole('row', { name: /Starter/ })).toBeVisible()
    await page.evaluate(async () => {
      await document.fonts.ready
    })

    const widths = await page.evaluate(() => {
      const main = document.querySelector('main')
      return {
        document: document.documentElement.scrollWidth,
        viewport: window.innerWidth,
        main: main?.scrollWidth ?? 0,
        mainBox: main?.clientWidth ?? 0,
      }
    })
    expect(widths.document, `${locale}: the document scrolls sideways`).toBeLessThanOrEqual(widths.viewport)
    expect(widths.main, `${locale}: the main panel overflows`).toBeLessThanOrEqual(widths.mainBox)

    await page.goto('/plans/new')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    const form = await page.evaluate(() => {
      const main = document.querySelector('main')
      return {
        document: document.documentElement.scrollWidth,
        viewport: window.innerWidth,
        main: main?.scrollWidth ?? 0,
        mainBox: main?.clientWidth ?? 0,
      }
    })
    expect(form.document, `${locale}: the form scrolls sideways`).toBeLessThanOrEqual(form.viewport)
    expect(form.main, `${locale}: the form overflows its panel`).toBeLessThanOrEqual(form.mainBox)
    await context.close()
  }
})
