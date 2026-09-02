import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  stubDatabaseActions,
  stubDatabases,
  stubbedCreatedPassword,
  stubbedResetPassword,
} from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const SHOP: Database = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  name: 'shop',
  fullName: 'alice_shop',
  dbUserName: 'alice_shopuser',
  createdAt: '2026-08-01T10:00:00Z',
}

/** Puts the screen in front of an operator, with whatever databases the case needs already created. */
const openScreen = async (page: Page, databases: Database[], startAt = '/databases'): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, databases)
  await stubDatabaseActions(page, SHOP.dbUserName)
  await page.goto(startAt)
}

/** Fills the create form and submits it, which is the path that mints a password. */
const createDatabase = async (page: Page): Promise<void> => {
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Database name' }).fill('shop')
  await page.getByRole('textbox', { name: 'User name' }).fill('shopuser')
  await page.getByRole('button', { name: 'Create database' }).click()
}

/**
 * Opens a row's actions menu and chooses one of its commands.
 *
 * The commands moved out of the row and behind a trigger, so a test that
 * clicked a button in the row now has to open the menu first. Written as a
 * helper rather than repeated, because every one of these tests reaches its
 * command the same way, and the next one should not have to rediscover it.
 */
const chooseRowAction = async (page: Page, row: Locator, name: string, command: string): Promise<void> => {
  await row.getByRole('button', { name: `Actions for ${name}` }).click()
  // The menu is queried from the PAGE, not from the row: it is rendered into
  // `body` so that the table's own horizontal scroll cannot clip it, which
  // means it is not a descendant of the row that opened it.
  await page.getByRole('menuitem', { name: command }).click()
}

test('the generated password is shown once and is gone after a reload', async ({ page }) => {
  await openScreen(page, [])
  await createDatabase(page)

  await expect(page.getByTestId('database-password')).toHaveText(stubbedCreatedPassword)

  await page.reload()

  await expect(page.getByTestId('database-password')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedPassword)).toHaveCount(0)
})

// The store outlives the page, so a route change that happens while the dialog is open — the back
// button here, an expired session redirecting to the sign-in screen in the field — would otherwise
// leave the password in memory for the next visit to this screen to render.
//
// The back button rather than a sidebar click on purpose: the dialog is modal, so it correctly
// intercepts every pointer event on the shell behind it, and a click could not reach the sidebar.
test('the generated password is gone after navigating away and back', async ({ page }) => {
  await openScreen(page, [], '/')
  await page.getByRole('navigation').getByRole('link', { name: 'Databases' }).click()
  await createDatabase(page)

  await expect(page.getByTestId('database-password')).toBeVisible()

  await page.goBack()
  await page.goForward()

  await expect(page.getByRole('heading', { level: 1, name: 'Databases' })).toBeVisible()
  await expect(page.getByTestId('database-password')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedPassword)).toHaveCount(0)
})

test('the dialog says plainly that the password cannot be shown again', async ({ page }) => {
  await openScreen(page, [])
  await createDatabase(page)

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('This password is shown once.')
  await expect(dialog).toContainText('it cannot be shown again')
  // And it names the one way back, so nobody goes looking for a support path that does not exist.
  await expect(dialog).toContainText('reset the password from the list')
})

test('the dialog shows the prefixed database and user, not the bare names that were typed', async ({
  page,
}) => {
  await openScreen(page, [])
  await createDatabase(page)

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('alice_shop')
  await expect(dialog).toContainText('alice_shopuser')
})

// A form card once clipped its own dropdown, and a plan option rendered but was unclickable, both
// with green tests. A hit test at the control's own centre is what tells those apart.
test('the copy control is genuinely reachable at its own centre, not merely present', async ({ page }) => {
  await openScreen(page, [])
  await createDatabase(page)

  const copyButton = page.getByRole('button', { name: /Copy password/ })
  await expect(copyButton).toBeVisible()
  const box = await copyButton.boundingBox()
  expect(box).not.toBeNull()

  const hitsTheButton = await page.evaluate(
    ({ x, y }) => {
      const hit = document.elementFromPoint(x, y)
      return hit !== null && hit.closest('button') !== null
    },
    { x: (box?.x ?? 0) + (box?.width ?? 0) / 2, y: (box?.y ?? 0) + (box?.height ?? 0) / 2 },
  )
  expect(hitsTheButton).toBe(true)
})

