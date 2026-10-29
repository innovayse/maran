import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabaseActions, stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'

// Where keyboard focus goes when an overlay lets go of it.
//
// Two defects, found by driving the panel with the keyboard alone:
//
//   - Tab inside an open row-actions menu closed it and sent focus to the top of the document
//     ("Skip to content"), because the menu panel is teleported to the end of <body> and "the next
//     tabbable after the focused item" is therefore the end of the page, where Tab wraps. The menu
//     button pattern wants focus to continue from the TRIGGER.
//   - Closing a created-credential dialog dropped focus on <body>: it opens from the create form's
//     submit button, which is disabled while the request runs, so the browser reported <body> as
//     the opener and there was nothing to return to.
//
// Each assertion names the element focus must be on, or the region it must be inside. "Focus is
// not on the skip link" alone would pass against a focus that fell on <body> (rules/testing.md).

/** The one account the databases screen offers. */
const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** Two rows, so the first row's trigger has a next tabbable element and the second has a previous one. */
const DATABASES: Database[] = [
  {
    id: '33333333-3333-3333-3333-333333333333',
    accountId: ALICE.id,
    name: 'shop',
    fullName: 'alice_shop',
    dbUserName: 'alice_shopuser',
    createdAt: '2026-09-01T10:00:00Z',
  },
  {
    id: '55555555-5555-5555-5555-555555555555',
    accountId: ALICE.id,
    name: 'blog',
    fullName: 'alice_blog',
    dbUserName: 'alice_bloguser',
    createdAt: '2026-09-02T10:00:00Z',
  },
]

/** The catalogue that licenses the databases screen. */
const LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

/**
 * Opens the databases screen with both rows, in English.
 * @param page The page under test.
 * @param databases The rows the list returns.
 * @returns Resolves once the screen has been requested.
 */
const openDatabasesScreen = async (page: Page, databases: Database[]): Promise<void> => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, databases)
  await stubDatabaseActions(page, DATABASES[0].dbUserName)
  await page.goto('/databases')
}

/**
 * The actions trigger of one database row.
 * @param page The page under test.
 * @param fullName The row's prefixed database name.
 * @returns The trigger's locator.
 */
const actionsTrigger = (page: Page, fullName: string): Locator => {
  return page.getByRole('button', { name: `Actions for ${fullName}` })
}

test('Tab out of an open row menu closes it and lands on the element after its trigger', async ({ page }) => {
  await openDatabasesScreen(page, DATABASES)

  await actionsTrigger(page, 'alice_shop').click()
  await expect(page.getByRole('menuitem', { name: 'Reset password' })).toBeFocused()

  await page.keyboard.press('Tab')

  await expect(page.getByRole('menu')).toHaveCount(0)
  // The element after the first row's trigger, in document order, is the second row's trigger —
  // not the skip link at the top of the document.
  await expect(actionsTrigger(page, 'alice_blog')).toBeFocused()
  await expect(page.getByRole('link', { name: 'Skip to content' })).not.toBeFocused()
})

test('Shift+Tab out of an open row menu lands on the element before its trigger', async ({ page }) => {
  await openDatabasesScreen(page, DATABASES)

  await actionsTrigger(page, 'alice_blog').click()
  await expect(page.getByRole('menuitem', { name: 'Reset password' })).toBeFocused()

  await page.keyboard.press('Shift+Tab')

  await expect(page.getByRole('menu')).toHaveCount(0)
  await expect(actionsTrigger(page, 'alice_shop')).toBeFocused()
})

test('closing a created-credential dialog returns focus to a visible element inside main', async ({
  page,
}) => {
  await openDatabasesScreen(page, [])

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Database name' }).fill('shop')
  await page.getByRole('textbox', { name: 'User name' }).fill('shopuser')
  await page.getByRole('button', { name: 'Create database' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByRole('button', { name: 'Done' }).click()
  await expect(dialog).toHaveCount(0)

  // Read from the document: the defect's signature is focus on <body>, which no role locator can
  // be asked about. Inside <main>, and visible, is what a keyboard user can keep working from.
  await expect
    .poll(() => {
      return page.evaluate(() => {
        const active = document.activeElement
        const main = document.querySelector('main')
        return (
          active !== null &&
          active !== document.body &&
          main !== null &&
          main.contains(active) &&
          active.checkVisibility()
        )
      })
    })
    .toBe(true)
})
