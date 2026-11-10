import { expect, type Page } from '@playwright/test'
import { openDestination } from './real-sidebar-walk'

/** What the create-account form is filled with. */
export interface NewAccount {
  /** The account's system name. */
  name: string
  /** The owner's e-mail address. */
  ownerEmail: string
  /** When set, the owner gets this initial password instead of an invitation. */
  initialPassword?: string
}

/**
 * Reaches the account list from the sidebar, opens the create form from the list, fills it and
 * submits it. Clicks only, so the route table, the menu and the list's own button are all driven.
 * @param page The page under test, signed in as someone who may create accounts.
 * @param account What to create.
 * @returns Resolves once the new account's own page is showing.
 */
export const createAccountByClicking = async (page: Page, account: NewAccount): Promise<void> => {
  await openDestination(page, 'Accounts')
  await page.getByRole('link', { name: 'New account' }).first().click()
  await page.getByRole('textbox', { name: 'Username' }).fill(account.name)
  await page.getByRole('textbox', { name: 'Primary domain' }).fill(`${account.name}.example.test`)
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  const plans = page.getByRole('option')
  // A panel with no plan can create no account at all; say so instead of timing out on a click.
  await expect(plans, 'this panel has no plans, so no account can be created').not.toHaveCount(0)
  await plans.first().click()
  await page.getByLabel('Owner email').fill(account.ownerEmail)
  if (account.initialPassword !== undefined) {
    await page.getByRole('radio', { name: 'Set an initial password' }).check({ force: true })
    await page.getByLabel('Initial password', { exact: true }).fill(account.initialPassword)
  }
  await page.getByRole('button', { name: 'Create account' }).click()
  // Creating opens the account's own page, read back from the panel (issue #78).
  await expect(page).toHaveURL(/\/accounts\/[0-9a-f-]{36}$/)
  await expect(page.getByRole('heading', { name: account.name })).toBeVisible()
}

/**
 * Suspends the open account and reactivates it, through the dialogs.
 * @param page The page under test, on an account's own page.
 * @returns Resolves once the account is active again.
 */
export const suspendAndReactivate = async (page: Page): Promise<void> => {
  await page.getByRole('button', { name: 'Suspend', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Suspend account' }).click()
  await expect(page.getByRole('button', { name: 'Reactivate' })).toBeVisible()
  await page.getByRole('button', { name: 'Reactivate' }).click()
  await expect(page.getByRole('button', { name: 'Suspend', exact: true })).toBeVisible()
}

/**
 * Deletes the open account by typing its name into the confirmation.
 * @param page The page under test, on an account's own page.
 * @param name The account's name.
 * @returns Resolves once the list is showing again.
 */
export const deleteAccountByClicking = async (page: Page, name: string): Promise<void> => {
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('textbox', { name: 'Type the account name to confirm' }).fill(name)
  await page.getByRole('button', { name: 'Delete this account' }).click()
  await expect(page).toHaveURL('/accounts')
}
