import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { FirewallRule } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

// Issue #221: the sidebar after a live language switch, the role line, the current row's
// visibility and highlight, the focus ring after a click, and a page that scrolled twice.

/** The router's active class as a whole class name, the one the nav row's fill is keyed on. */
const LIT = /(^|\s)router-link-active(\s|$)/

/** Module names per language, as the panel would localize them. */
const FIREWALL_NAME: Record<string, string> = { en: 'Firewall', ru: 'Брандмауэр', hy: 'Firewall-ը' }

/**
 * Answers the module catalogue in the language the request asks for, like the panel does.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
const stubLocalizedModules = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/modules', async (route) => {
    const language = (route.request().headers()['accept-language'] ?? 'en').slice(0, 2)
    const modules: PanelModule[] = [
      {
        name: 'firewall',
        displayName: FIREWALL_NAME[language] ?? 'Firewall',
        tier: 'included',
        isEnabled: true,
      },
    ]
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(modules) })
  })
}

test('switching the language live re-reads the module names', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubLocalizedModules(page)
  await page.goto('/')

  // The sidebar's own navigation: the app bar's breadcrumb is a navigation landmark too and comes
  // first, and the sidebar's accessible name is itself translated by the switch under test.
  const nav = page.getByRole('complementary').getByRole('navigation')
  await expect(nav.getByRole('link', { name: 'Firewall', exact: true })).toBeVisible()

  await page.getByRole('banner').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: 'Русский', exact: true }).click()

  await expect(nav.getByRole('link', { name: 'Брандмауэр' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Firewall', exact: true })).toHaveCount(0)
})

test('the role under the user name is not cut off in Armenian', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await setPersistedLocale(page, 'hy')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [])
  await page.goto('/')

  const card = page.getByTestId('shell-identity')
  await expect(card).toBeVisible()
  const cut = await card.locator('span').evaluateAll((spans) => {
    return spans
      .filter((span) => {
        return span.getAttribute('data-testid') !== 'shell-identity-name' && span.children.length === 0
      })
      .some((span) => {
        return span.scrollWidth > span.clientWidth || getComputedStyle(span).textOverflow === 'ellipsis'
      })
  })
  expect(cut).toBe(false)
})

test('the Accounts row is lit on the Plans page', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await page.goto('/plans')

  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(nav.getByRole('link', { name: 'Plans' })).toHaveClass(LIT)
  await expect(nav.getByRole('link', { name: 'Accounts', exact: true })).toHaveClass(LIT)
  await expect(nav.getByRole('link', { name: 'Account list' })).not.toHaveClass(LIT)
})

test('the current row is scrolled into view on a short window', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 480 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true },
    { name: 'tasks', displayName: 'Tasks', tier: 'included', isEnabled: true },
    { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
    { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
    { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
    { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
    { name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true },
    { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
    { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
  ])
  await page.goto('/backups/schedule')

  const current = page
    .getByRole('navigation', { name: 'Main navigation' })
    // Exact: the cron row is "Scheduled tasks", and this is the Backups child "Schedule".
    .getByRole('link', { name: 'Schedule', exact: true })
  await expect(current).toHaveAttribute('aria-current', 'page')
  await expect(current).toBeInViewport({ ratio: 1 })
  // In view means above the identity card, not under it.
  const row = await current.boundingBox()
  const card = await page.getByTestId('shell-identity').boundingBox()
  expect((row?.y ?? 0) + (row?.height ?? 0)).toBeLessThanOrEqual(card?.y ?? 0)
})

test('a mouse click on a sidebar row leaves no focus ring', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }])
  await page.goto('/')

  const link = page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Firewall' })
  await link.click()
  await expect(page).toHaveURL('/firewall')
  const ringed = await link.evaluate((element: HTMLElement): boolean => {
    return element.matches(':focus-visible')
  })
  expect(ringed).toBe(false)
})

test('the Firewall page scrolls in its panel only, and the app bar does not move', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 600 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }])
  const rules = Array.from({ length: 40 }, (_, index): FirewallRule => {
    return { port: 2000 + index, portTo: null, protocol: 'tcp', sourceCidr: '0.0.0.0/0' }
  })
  await stubFirewall(page, { rules, bans: [], whitelist: [] })

  await page.goto('/')
  const before = await page.getByRole('banner').boundingBox()

  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Firewall' }).click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

  const documentScrolls = await page.evaluate((): boolean => {
    const root = document.scrollingElement ?? document.documentElement
    return root.scrollHeight > root.clientHeight || root.scrollWidth > root.clientWidth
  })
  expect(documentScrolls).toBe(false)

  const after = await page.getByRole('banner').boundingBox()
  expect(after?.x).toBe(before?.x)
  expect(after?.width).toBe(before?.width)
})
