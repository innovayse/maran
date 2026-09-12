import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubEmptyModules, stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'

// Where `UiDropdown` lands focus when it opens. Two menus in this panel are built from the same
// component and want opposite answers, so both are driven here through their real screens:
//
//   - the header's language picker is a set of `menuitemradio` alternatives, and opening it on the
//     FIRST one made trigger-Enter-Enter — or one held Enter, whose key repeat delivers twice —
//     select `English` from a Russian interface, silently. Measured live before this fix: the item
//     focused on open was `English` with `aria-checked="false"`, and the second Enter left
//     `maran.locale` reading `en`.
//   - a table row's actions menu is a list of commands where "first" is the right answer and
//     nothing is selected at all.
//
// Every assertion below names the item it expects — by its accessible name — rather than checking
// that SOMETHING has focus: "an item is focused" was true of the defect too (rules/testing.md,
// assert the value, not a bound).

/** The single account the databases screen offers, so the rows have an owner to render. */
const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** One database, which is one row, which is one command menu of two commands. */
const SHOP: Database = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  name: 'shop',
  fullName: 'alice_shop',
  dbUserName: 'alice_shopuser',
  createdAt: '2026-09-01T10:00:00Z',
}

/** The catalogue that licenses the databases screen. */
const DATABASES_LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

/** The key `stores/locale.ts` persists the chosen interface language under. */
const LOCALE_STORAGE_KEY = 'maran.locale'

/**
 * The header's language trigger, located by role rather than by its accessible name: that name is
 * itself translated, so naming it in English would stop finding it in a Russian interface.
 * @param page The page under test.
 * @returns The trigger button of the header's language menu.
 */
const localeTrigger = (page: Page): Locator => {
  return page.getByRole('banner').locator('[aria-haspopup="menu"]')
}

/**
 * The actions trigger of the one database row.
 * @param page The page under test.
 * @returns The row's menu trigger.
 */
const rowActionsTrigger = (page: Page): Locator => {
  return page.getByRole('button', { name: `Actions for ${SHOP.fullName}` })
}

/**
 * Opens the databases screen with one row, in English.
 * @param page The page under test.
 * @returns Resolves once the row is on screen.
 */
const openDatabasesScreen = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubModules(page, DATABASES_LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [SHOP])

  await page.goto('/databases')
  // By cell rather than by text: `alice_shop` is also a prefix of the user cell's `alice_shopuser`,
  // so a plain text match resolves to two elements and fails strict mode before anything is measured.
  await expect(page.getByRole('cell', { name: SHOP.fullName, exact: true })).toBeVisible()
}

/**
 * Opens the dashboard with the interface already in Russian — the state the defect was measured
 * from, where the chosen language is the SECOND item of the menu.
 * @param page The page under test.
 * @returns Resolves once the Russian heading is on screen.
 */
const openRussianShell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'ru')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  // The whole point of the spec is what a RUSSIAN interface does, so the language in force is
  // asserted before anything is pressed rather than assumed from the seed.
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()
}

test('a menu of languages opens on the chosen language, not on the first one', async ({ page }) => {
  await openRussianShell(page)

  await localeTrigger(page).click()

  // Positive control on the probe: the menu really opened, and it really holds all three languages
  // with English first. Without it, a run where the panel never appeared would report the same
  // silence as one where the wrong item had focus.
  await expect(page.getByRole('menuitemradio')).toHaveCount(3)
  await expect(page.getByRole('menuitemradio').first()).toHaveAccessibleName('English')

  const russian = page.getByRole('menuitemradio', { name: 'Русский', exact: true })
  await expect(russian).toBeFocused()
  await expect(russian).toHaveAttribute('aria-checked', 'true')
  // Named the other way round as well: the item that used to take focus must not have it. The
  // defect focused `English` with `aria-checked="false"`, and that exact pair is what is refused.
  await expect(page.getByRole('menuitemradio', { name: 'English', exact: true })).not.toBeFocused()
})

test('opening the language menu and confirming keeps the language already in force', async ({ page }) => {
  await openRussianShell(page)

  // The gesture as a keyboard user performs it: the trigger, then Enter to open, then Enter on
  // whatever the menu offered. One held Enter delivers the same two presses through key repeat.
  await localeTrigger(page).focus()
  await page.keyboard.press('Enter')
  await page.keyboard.press('Enter')

  // The resulting STATE, not the focus: the interface, the document's language and the stored
  // preference all still say Russian. Against the defect all three read English.
  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'ru')
  const stored = await page.evaluate((key) => {
    return window.localStorage.getItem(key)
  }, LOCALE_STORAGE_KEY)
  expect(stored).toBe('ru')
})

test("a table row's command menu still opens on its first command", async ({ page }) => {
  await openDatabasesScreen(page)

  await rowActionsTrigger(page).click()

  // Positive control: the menu opened and holds both commands in the order the page writes them.
  await expect(page.getByRole('menuitem')).toHaveCount(2)

  // Nothing in a command menu is chosen, so the first item is the right landing place and must
  // stay it — the fix must not have flipped the behaviour for every menu in the panel.
  await expect(page.getByRole('menuitem', { name: 'Reset password' })).toBeFocused()
  await expect(page.getByRole('menuitem', { name: 'Drop' })).not.toBeFocused()
})

test('Escape closes a menu and gives focus back to its trigger', async ({ page }) => {
  await openDatabasesScreen(page)

  const trigger = rowActionsTrigger(page)
  await trigger.click()
  await expect(page.getByRole('menuitem', { name: 'Reset password' })).toBeFocused()

  await page.keyboard.press('Escape')

  await expect(page.getByRole('menu')).toHaveCount(0)
  await expect(trigger).toBeFocused()
})

// The regression guard for the two fixes this component carries from yesterday: the capture-phase
// handler that moves focus to the trigger BEFORE a command runs, and `close()` declining to take
// focus back when something else already holds it. Breaking either is worse than the defect being
// fixed here — a keyboard user is left with no focus at all behind a modal they cannot reach.
test('choosing a row command opens its dialog and leaves focus inside that dialog', async ({ page }) => {
  await openDatabasesScreen(page)

  await rowActionsTrigger(page).click()
  const drop = page.getByRole('menuitem', { name: 'Drop' })
  await expect(drop).toBeVisible()
  await drop.click()

  // The command RAN. This is the half that the capture-phase handler must not swallow: closing the
  // panel in the capture phase took the item's own listener down with it and the menu's commands
  // did nothing at all.
  const dialog = page.getByRole('dialog', { name: `Drop database ${SHOP.fullName}` })
  await expect(dialog).toBeVisible()

  // And focus followed the dialog rather than being pulled back to the trigger behind it. Read
  // from the document because the question is about `document.activeElement` itself, and named
  // both ways: inside the dialog, and not on the menu trigger.
  const focus = await page.evaluate(() => {
    const panel = document.querySelector('[role="dialog"]')
    const active = document.activeElement
    return {
      inside: panel !== null && active !== null && panel.contains(active),
      label: active?.getAttribute('aria-label') ?? '',
    }
  })
  expect(focus.inside).toBe(true)
  expect(focus.label).not.toContain('Actions for')
})
