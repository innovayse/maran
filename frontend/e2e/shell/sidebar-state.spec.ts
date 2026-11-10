import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubBackupOptions } from '../fixtures/stub-backup-options-route'
import { stubBackupSchedules } from '../fixtures/stub-backup-schedule-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { setPersistedLocale } from '../fixtures/set-locale'
import { mainNavigation, openFullShell } from '../fixtures/open-full-shell'
import type { PanelModule } from '../../src/types/module'
import type { Locator, Page } from '@playwright/test'

// Issue #221: the sidebar after a live language switch, the role line, the current row's
// visibility and highlight, and the focus ring after a click (the page that scrolled twice is in
// content-scroll.spec.ts, which needs its own browser). Every destination is reached by clicks; an address is typed only to start on a deep link.

/** The router's active class as a whole class name, the one the nav row's fill is keyed on. */
const LIT = /(^|\s)router-link-active(\s|$)/

/**
 * A catalogue module this bundle has no item for. Menu labels are the SPA's own i18n keys (#311);
 * the panel's localized module name is drawn only for such a module, in the fallback "Panel" group,
 * so it is the one sidebar text a language switch must re-read from the server.
 */
const UNKNOWN_MODULE = 'webmail'

/** That module's name per language, as the panel would localize it. */
const UNKNOWN_NAME: Record<string, string> = { en: 'Webmail', ru: 'Веб-почта', hy: 'Վեբփոստ' }

/**
 * Answers the module catalogue in the language the request asks for, like the panel does.
 * @param page The Playwright page whose network the route is installed on.
 * @param slowLanguage A language whose answer is held back until the returned release is called.
 * @returns A release for the held-back answer.
 */
const stubLocalizedModules = async (page: Page, slowLanguage?: string): Promise<() => void> => {
  let release = (): void => {}
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route('**/api/v1/modules', async (route) => {
    const language = (route.request().headers()['accept-language'] ?? 'en').slice(0, 2)
    if (language === slowLanguage) {
      await held
    }
    const modules: PanelModule[] = [
      { name: 'firewall', displayName: 'Firewall', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
      {
        name: UNKNOWN_MODULE,
        displayName: UNKNOWN_NAME[language] ?? 'Webmail',
        kindDisplayName: 'Core',
        tier: 'included',
        state: 'enabled',
      },
    ]
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(modules) })
  })
  return release
}

/**
 * Switches the interface language from the app bar's language menu, as a visitor does.
 * @param page The Playwright page under test.
 * @param name The language's own name, as the menu lists it.
 * @returns Resolves once the option has been chosen.
 */
const chooseLanguage = async (page: Page, name: string): Promise<void> => {
  await page.getByRole('banner').getByTestId('locale-switcher').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name, exact: true }).click()
}

