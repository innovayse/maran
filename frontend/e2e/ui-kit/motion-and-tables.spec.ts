import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubManagedPlans } from '../fixtures/stub-plans-route'
import type { Account } from '../../src/types/account'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'

// The kit's overlay motion, the confirmation's named action and safe starting focus, and the narrow
// table's scroll cue and sticky action column (issues #217 and #218), all driven through real
// screens, because the kit has no page of its own.
//
// Motion is asserted on the COMPUTED transition, not by timing a frame: a timing assertion is
// flaky, while "a reader who asked for less motion gets a 0s transition" is a value.

/** The accounts module, which licenses the plans screen. */
const ACCOUNTS_LICENSED: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

/** The databases module, whose list carries a row actions menu. */
const DATABASES_LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

/** The owner the database rows need. */
const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** One database with long names, so the row is wider than a phone. */
const SHOP: Database = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  name: 'storefront_catalogue_archive',
  fullName: 'alice_storefront_catalogue_archive',
  dbUserName: 'alice_storefront_catalogue_archive_user',
  createdAt: '2026-09-01T10:00:00Z',
}

/**
 * Opens the plans list with the management endpoints stubbed.
 * @param page The page under test.
 * @returns Resolves once the list shows its plans.
 */
const openPlans = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, ACCOUNTS_LICENSED)
  await stubManagedPlans(page)
  await page.goto('/plans')
  await expect(page.getByRole('button', { name: 'Delete Business' })).toBeVisible()
}

/**
 * Opens the databases list with one wide row.
 * @param page The page under test.
 * @returns Resolves once the row is on screen.
 */
const openDatabases = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, DATABASES_LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [SHOP])
  await page.goto('/databases')
  await expect(page.getByRole('row').filter({ hasText: SHOP.fullName })).toBeVisible()
}

/**
 * Reads the computed transition duration of the open dialog's backdrop and panel.
 * @param page The page under test.
 * @returns The two `transition-duration` values.
 */
const dialogTransition = (page: Page): Promise<string[]> => {
  return page.evaluate(() => {
    const panel = document.querySelector('[role="dialog"]')
    const backdrop = panel?.parentElement
    return [backdrop, panel].map((element) => {
      return element instanceof HTMLElement ? getComputedStyle(element).transitionDuration : 'missing'
    })
  })
}

test('the confirmation names its action and never opens with that action focused', async ({ page }) => {
  await openPlans(page)

  await page.getByRole('button', { name: 'Delete Business' }).click()

  const dialog = page.getByRole('dialog', { name: 'Delete plan Business' })
  await expect(dialog).toBeVisible()
  // The caller's `confirmLabel` is the button's name, not the generic answer.
  const confirm = dialog.getByRole('button', { name: 'Delete', exact: true })
  await expect(confirm).toBeVisible()
  // A reflexive Enter must not delete anything: focus starts inside the dialog, never on the
  // destructive answer.
  await expect(dialog.locator(':focus')).toHaveCount(1)
  await expect(confirm).not.toBeFocused()
  await expect(dialog.getByRole('button', { name: 'Yes, do it' })).toHaveCount(0)
})

test('the confirmation still closes on Escape and leaves no dialog behind', async ({ page }) => {
  await openPlans(page)

  await page.getByRole('button', { name: 'Delete Business' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')

  // The 200ms leave runs first; the dialog must be gone once it has.
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Delete Business' })).toBeFocused()
})

test('a dialog animates for a reader with no motion preference', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await openPlans(page)

  await page.getByRole('button', { name: 'Delete Business' }).click()
  // Read during the 300ms enter, while the transition classes are applied.
  const durations = await dialogTransition(page)

  expect(durations).toEqual(['0.3s, 0.3s', '0.3s, 0.3s'])
})

test('a dialog does not animate for a reader who asked for less motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await openPlans(page)

  await page.getByRole('button', { name: 'Delete Business' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  const durations = await dialogTransition(page)

  for (const duration of durations) {
    expect(duration.split(',').every((part) => {return Number.parseFloat(part) === 0})).toBe(true)
  }
})

test('a row menu does not animate for a reader who asked for less motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await openDatabases(page)

  await page.getByRole('button', { name: `Actions for ${SHOP.fullName}` }).click()
  const menu = page.getByRole('menu')
  await expect(menu).toBeVisible()

  const duration = await menu.evaluate((element) => {return getComputedStyle(element).transitionDuration})
  expect(duration.split(',').every((part) => {return Number.parseFloat(part) === 0})).toBe(true)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('menu')).toHaveCount(0)
})

