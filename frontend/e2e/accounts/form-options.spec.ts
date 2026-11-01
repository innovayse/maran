import { expect, test, type Page } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import type { PanelModule } from '../../src/types/module'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPlans, THE_PLAN } from '../fixtures/stub-plans-route'
import { setPersistedLocale } from '../fixtures/set-locale'

// Issue #194: the create form suggests the username from the domain, refuses a "www." domain the
// way the server does, asks for the owner's language and says what creation makes on the server.

/** Catalogue every spec here starts from: the accounts module licensed, so the form is reachable. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

/**
 * Signs in as an administrator and opens the create form.
 * @param page The Playwright page.
 * @returns Resolves once the form is on screen.
 */
const openForm = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)
  await page.goto('/accounts/new')
  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()
}

test('the username follows the domain until the operator types one of their own', async ({ page }) => {
  await openForm(page)
  const username = page.getByLabel('Username')

  await page.getByLabel('Primary domain').fill('www.my-shop.example.com')
  await expect(username).toHaveValue('myshop')

  await username.fill('shopowner')
  await page.getByLabel('Primary domain').fill('other.example.com')
  await expect(username).toHaveValue('shopowner')
})

test('a domain typed with www is refused on the form, and nothing is sent', async ({ page }) => {
  await openForm(page)
  const createRequests: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/accounts')) {
      createRequests.push(request.url())
    }
  })

  await page.getByLabel('Primary domain').fill('www.alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: THE_PLAN.displayName, exact: false }).click()
  await page.getByLabel('Owner email').fill('owner@alpha.example.com')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByText('Enter the primary domain without "www.".')).toBeVisible()
  await expect(page.getByLabel('Primary domain')).toHaveAttribute('aria-invalid', 'true')
  expect(createRequests).toEqual([])
})

test('the chosen owner language is posted with the account', async ({ page }) => {
  await openForm(page)
  await page.route('**/api/v1/accounts', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Stop here.' }),
    })
  })

  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: THE_PLAN.displayName, exact: false }).click()
  await page.getByLabel('Owner email').fill('owner@alpha.example.com')
  const language = page.getByRole('combobox', { name: 'Owner language' })
  await expect(language).toHaveText('English')
  await language.click()
  await page.getByRole('option', { name: 'Հայերեն' }).click()

  const [request] = await Promise.all([
    page.waitForRequest((candidate) => {
      return candidate.method() === 'POST' && candidate.url().includes('/api/v1/accounts')
    }),
    page.getByRole('button', { name: 'Create account' }).click(),
  ])

  expect(request.postDataJSON()).toEqual({
    name: 'alpha',
    primaryDomain: 'alpha.example.com',
    planId: THE_PLAN.id,
    ownerEmail: 'owner@alpha.example.com',
    ownerLanguage: 'hy',
  })
})

test('the owner language starts as the operator own interface language', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)

  await page.goto('/accounts/new')

  await expect(page.getByRole('combobox', { name: 'Язык владельца' })).toHaveText('Русский')
})

test('the panel beside the form says what creating the account makes on the server', async ({ page }) => {
  await openForm(page)

  await expect(page.getByRole('heading', { name: 'What will be created' })).toBeVisible()
  await expect(page.getByText('A system user named after the username', { exact: false })).toBeVisible()
  await expect(page.getByText('an invitation mailed to the owner email', { exact: false })).toBeVisible()
})