test('the copy control puts the password on the clipboard', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await openScreen(page, [])
  await createDatabase(page)

  await page.getByRole('button', { name: /Copy password/ }).click()

  await expect(page.getByRole('button', { name: /Copied/ })).toBeVisible()
  const clipboard = await page.evaluate(() => {
    return navigator.clipboard.readText()
  })
  expect(clipboard).toEqual(stubbedCreatedPassword)
})

// A browser can refuse the clipboard outright. The dialog must not then report a copy that never
// happened: the operator would close it believing the value is saved, and it would be gone.
test('a blocked clipboard leaves the control unchanged and the password on screen', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: () => {
          return Promise.reject(new Error('clipboard blocked'))
        },
      },
    })
  })
  await openScreen(page, [])
  await createDatabase(page)

  await page.getByRole('button', { name: /Copy password/ }).click()

  await expect(page.getByRole('button', { name: /Copied/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Copy password' })).toBeVisible()
  await expect(page.getByTestId('database-password')).toHaveText(stubbedCreatedPassword)
})

// The value cannot be recovered, so a mis-aimed click beside the panel must not destroy it.
test('a click on the backdrop does not close the credential dialog', async ({ page }) => {
  await openScreen(page, [])
  await createDatabase(page)

  await expect(page.getByRole('dialog')).toBeVisible()
  await page.mouse.click(5, 5)

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByTestId('database-password')).toHaveText(stubbedCreatedPassword)
})

// The defect underneath the Escape one, and the reason that mutant first survived: with `v-if`
// plus a literal `:open="true"` the modal was created with the prop already true, so its
// open-watcher never ran and focus never entered the dialog. Escape went nowhere, the focus trap
// was inert, and a keyboard user was left outside a dialog that looked perfectly correct.
test('opening the credential dialog moves focus into it', async ({ page }) => {
  await openScreen(page, [])
  await createDatabase(page)

  await expect(page.getByRole('dialog')).toBeVisible()
  const focusIsInsideTheDialog = await page.evaluate(() => {
    const dialog = document.querySelector('[role="dialog"]')
    return dialog !== null && document.activeElement !== null && dialog.contains(document.activeElement)
  })
  expect(focusIsInsideTheDialog).toBe(true)
})

// The likelier of the two accidents: one keystroke, muscle memory, and nothing to undo it.
// Every other dialog in the panel still closes on Escape — only this one opts out.
test('pressing Escape does not close the credential dialog', async ({ page }) => {
  await openScreen(page, [])
  await createDatabase(page)

  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByTestId('database-password')).toHaveText(stubbedCreatedPassword)
})

test('closing the dialog ends the only showing the password gets', async ({ page }) => {
  await openScreen(page, [])
  await createDatabase(page)

  await page.getByRole('button', { name: 'Done' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedPassword)).toHaveCount(0)
})

// Reset is the only recovery there is, so it has to be on the screen and it has to work.
test('resetting the password is offered on the list and shows a new one once', async ({ page }) => {
  await openScreen(page, [SHOP])

  const row = page.getByRole('row').filter({ hasText: 'alice_shop' })
  await chooseRowAction(page, row, 'alice_shop', 'Reset password')
  await expect(row).toContainText('The current one stops working immediately.')
  await row.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByTestId('database-password')).toHaveText(stubbedResetPassword)
  await expect(page.getByRole('dialog')).toContainText('alice_shopuser')

  await page.reload()
  await expect(page.getByText(stubbedResetPassword)).toHaveCount(0)
})

