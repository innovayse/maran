import { expect, test, type Page } from '@playwright/test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { stubPinnedPages, type PinnedPagesState } from '../fixtures/stub-pinned-pages-route'
import { stubbedAdministrator, stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

// Favourites (issue #318): a person pins pages from the sidebar or the phone drawer, reaches them
// from the app bar's Favourites menu, and finds the same pins on another device. Pages that cannot
// be opened (planned) have no star, and a pin whose page is no longer offered is not drawn.
// Everything is reached by clicks after the first entry; no spec navigates to a deep URL.

/** A customer on one hosting account, who is offered only the pages of their own account. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The one module the customer's own account reaches in these specs. */
const CUSTOMER_MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', state: 'enabled' },
]

/**
 * Signs a person in with a catalogue and the panel's pinned-pages state, and opens the shell.
 * @param page The Playwright page under test.
 * @param state What the stubbed panel holds for the person; shared between pages to model devices.
 * @param user Who signs in.
 * @param modules The catalogue the panel reports.
 * @param viewport The window size; the desktop sidebar by default.
 * @returns Resolves once the main navigation is drawn.
 */
const openShell = async (
  page: Page,
  state: PinnedPagesState,
  user: AuthenticatedUser = stubbedAdministrator,
  modules: PanelModule[] = FULL_CATALOGUE,
  viewport: { width: number; height: number } = { width: 1440, height: 900 },
): Promise<void> => {
  await page.setViewportSize(viewport)
  // Anything no narrower stub answers is an empty 404: these specs are about the shell around a page.
  await page.route(/\/api\/v1\//, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Not found', status: 404 }),
    })
  })
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, modules)
  await stubPinnedPages(page, state)
  await page.goto('/')
  await expect(page.getByRole('main')).toBeVisible()
}

/**
 * Opens the app bar's Favourites menu.
 * @param page The Playwright page under test.
 * @returns Resolves once the menu is open.
 */
const openFavourites = async (page: Page): Promise<void> => {
  await page.getByRole('button', { name: 'Favourites', exact: true }).click()
  await expect(page.getByRole('menu')).toBeVisible()
}

test('an administrator pins a page from the sidebar, opens it from Favourites and unpins it', async ({ page }) => {
  const state: PinnedPagesState = { keys: [], limit: 8 }
  await openShell(page, state)
  await openSection(page, 'Sites and domains')
  const nav = mainNavigation(page)

  await nav.getByRole('button', { name: 'Pin Site list to favourites' }).click()

  await expect(nav.getByRole('button', { name: 'Remove Site list from favourites' })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  expect(state.keys).toEqual(['sites-list'])

  await openFavourites(page)
  await page.getByRole('menuitem', { name: 'Site list' }).click()
  await expect(page).toHaveURL(/\/sites$/)

  await nav.getByRole('button', { name: 'Remove Site list from favourites' }).click()

  await expect(nav.getByRole('button', { name: 'Pin Site list to favourites' })).toHaveAttribute(
    'aria-pressed',
    'false',
  )
  expect(state.keys).toEqual([])
  await openFavourites(page)
  await expect(page.getByRole('menu').getByRole('menuitem')).toHaveText(/Nothing is pinned yet/)
})

test('pins follow the person to another browser', async ({ page, browser }, testInfo) => {
  const state: PinnedPagesState = { keys: [], limit: 8 }
  await openShell(page, state)
  await openSection(page, 'Sites and domains')
  await mainNavigation(page).getByRole('button', { name: 'Pin Site list to favourites' }).click()
  await expect(mainNavigation(page).getByRole('button', { name: 'Remove Site list from favourites' })).toBeVisible()

  // A new context does not inherit the project's base URL, so it is passed on.
  const otherContext = await browser.newContext({ baseURL: testInfo.project.use.baseURL })
  const otherPage = await otherContext.newPage()
  await openShell(otherPage, state)
  await openFavourites(otherPage)

  await expect(otherPage.getByRole('menu').getByRole('menuitem', { name: 'Site list' })).toBeVisible()
  await otherContext.close()
})

test('a customer pins a page of their own account', async ({ page }) => {
  const state: PinnedPagesState = { keys: [], limit: 8 }
  await openShell(page, state, CUSTOMER, CUSTOMER_MODULES)
  await openSection(page, 'Sites and domains')

  await mainNavigation(page).getByRole('button', { name: 'Pin Site list to favourites' }).click()

  expect(state.keys).toEqual(['sites-list'])
  await openFavourites(page)
  await expect(page.getByRole('menu').getByRole('menuitem', { name: 'Site list' })).toBeVisible()
})

test('a page that is not built yet has no star', async ({ page }) => {
  await openShell(page, { keys: [], limit: 8 })
  await openSection(page, 'DNS')
  const nav = mainNavigation(page)

  await expect(nav.getByText('Zones', { exact: true })).toBeVisible()
  await expect(nav.getByRole('button', { name: /^Pin Zones/ })).toHaveCount(0)
  // A built page in another section does have one, so the absence above is a finding and not a blind search.
  await openSection(page, 'Sites and domains')
  await expect(nav.getByRole('button', { name: 'Pin Site list to favourites' })).toBeVisible()
})

test('a pin for a page the person is no longer offered is stored but not drawn', async ({ page }) => {
  // `resellers` is planned, `gone-page` names nothing: the panel keeps both, the menu draws neither.
  const state: PinnedPagesState = { keys: ['gone-page', 'resellers', 'sites-list'], limit: 8 }
  await openShell(page, state)

  await openFavourites(page)

  const items = page.getByRole('menu').getByRole('menuitem')
  await expect(items).toHaveCount(1)
  await expect(items.first()).toHaveText('Site list')
})

test('a full list disables the star of an unpinned page but keeps a pinned one usable', async ({ page }) => {
  const state: PinnedPagesState = { keys: ['sites-list'], limit: 1 }
  await openShell(page, state)
  await openSection(page, 'Sites and domains')
  await openSection(page, 'Backups')
  const nav = mainNavigation(page)

  await expect(nav.getByRole('button', { name: 'Favourites are full. Remove one to pin Backup list.' })).toBeDisabled()
  await expect(nav.getByRole('button', { name: 'Remove Site list from favourites' })).toBeEnabled()
})

test('the phone drawer pins a page too', async ({ page }) => {
  const state: PinnedPagesState = { keys: [], limit: 8 }
  await openShell(page, state, stubbedAdministrator, FULL_CATALOGUE, { width: 390, height: 844 })

  await page.locator('button[aria-label="Open the navigation"]').click()
  const drawer = page.getByRole('dialog', { name: 'Navigation' })
  await drawer.getByRole('button', { name: 'Show the Sites and domains pages' }).click()
  await drawer.getByRole('button', { name: 'Pin Site list to favourites' }).click()

  await expect(drawer.getByRole('button', { name: 'Remove Site list from favourites' })).toBeVisible()
  expect(state.keys).toEqual(['sites-list'])
})