test('switching the language live re-reads the sidebar and the breadcrumb, server names included', async ({
  page,
}) => {
  // Wide enough for the whole Armenian trail beside the app bar's controls: the breadcrumb hides itself
  // when it does not fit (see ShellBreadcrumb), and at 1440 the longer Armenian labels leave it 35px short.
  await page.setViewportSize({ width: 1680, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubLocalizedModules(page)
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await page.goto('/')

  // The sidebar's own navigation: the app bar's breadcrumb is a navigation landmark too and comes
  // first, and the sidebar's accessible name is itself translated by the switch under test.
  const nav = page.getByRole('complementary').getByRole('navigation')
  const trail = page.getByRole('banner').getByRole('navigation')
  // The breadcrumb names a page by the SPA's own labels; the unknown module's row, which is
  // locked (this bundle has no screen for it), carries the panel's localized name.
  await nav.getByRole('link', { name: 'Security', exact: true }).click()
  await expect(page).toHaveURL('/firewall')
  await expect(trail).toContainText('Security')
  await expect(nav.getByRole('link', { name: /^Webmail/ })).toBeVisible()

  await chooseLanguage(page, 'Հայերեն')
  await expect(nav.getByRole('link', { name: /^Վեբփոստ/ })).toBeVisible()
  await expect(nav.getByRole('link', { name: /^Webmail/ })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'Անվտանգություն', exact: true })).toBeVisible()
  await expect(trail).toContainText('Անվտանգություն')
  await expect(trail).not.toContainText('Security')

  await chooseLanguage(page, 'English')
  await expect(nav.getByRole('link', { name: /^Webmail/ })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Security', exact: true })).toBeVisible()
  await expect(trail).toContainText('Security')
})

test('after two quick switches the names are the last language chosen, whatever answers last', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  const releaseRussian = await stubLocalizedModules(page, 'ru')
  await page.goto('/')
  const nav = page.getByRole('complementary').getByRole('navigation')
  await expect(nav.getByRole('link', { name: /^Webmail/ })).toBeVisible()

  const russianAsked = page.waitForRequest((request): boolean => {
    return request.url().endsWith('/api/v1/modules') && request.headers()['accept-language']?.startsWith('ru') === true
  })
  await chooseLanguage(page, 'Русский')
  await russianAsked
  await chooseLanguage(page, 'Հայերեն')
  await expect(nav.getByRole('link', { name: /^Վեբփոստ/ })).toBeVisible()

  // The Russian answer lands last; it was asked for a language no longer chosen.
  const russianAnswered = page.waitForResponse((response): boolean => {
    return response.url().endsWith('/api/v1/modules') && response.request().headers()['accept-language']?.startsWith('ru') === true
  })
  releaseRussian()
  await russianAnswered
  await expect(nav.getByRole('link', { name: /^Վեբփոստ/ })).toBeVisible()
  await expect(nav.getByRole('link', { name: /^Веб-почта/ })).toHaveCount(0)
})

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the role under the user name is not cut off in the 240px sidebar (${locale})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 })
    await setPersistedLocale(page, locale)
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubModules(page, [])
    await stubShellReads(page)
    await stubLandingReads(page)
    await page.goto('/')

    // The sidebar is the 240px panel the role has to fit in.
    const panel = await page.getByRole('complementary').boundingBox()
    expect(panel?.width).toBe(240)
    const card = page.getByTestId('shell-identity')
    await expect(card).toBeVisible()
    const cut = await card.locator('span').evaluateAll((spans) => {
      const leaves = spans.filter((span) => {
        return span.getAttribute('data-testid') !== 'shell-identity-name' && span.children.length === 0
      })
      return {
        leaves: leaves.length,
        cut: leaves.some((span) => {
          return span.scrollWidth > span.clientWidth || getComputedStyle(span).textOverflow === 'ellipsis'
        }),
      }
    })
    // Vacuity guard: the role line was found, so "nothing is cut" was measured on it.
    expect(cut.leaves).toBeGreaterThan(0)
    expect(cut.cut).toBe(false)
  })
}

test('the Accounts row is lit on each of its pages, as wide as its children', async ({ page }) => {
  await openFullShell(page, '/')
  const nav = mainNavigation(page)
  const accounts = nav.getByRole('link', { name: 'Accounts', exact: true })

  for (const child of ['Account list', 'Plans']) {
    await accounts.click()
    await nav.getByRole('link', { name: child, exact: true }).click()
    await expect(nav.getByRole('link', { name: child, exact: true })).toHaveClass(LIT)
    await expect(accounts).toHaveClass(LIT)
  }

  // One shape: the section's pill ends where its children's do, its toggle drawn inside it.
  const parent = await accounts.boundingBox()
  const plans = await nav.getByRole('link', { name: 'Plans', exact: true }).boundingBox()
  const toggle = await nav.getByRole('button', { name: 'Show the Accounts pages' }).boundingBox()
  expect((parent?.x ?? 0) + (parent?.width ?? 0)).toBe((plans?.x ?? 0) + (plans?.width ?? 0))
  expect((toggle?.x ?? 0) + (toggle?.width ?? 0)).toBeLessThanOrEqual((parent?.x ?? 0) + (parent?.width ?? 0))
  await expect(nav.getByRole('link', { name: 'Account list', exact: true })).not.toHaveClass(LIT)
})

test('the collapsed rail lights the Accounts section on the Plans page too', async ({ page }) => {
  await openFullShell(page, '/')
  const nav = mainNavigation(page)
  await nav.getByRole('link', { name: 'Accounts', exact: true }).click()
  await nav.getByRole('link', { name: 'Plans', exact: true }).click()
  await expect(page).toHaveURL('/plans')

  await page.getByRole('button', { name: 'Collapse the sidebar' }).click()
  const rail = mainNavigation(page)
  await expect(rail.getByRole('link', { name: 'Accounts', exact: true })).toHaveClass(LIT)
  // Inverse control: a section that holds nothing on this page stays dark.
  await expect(rail.getByRole('link', { name: 'Sites and domains', exact: true })).not.toHaveClass(LIT)
})