test('dropping a database asks for confirmation before it is done', async ({ page }) => {
  await openScreen(page, [SHOP])

  const row = page.getByRole('row').filter({ hasText: 'alice_shop' })
  await chooseRowAction(page, row, 'alice_shop', 'Drop')
  await expect(row).toContainText('nothing here can bring them back')

  await row.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByRole('row').filter({ hasText: 'alice_shop' })).toBeVisible()
})

test('the row commands live behind one trigger and are reachable from the keyboard', async ({
  page,
}) => {
  await openScreen(page, [SHOP])

  const row = page.getByRole('row').filter({ hasText: 'alice_shop' })
  // Nothing is on the row until the menu is opened: that is the point of moving
  // them, and a test that only clicked through the helper would pass just as
  // well if both buttons were still sitting there beside the trigger.
  await expect(page.getByRole('menuitem')).toHaveCount(0)

  // Named for the row it acts on, not just "Actions": with one trigger per row,
  // an accessible name that repeats gives a screen-reader user a list of
  // identical controls and no way to tell which row they are on.
  const trigger = row.getByRole('button', { name: 'Actions for alice_shop' })
  await trigger.focus()
  await page.keyboard.press('Enter')

  // Enter opens the menu on its FIRST item, so focus is already inside it.
  await expect(page.getByRole('menuitem', { name: 'Reset password' })).toBeFocused()
  await page.keyboard.press('ArrowDown')
  await expect(page.getByRole('menuitem', { name: 'Drop' })).toBeFocused()

  // Escape closes it and hands focus back, rather than leaving a keyboard user
  // at the top of the document.
  await page.keyboard.press('Escape')
  await expect(page.getByRole('menuitem')).toHaveCount(0)
  await expect(trigger).toBeFocused()
})

test('the open menu is not clipped by the table it was opened inside', async ({ page }) => {
  await openScreen(page, [SHOP])

  const row = page.getByRole('row').filter({ hasText: 'alice_shop' })
  await row.getByRole('button', { name: 'Actions for alice_shop' }).click()

  // The last command is the one a clip eats first, so it is the one to check.
  const command = page.getByRole('menuitem', { name: 'Drop' })
  await expect(command).toBeInViewport()

  // Being "visible" is not enough: Playwright's visibility does not account for
  // an ancestor's `overflow` cutting an element off, and that is exactly what
  // happened here — `UiTable` scrolls horizontally inside its own container, so
  // the menu was drawn and then clipped at the container's edge. Asking the
  // document what is actually painted at the command's own centre is what
  // distinguishes "rendered" from "reachable".
  const box = await command.boundingBox()
  expect(box).not.toBeNull()
  const reachable = await page.evaluate(
    ({ x, y }) => {
      const painted = document.elementFromPoint(x, y)
      return painted?.closest('[role="menuitem"]')?.textContent?.trim() ?? null
    },
    { x: (box?.x ?? 0) + (box?.width ?? 0) / 2, y: (box?.y ?? 0) + (box?.height ?? 0) / 2 },
  )

  expect(reachable).toBe('Drop')

  // Painted is not the same as painted in the right place. Positioning the panel
  // in viewport coordinates means a broken measurement leaves it at 0,0 — still
  // in the viewport, still hit-testable, and still passing everything above. So
  // the panel is also checked against the trigger it belongs to.
  const trigger = await row
    .getByRole('button', { name: 'Actions for alice_shop' })
    .boundingBox()
  const panel = await page.getByRole('menu').boundingBox()
  expect(trigger).not.toBeNull()
  expect(panel).not.toBeNull()

  // Opened downwards, just under the trigger.
  expect(panel?.y ?? 0).toBeGreaterThanOrEqual(trigger?.y ?? 0)
  // And aligned to the trigger's right edge, which is what `align="end"` buys in
  // the last column of a table.
  const panelRight = (panel?.x ?? 0) + (panel?.width ?? 0)
  const triggerRight = (trigger?.x ?? 0) + (trigger?.width ?? 0)
  expect(Math.abs(panelRight - triggerRight)).toBeLessThan(2)
})
