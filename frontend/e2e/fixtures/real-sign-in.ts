import { expect, type Page } from '@playwright/test'
import { credentialsFor, type RealRole } from './real-credentials'

/**
 * Signs in through the sign-in screen, the way a person does, and waits for the signed-in shell.
 * @param page The page under test.
 * @param role The role whose login the environment carries.
 * @returns Resolves once the navigation landmark is drawn.
 */
export const signInAs = async (page: Page, role: RealRole): Promise<void> => {
  const { username, password } = credentialsFor(role)
  await signInWith(page, username, password)
  await expect(page.getByRole('navigation').first()).toBeVisible()
}

/**
 * Fills the sign-in form with the given login and submits it. Does not wait for any destination,
 * because a login holding a first password lands on the new-password screen instead of the shell.
 * @param page The page under test.
 * @param username The login name.
 * @param password The password.
 * @returns Resolves once the form has been submitted.
 */
export const signInWith = async (page: Page, username: string, password: string): Promise<void> => {
  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Username' }).fill(username)
  await page.getByRole('textbox', { name: 'Password' }).fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
}

/**
 * Signs out through the account menu.
 * @param page The page under test.
 * @returns Resolves once the sign-in screen is showing.
 */
export const signOut = async (page: Page): Promise<void> => {
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
}

/**
 * Chooses an interface language from the header's locale menu.
 * @param page The page under test.
 * @param language The language's own name: `English`, `Русский` or `Հայերեն`.
 * @returns Resolves once the option has been chosen.
 */
export const chooseLanguage = async (page: Page, language: string): Promise<void> => {
  await page.getByRole('banner').getByTestId('locale-switcher').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: language, exact: true }).click()
}
