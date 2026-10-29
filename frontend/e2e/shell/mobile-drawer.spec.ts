import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubDashboard, WITHHELD_DASHBOARD } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'

/**
 * The phone shell's navigation drawer (issue #82, acceptance criterion "Mobile menu"), at 390px.
 *
 * Protects the drawer's modal-dialog contract, which a pointer-only test cannot see: it opens from
 * both of its openers (the app bar's hamburger and the tab bar's "More"); it closes from its own
 * button, from the backdrop, from choosing a destination and from `Escape`; focus moves into it on
 * open, is held inside while it is open (Tab and Shift+Tab wrap at the ends) and returns to the
 * opener on close; both openers report `aria-expanded` from the same state; and widening the
 * viewport past 1024px with the drawer open drops it, because the sidebar is back in the flow and
 * a leftover backdrop and inert page would block the whole screen.
 *
 * Why it exists: the admin this structure comes from did not close on `Escape` and stranded
 * keyboard users behind its backdrop. Each clause is its own test so a regression names itself.
 */

/** The two controls that open the drawer. */
type Opener = 'hamburger' | 'more'

/** The openers, each with the title its tests carry. */
const OPENERS: readonly { opener: Opener; title: string }[] = [
  { opener: 'hamburger', title: 'the hamburger' },
  { opener: 'more', title: '"More" in the tab bar' },
]

/**
 * Signs an administrator in and answers the pages the shell and its destinations request.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
const stubPanel = async (page: Page): Promise<void> => {
  await stubSignedIn(page, stubbedAdministrator)
  await stubHealthy(page)
  await stubDashboard(page, WITHHELD_DASHBOARD)
  await stubEmptyModules(page)
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
}

/**
 * Finds one of the two openers. While the drawer is open the page behind it is `inert` and absent
 * from the accessibility tree, so the opener is found by its accessible name as an attribute and
 * by its visible word rather than by role: that keeps the lookup valid in both states.
 * @param page The page showing the shell.
 * @param opener Which opener to find.
 * @returns The opener's locator.
 */
const openerOf = (page: Page, opener: Opener): Locator => {
  return opener === 'hamburger'
    ? page.locator('button[aria-label="Open the navigation"]')
    : page.locator('nav button', { hasText: 'More' })
}

/**
 * Opens the shell at 390x844 and returns its drawer dialog (not yet open).
 * @param page The Playwright page to drive.
 * @returns The drawer's locator.
 */
const gotoPhoneShell = async (page: Page): Promise<Locator> => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubPanel(page)
  await page.goto('/')
  await expect(page.getByRole('main')).toBeVisible()
  return page.getByRole('dialog', { name: 'Navigation' })
}

for (const { opener, title } of OPENERS) {
  test.describe(`opened from ${title}`, () => {
    test('it opens, and focus moves onto the close button', async ({ page }) => {
      const drawer = await gotoPhoneShell(page)
      await expect(drawer).toHaveCount(0)

      await openerOf(page, opener).click()

      await expect(drawer).toBeVisible()
      await expect(drawer).toHaveAttribute('aria-modal', 'true')
      await expect(drawer.getByRole('button', { name: 'Close the navigation' })).toBeFocused()
    })

    test('the opener reports aria-expanded from the drawer state', async ({ page }) => {
      const drawer = await gotoPhoneShell(page)
      const trigger = openerOf(page, opener)
      await expect(trigger).toHaveAttribute('aria-expanded', 'false')

      await trigger.click()
      await expect(drawer).toBeVisible()
      await expect(trigger).toHaveAttribute('aria-expanded', 'true')

      await drawer.getByRole('button', { name: 'Close the navigation' }).click()
      await expect(drawer).toHaveCount(0)
      await expect(trigger).toHaveAttribute('aria-expanded', 'false')
    })

    test('its own button closes it and focus returns to the opener', async ({ page }) => {
      const drawer = await gotoPhoneShell(page)
      const trigger = openerOf(page, opener)
      await trigger.click()

      await drawer.getByRole('button', { name: 'Close the navigation' }).click()

      await expect(drawer).toHaveCount(0)
      await expect(trigger).toBeFocused()
    })

    test('a tap on the backdrop closes it and focus returns to the opener', async ({ page }) => {
      const drawer = await gotoPhoneShell(page)
      const trigger = openerOf(page, opener)
      await trigger.click()
      await expect(drawer).toBeVisible()

      // The panel spans x 12..276; x 360 is the darkened page beside it.
      await page.mouse.click(360, 420)

      await expect(drawer).toHaveCount(0)
      await expect(trigger).toBeFocused()
    })

    test('Escape closes it and focus returns to the opener', async ({ page }) => {
      const drawer = await gotoPhoneShell(page)
      const trigger = openerOf(page, opener)
      await trigger.click()
      await expect(drawer).toBeVisible()

      await page.keyboard.press('Escape')

      await expect(drawer).toHaveCount(0)
      await expect(trigger).toBeFocused()
    })

    test('choosing a destination closes it and goes there', async ({ page }) => {
      const drawer = await gotoPhoneShell(page)
      await openerOf(page, opener).click()

      await drawer.getByRole('link', { name: 'Audit journal' }).click()

      await expect(page).toHaveURL('/settings/audit')
      await expect(drawer).toHaveCount(0)
      await expect(page.getByRole('main')).toBeVisible()
    })

    test('Tab and Shift+Tab are held inside while it is open', async ({ page }) => {
      const drawer = await gotoPhoneShell(page)
      await openerOf(page, opener).click()
      await expect(drawer.getByRole('button', { name: 'Close the navigation' })).toBeFocused()

      /**
       * Whether the focused element is inside the drawer.
       * @returns True when focus is on the dialog or one of its descendants.
       */
      const focusInside = async (): Promise<boolean> => {
        return drawer.evaluate((panel: HTMLElement): boolean => {
          return panel.contains(document.activeElement)
        })
      }

      // Forward past the last control must wrap round, never leave: more presses than the drawer
      // has controls guarantees the end is crossed at least once.
      const controls = await drawer.locator('a, button, [tabindex="0"]').count()
      for (let press = 0; press < controls + 3; press += 1) {
        await page.keyboard.press('Tab')
        expect(await focusInside()).toBe(true)
      }

      // Backward from the first control wraps to the last, and again stays inside.
      await drawer.getByRole('button', { name: 'Close the navigation' }).focus()
      await page.keyboard.press('Shift+Tab')
      expect(await focusInside()).toBe(true)
      await expect(drawer.getByRole('button', { name: 'Close the navigation' })).not.toBeFocused()
      for (let press = 0; press < controls + 3; press += 1) {
        await page.keyboard.press('Shift+Tab')
        expect(await focusInside()).toBe(true)
      }
    })
  })
}

test('widening past 1024px with the drawer open drops it and frees the page', async ({ page }) => {
  const drawer = await gotoPhoneShell(page)
  await openerOf(page, 'hamburger').click()
  await expect(drawer).toBeVisible()
  // The page behind is made inert while the drawer is open.
  await expect(page.locator('[inert]')).toHaveCount(1)

  await page.setViewportSize({ width: 1280, height: 800 })

  await expect(drawer).toHaveCount(0)
  await expect(page.locator('[inert]')).toHaveCount(0)
  await expect(page.getByRole('main')).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Navigation' })).toBeVisible()
  await expect(page.locator('button[aria-label="Open the navigation"]')).toBeHidden()
})