test.describe('on a phone', () => {
  test.use({ viewport: { width: 390, height: 844 } })

  test('an overflowing table shows a scroll cue that follows the scroll position', async ({ page }) => {
    await openDatabases(page)
    const scroller = page.getByTestId('table-scroller').first()

    const overflows = await scroller.evaluate((element) => {return element.scrollWidth > element.clientWidth + 1})
    expect(overflows).toBe(true)
    // At the start: more to the right, nothing to the left.
    await expect(page.getByTestId('table-fade-end').first()).toBeVisible()
    await expect(page.getByTestId('table-fade-start')).toHaveCount(0)

    await scroller.evaluate((element) => {
      element.scrollLeft = element.scrollWidth
    })

    // At the end: the cues swap sides.
    await expect(page.getByTestId('table-fade-start').first()).toBeVisible()
    await expect(page.getByTestId('table-fade-end')).toHaveCount(0)
  })

  test('the row action stays on screen while the row is scrolled', async ({ page }) => {
    await openDatabases(page)
    const trigger = page.getByRole('button', { name: `Actions for ${SHOP.fullName}` })
    const scroller = page.getByTestId('table-scroller').first()

    const box = await scroller.boundingBox()
    expect(box).not.toBeNull()
    const right = (box?.x ?? 0) + (box?.width ?? 0)

    for (const position of [0, 'end'] as const) {
      await scroller.evaluate((element, to) => {
        element.scrollLeft = to === 'end' ? element.scrollWidth : 0
      }, position)
      const triggerBox = await trigger.boundingBox()
      expect(triggerBox).not.toBeNull()
      // The whole trigger is inside the scroller's visible box, at either end of the scroll.
      expect((triggerBox?.x ?? 0) + (triggerBox?.width ?? 0)).toBeLessThanOrEqual(right + 0.5)
      expect(triggerBox?.x ?? 0).toBeGreaterThanOrEqual(box?.x ?? 0)
    }
  })
})

// UiFilterTrigger and UiBulkBar are new kit primitives with no screen using them yet. Their cases
// are written against the accounts list, the first screen planned to adopt both; they are `fixme`
// until that screen renders them, and are switched on in the same change that wires them in.
test.describe('filter trigger and bulk bar', () => {
  test.fixme('the filter trigger is named, emits a press, and announces the active count', async ({ page }) => {
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubModules(page, ACCOUNTS_LICENSED)
    await stubAccounts(page, [ALICE])
    await page.goto('/accounts')

    const trigger = page.getByTestId('filter-trigger')
    await expect(trigger).toHaveAccessibleName(/^Filters/)
    await expect(trigger).toHaveAttribute('aria-haspopup', 'dialog')
    await expect(page.getByTestId('filter-trigger-dot')).toHaveCount(0)
    await expect(page.getByTestId('filter-trigger-status')).toHaveText('')

    await trigger.click()
    await expect(page.getByRole('dialog')).toBeVisible()
  })

  test.fixme('the bulk bar is hidden at zero and announces the selection politely', async ({ page }) => {
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubModules(page, ACCOUNTS_LICENSED)
    await stubAccounts(page, [ALICE])
    await page.goto('/accounts')

    await expect(page.getByTestId('bulk-bar')).toHaveCount(0)
    const status = page.getByTestId('bulk-bar-status')
    await expect(status).toHaveAttribute('aria-live', 'polite')
    await expect(status).toHaveText('')

    await page.getByRole('checkbox', { name: /alice/ }).check()

    await expect(page.getByTestId('bulk-bar')).toBeVisible()
    await expect(status).toHaveText('1 selected')
  })
})
