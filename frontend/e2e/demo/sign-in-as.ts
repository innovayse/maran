import { expect, type Page } from '@playwright/test'

/** Which of the demo's two published logins to use. */
export type DemoRole = 'administrator' | 'customer'

/** A login as the sign-in screen prints it. */
export interface PublishedLogin {
  /** The login name. */
  username: string
  /** The password. */
  password: string
}

/**
 * Reads one published login off the sign-in screen. The demo publishes its credentials there and
 * nowhere else, so the suite never carries a password of its own: a spec that signed in with a
 * value written into the repository would keep passing while the page showed something else.
 * @param page A page already on the sign-in screen.
 * @param role Which login to read.
 * @returns The username and password exactly as printed.
 */
export const readPublishedLogin = async (page: Page, role: DemoRole): Promise<PublishedLogin> => {
  const block = page.getByTestId(`demo-identity-${role}`)
  await expect(block, `the sign-in screen prints no ${role} login`).toBeVisible()
  const username = (await block.getByTestId('demo-username').innerText()).trim()
  const password = (await block.getByTestId('demo-password').innerText()).trim()
  expect(username, `the ${role} username is empty`).not.toBe('')
  expect(password, `the ${role} password is empty`).not.toBe('')
  return { username, password }
}

/**
 * Signs in as one of the published logins through the sign-in form, whatever the interface
 * language: the fields and the submit button are found by what they are (autocomplete token,
 * input type, submit button), not by their translated labels.
 * @param page A page that has not navigated yet.
 * @param role Which login to use.
 * @returns Resolves once the panel has left the sign-in screen.
 */
export const signInAs = async (page: Page, role: DemoRole): Promise<void> => {
  await page.goto('/login')
  const login = await readPublishedLogin(page, role)
  await page.locator('input[autocomplete="username"]').fill(login.username)
  await page.locator('input[type="password"]').fill(login.password)
  await page.locator('button[type="submit"]').click()
  await expect(page, 'the demo refused its own published login').not.toHaveURL(/\/login/)
  await expect(page.getByRole('main')).toBeVisible()
}
