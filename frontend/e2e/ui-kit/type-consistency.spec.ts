import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules, stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubLogin, stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import { stubManagedPlans } from '../fixtures/stub-plans-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'

// Issue #218, points 1-4: one face, one size and one colour for things that are the same kind of
// text. Each check compares the element against a sibling of the same kind on the same screen, so
// it fails on a drift between them rather than on a design value somebody may later change.

const SITES_MODULE: PanelModule[] = [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }]
const ACCOUNTS_MODULE: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

const SITE: SiteDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'alpha.example.com',
  aliases: [],
  backendType: 'php',
  phpVersion: '8.3',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/alpha.example.com/public',
  hasCertificate: false,
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const ACCOUNT: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Reads one computed style property of an element (or of its `::placeholder`).
 * @param locator The element.
 * @param property The CSS property, in camelCase.
 * @param pseudo `::placeholder` to read the placeholder's style instead of the element's.
 * @returns The computed value.
 */
const styleOf = async (locator: Locator, property: 'fontFamily' | 'fontSize' | 'color', pseudo?: string): Promise<string> => {
  return locator.evaluate(
    (element, args) => {
      return getComputedStyle(element, args.pseudo ?? null)[args.property]
    },
    { property, pseudo },
  )
}

/**
 * Signs in and installs the routes the site screens read.
 * @param page The Playwright page.
 * @returns Resolves once every route is installed.
 */
const stubSiteScreens = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, SITES_MODULE)
  await stubAccounts(page, [ACCOUNT])
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])
}

for (const width of [1440, 390]) {
  test(`at ${width}px every segment of the site tabs is set in one face, chosen or not`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await stubSiteScreens(page)
    await page.goto(`/sites/${SITE.id}`)

    const tabs = page.getByRole('group', { name: 'Site' })
    const overview = tabs.getByRole('button', { name: 'Overview' })
    const logs = tabs.getByRole('button', { name: 'Logs' })
    await expect(overview).toHaveAttribute('aria-pressed', 'true')
    const chosenFace = await styleOf(overview, 'fontFamily')

    expect(await styleOf(logs, 'fontFamily')).toBe(chosenFace)
    expect(chosenFace).not.toContain('JetBrains Mono')

    await logs.click()
    await expect(logs).toHaveAttribute('aria-pressed', 'true')
    expect(await styleOf(overview, 'fontFamily')).toBe(chosenFace)
    expect(await styleOf(logs, 'fontFamily')).toBe(chosenFace)
  })
}

test('the PHP version label reads as written, and its helper is the size of every other helper', async ({ page }) => {
  await stubSiteScreens(page)
  await page.goto('/sites/new')
  await page.getByRole('combobox', { name: 'Backend' }).click()
  await page.getByRole('option', { name: 'PHP', exact: true }).click()

  // Sentence case, no caps tracking: "PHPVERSION" was the label in capitals with its space eaten.
  const label = page.getByText('PHP version', { exact: true })
  await expect(label).toHaveCSS('text-transform', 'none')
  // No caps tracking: the body face's own slight negative tracking is fine, a widened one is not.
  const spacing = await label.evaluate((element) => {return getComputedStyle(element).letterSpacing})
  expect(spacing === 'normal' || Number.parseFloat(spacing) <= 0).toBe(true)
  await expect(label).toHaveText('PHP version')

  const phpHint = page.getByText('Only versions installed on this server can be selected.')
  const aliasesHint = page.getByText(/^One hostname per line/)
  await expect(aliasesHint).toBeVisible()
  expect(await styleOf(phpHint, 'fontSize')).toBe(await styleOf(aliasesHint, 'fontSize'))
})

for (const locale of ['en', 'hy'] as const) {
  test(`in ${locale} the second-factor placeholder is set in the body face, the same as the sign-in fields`, async ({
    page,
  }) => {
    await setPersistedLocale(page, locale)
    await stubSignedOut(page)
    await stubHealthy(page)
    await stubEmptyModules(page)
    await stubLogin(page, { session: null })

    await page.goto('/login')
    const username = page.locator('input').first()
    const bodyFace = await styleOf(username, 'fontFamily', '::placeholder')
    await username.fill('admin')
    await page.locator('input[type="password"]').fill('correct horse battery staple')
    await page.locator('button[type="submit"]').click()
    await expect(page).toHaveURL('/login/two-factor')

    const code = page.locator('input[autocomplete="one-time-code"]')
    const placeholderFace = await styleOf(code, 'fontFamily', '::placeholder')
    expect(placeholderFace).not.toContain('JetBrains Mono')
    // The Armenian placeholder must reach a face that has its glyphs, and the same one the
    // sign-in screen falls back to, so the two steps of one flow read alike.
    expect(placeholderFace).toContain('Noto Sans Armenian')
    expect(placeholderFace).toBe(bodyFace)
  })
}

test('the invitation sentence on an account is body text, the size of the notes beside it', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, ACCOUNTS_MODULE)
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
  await stubInvitation(page, ACCOUNT.id, 'pending')
  await page.goto(`/accounts/${ACCOUNT.id}`)

  const state = page.getByTestId('invitation-state')
  await expect(state).toContainText('Waiting for the owner')
  const note = page.getByText(/^An e-mail is sent only if/)
  expect(await styleOf(state, 'fontSize')).toBe(await styleOf(note, 'fontSize'))
})

test('the plans list sets its accounts figures in the same colour as every other figure', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, ACCOUNTS_MODULE)
  await stubManagedPlans(page)
  await page.goto('/plans')

  const table = page.getByRole('table', { name: 'Plans and their limits' })
  const firstRow = table.getByRole('row').nth(1)
  // Every cell of the row, the row header included, so the indices line up with the column headers.
  const cells = firstRow.locator('th, td')
  await expect(cells.first()).toBeVisible()
  const headers = await table.getByRole('columnheader').allInnerTexts()
  const accountsCell = cells.nth(headers.findIndex((text) => {return text.startsWith('Accounts')}))
  const sitesCell = cells.nth(headers.findIndex((text) => {return text.startsWith('Sites')}))

  expect(await styleOf(accountsCell, 'color')).toBe(await styleOf(sitesCell, 'color'))
})