/**
 * Asserts a sidebar row ends above the identity card, retrying until it does. The window alone
 * cannot say so (a row under the card is still in the viewport), and the sidebar settles over a few
 * frames after first paint (the card fills in, rows above the current one reflow), so a one-shot
 * measurement races that.
 * @param page The Playwright page under test.
 * @param row The sidebar link that must end up above the card.
 * @returns Resolves once the row's bottom edge is at or above the card's top edge.
 */
const expectAboveIdentityCard = async (page: Page, row: Locator): Promise<void> => {
  const card = page.getByTestId('shell-identity')
  await expect
    .poll(
      async (): Promise<number> => {
        const [rowBox, cardBox] = [await row.boundingBox(), await card.boundingBox()]
        // A missing box reads as "not yet": NaN never satisfies the comparison below.
        return rowBox === null || cardBox === null ? Number.NaN : cardBox.y - (rowBox.y + rowBox.height)
      },
      { message: 'the current row ends above the identity card' },
    )
    .toBeGreaterThanOrEqual(0)
}

test('the current row is scrolled into view on a short window', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 480 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubBackupOptions(page)
  // The page the spec opens is the backup schedule screen, which reads the host policy.
  await stubBackupSchedules(page, {})
  await stubModules(page, [
    { name: 'monitoring', displayName: 'Monitoring', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'tasks', displayName: 'Tasks', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'sftp', displayName: 'File transfer', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'cron', displayName: 'Scheduled tasks', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'firewall', displayName: 'Firewall', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  ])
  await page.goto('/backups/schedule')

  const current = page
    .getByRole('navigation', { name: 'Main navigation' })
    // Exact: the cron row is "Scheduled tasks", and this is the Backups child "Schedule".
    .getByRole('link', { name: 'Schedule', exact: true })
  await expect(current).toHaveAttribute('aria-current', 'page')
  await expect(current).toBeInViewport({ ratio: 1 })
  await expectAboveIdentityCard(page, current)
})

test('choosing Backups on a short window brings its Backup list row into view', async ({ page }) => {
  await openFullShell(page, '/')
  await page.setViewportSize({ width: 1440, height: 480 })
  const nav = mainNavigation(page)

  await nav.getByRole('link', { name: 'Backups', exact: true }).click()
  await expect(page).toHaveURL('/backups')
  const current = nav.getByRole('link', { name: 'Backup list', exact: true })
  await expect(current).toHaveAttribute('aria-current', 'page')
  await expect(current).toBeInViewport({ ratio: 1 })
  await expectAboveIdentityCard(page, current)

  // Nearest, no jumping: a row already in view does not move the list.
  const scroller = nav.locator('xpath=ancestor::div[contains(@class,"overflow-y-auto")][1]')
  const before = await scroller.evaluate((element): number => {
    return element.scrollTop
  })
  await nav.getByRole('link', { name: 'Schedule', exact: true }).click()
  await expect(page).toHaveURL('/backups/schedule')
  const after = await scroller.evaluate((element): number => {
    return element.scrollTop
  })
  expect(after).toBe(before)
})

test('a mouse click on a sidebar row leaves no focus ring', async ({ page }) => {
  await openFullShell(page, '/')
  const nav = mainNavigation(page)

  for (const name of ['Files and access', 'Security']) {
    const link = nav.getByRole('link', { name, exact: true })
    await link.click()
    await expect(link).toHaveClass(LIT)
    const ringed = await link.evaluate((element: HTMLElement): boolean => {
      return element === document.activeElement && element.matches(':focus-visible')
    })
    expect(ringed).toBe(false)
  }

  // Positive control: the same row reached by the keyboard does show the ring.
  const firewall = nav.getByRole('link', { name: 'Security', exact: true })
  await firewall.focus()
  await page.keyboard.press('ArrowUp')
  await page.keyboard.press('ArrowDown')
  const keyboardRing = await firewall.evaluate((element: HTMLElement): boolean => {
    return element.matches(':focus-visible')
  })
  expect(keyboardRing).toBe(true)
})
