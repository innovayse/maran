import { expect, test } from '@playwright/test'
import { stubLogin, stubSignedOut, stubbedRefresh, stubbedSignIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules, stubModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'

test('an anonymous visit to a panel page lands on the sign-in screen and remembers where it was headed', async ({
  page,
}) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/accounts')

  await expect(page).toHaveURL('/login?redirect=/accounts')
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
})

test('signing in returns the visitor to the page they originally asked for', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  // The catalogue has to carry the accounts module: the licence guard runs after this
  // one, and an unlicensed route would send the visitor to the upgrade page instead —
  // correctly, but that is a different spec's subject.
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await page.route('**/api/v1/accounts', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })

  await page.goto('/accounts')
  await stubLogin(page, stubbedSignIn)
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Password' }).fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page).toHaveURL('/accounts')
})

test('a refused sign-in shows the backend message verbatim and stays on the screen', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubLogin(page, {
    status: 401,
    code: 'InvalidCredentialsUnauthorized',
    detail: 'The username or password is incorrect.',
  })

  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Password' }).fill('wrong')
  await page.getByRole('button', { name: 'Sign in' }).click()

  // Rendered as the backend sent it: the SPA owns no error text (rules/vue.md).
  await expect(page.getByText('The username or password is incorrect.')).toBeVisible()
  await expect(page).toHaveURL('/login')
})

test('the sign-in form can be completed with the keyboard alone', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubLogin(page, stubbedSignIn)

  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Username' }).focus()
  await page.keyboard.type('admin')
  await page.keyboard.press('Tab')
  await page.keyboard.type('correct horse battery staple')
  await page.keyboard.press('Enter')

  await expect(page).toHaveURL('/')
})

test('a signed-in visitor is sent out of the sign-in screen rather than shown it again', async ({ page }) => {
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/setup/state', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '{"isComplete":true}' })
  })
  await page.route('**/api/v1/auth/refresh', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(stubbedRefresh),
    })
  })

  await page.goto('/login')

  await expect(page).toHaveURL('/')
})

test('the password field starts masked and the reveal toggle shows it', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/login')
  const password = page.getByRole('textbox', { name: 'Password' })
  await password.fill('correct horse battery staple')

  // Masked until asked: a reload that restores a filled field must not put the
  // password on screen for whoever is standing behind the person typing.
  await expect(password).toHaveAttribute('type', 'password')

  // The toggle draws an icon, so its accessible name is the only thing naming
  // it. Resolving it BY that name is the assertion: a control found by role and
  // name is a control a screen-reader user can find and operate.
  const reveal = page.getByRole('button', { name: 'Show' })
  await expect(reveal).toBeVisible()
  await expect(reveal).toHaveAttribute('aria-pressed', 'false')

  // Masked: the struck-through eye, and not the open one.
  await expect(reveal.locator('svg.lucide-eye-off-icon')).toBeVisible()
  await expect(reveal.locator('svg.lucide-eye-icon')).toHaveCount(0)

  await reveal.click()
  await expect(password).toHaveAttribute('type', 'text')

  // Revealed: the glyph flips, the name flips to the next action, and the
  // pressed state flips with them.
  const hide = page.getByRole('button', { name: 'Hide' })
  await expect(hide).toHaveAttribute('aria-pressed', 'true')
  await expect(hide.locator('svg.lucide-eye-icon')).toBeVisible()
  await expect(hide.locator('svg.lucide-eye-off-icon')).toHaveCount(0)

  await hide.click()
  await expect(password).toHaveAttribute('type', 'password')
})

test('the reveal toggle is reachable and operable from the keyboard alone', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/login')
  const password = page.getByRole('textbox', { name: 'Password' })
  await password.fill('correct horse battery staple')

  // Tab from the field lands on the toggle: an icon-only control that mouse
  // users can reach and keyboard users cannot is the failure this guards.
  await password.press('Tab')
  await expect(page.getByRole('button', { name: 'Show' })).toBeFocused()

  await page.keyboard.press('Enter')
  await expect(password).toHaveAttribute('type', 'text')
  await expect(page.getByRole('button', { name: 'Hide' })).toBeFocused()
})
