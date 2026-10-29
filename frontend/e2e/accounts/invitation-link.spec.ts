import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubInvitation, type InvitationStub } from '../fixtures/stub-invitation-routes'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { Account } from '../../src/types/account'
import type { InvitationState } from '../../src/types/invitation'

// #78: an account created on an install with no mail server could never be signed into, because
// the invitation's plaintext token existed only inside a mail that was never sent. The panel now
// lets an administrator create a one-time link and hand it over. These specs stub every route; the
// backend's half (the token is re-minted, never stored) is covered by its own tests.

/** The account under test: the one `stubAccounts` creates, so the form story lands on it. */
const ACCOUNT: Account = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '22222222-2222-2222-2222-222222222222',
  status: 'active',
  createdAt: '2026-08-29T10:00:00Z',
}

/**
 * Installs everything the account's detail page reads and returns the invitation handles.
 * @param page The Playwright page whose network the routes are installed on.
 * @param state The state the account's invitation starts in.
 * @returns The invitation stub, for counting calls and moving the state.
 */
const openDetailWith = async (page: Page, state: InvitationState): Promise<InvitationStub> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
  const invitation = await stubInvitation(page, ACCOUNT.id, state)
  await page.goto(`/accounts/${ACCOUNT.id}`)
  return invitation
}

test('creating an account opens its page, where the invitation link is created, shown and copied', async ({
  page,
  context,
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubAccounts(page, [])
  await stubPlans(page)
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
  // The AccountCreated handler has already issued a token whose plaintext went to an unsent mail.
  const invitation = await stubInvitation(page, ACCOUNT.id, 'pending')

  await page.goto('/accounts/new')
  await page.getByLabel('Name').fill('alpha')
  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option').first().click()
  await page.getByLabel('Owner email').fill('owner@alpha.example.com')
  await page.getByRole('button', { name: 'Create account' }).click()

  const card = page.getByTestId('invitation-card')
  await expect(card).toBeVisible()
  // The link is a secret shown on request: opening the page must not have minted one, because
  // minting retires the token already mailed on an install that can send mail.
  await expect(page.getByTestId('invitation-link')).toHaveCount(0)
  expect(invitation.linkRequests()).toBe(0)

  await card.getByRole('button', { name: 'Create invitation link' }).click()

  // Made absolute against the origin the operator is looking at.
  const shown = page.getByTestId('invitation-link')
  await expect(shown).toHaveText(`${new URL(page.url()).origin}/accept-invitation?token=token-1`)
  expect(invitation.linkRequests()).toBe(1)

  await card.getByRole('button', { name: 'Copy link' }).click()
  await expect(card.getByRole('button', { name: 'Copied' })).toBeVisible()
  expect(await page.evaluate(() => {return navigator.clipboard.readText()})).toBe(
    `${new URL(page.url()).origin}/accept-invitation?token=token-1`,
  )
})

test('the link is not put on the clipboard, in storage or in the address until the operator asks', async ({
  page,
  context,
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await openDetailWith(page, 'pending')
  await page.evaluate(() => {return navigator.clipboard.writeText('untouched')})

  const card = page.getByTestId('invitation-card')
  await card.getByRole('button', { name: 'Create invitation link' }).click()
  await expect(page.getByTestId('invitation-link')).toBeVisible()

  // Showing is not copying: only the click on the copy button writes the clipboard.
  expect(await page.evaluate(() => {return navigator.clipboard.readText()})).toBe('untouched')
  expect(page.url()).not.toContain('token')
  const stored = await page.evaluate(() => {
    return JSON.stringify([Object.entries(localStorage), Object.entries(sessionStorage)])
  })
  expect(stored).not.toContain('token-1')
})

test('on a pending invitation the page offers a link, and a new one replaces the old', async ({ page }) => {
  const invitation = await openDetailWith(page, 'pending')

  const card = page.getByTestId('invitation-card')
  await expect(page.getByTestId('invitation-state')).toContainText('Waiting for the owner to set a password.')
  // The consequence is stated before the button is pressed, not discovered after.
  await expect(card).toContainText('Creating a link replaces any invitation already sent')

  await card.getByRole('button', { name: 'Create invitation link' }).click()
  await expect(page.getByTestId('invitation-link')).toContainText('token=token-1')

  await card.getByRole('button', { name: 'Create a new link' }).click()
  await expect(page.getByTestId('invitation-link')).toContainText('token=token-2')
  // One link on screen, never two: the old one is dead and showing it would be a lie.
  await expect(page.getByTestId('invitation-link')).toHaveCount(1)
  await expect(card).not.toContainText('token-1')
  expect(invitation.linkRequests()).toBe(2)
})

test('an expired invitation says so and offers a new link', async ({ page }) => {
  await openDetailWith(page, 'expired')

  await expect(page.getByTestId('invitation-state')).toContainText('The invitation expired')
  await expect(page.getByRole('button', { name: 'Create invitation link' })).toBeVisible()
})

test('nothing is offered once the invitation has been used', async ({ page }) => {
  const invitation = await openDetailWith(page, 'accepted')

  await expect(page.getByTestId('invitation-state')).toContainText('The owner has set a password')
  // A link for a live login would be a way to reset a customer's access.
  await expect(page.getByRole('button', { name: 'Create invitation link' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Create a new link' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resend invitation' })).toHaveCount(0)
  await expect(page.getByTestId('invitation-link')).toHaveCount(0)
  expect(invitation.linkRequests()).toBe(0)
})

test('the link is forgotten when the operator leaves the page', async ({ page }) => {
  await stubAccounts(page, [])
  await openDetailWith(page, 'pending')

  await page.getByTestId('invitation-card').getByRole('button', { name: 'Create invitation link' }).click()
  await expect(page.getByTestId('invitation-link')).toBeVisible()

  await page.getByRole('link', { name: 'Back to accounts' }).click()
  await expect(page).toHaveURL('/accounts')
  await page.goBack()

  await expect(page.getByTestId('invitation-card')).toBeVisible()
  await expect(page.getByTestId('invitation-link')).toHaveCount(0)
})

test('resending the e-mail replaces the token, so a link on screen is dropped', async ({ page }) => {
  const invitation = await openDetailWith(page, 'pending')
  const card = page.getByTestId('invitation-card')

  await card.getByRole('button', { name: 'Create invitation link' }).click()
  await expect(page.getByTestId('invitation-link')).toBeVisible()

  await card.getByRole('button', { name: 'Resend invitation' }).click()

  await expect(card).toContainText('A new invitation has been sent.')
  await expect(page.getByTestId('invitation-link')).toHaveCount(0)
  expect(invitation.resendRequests()).toBe(1)
})

test('the card is available in Russian and Armenian too', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await openDetailWith(page, 'pending')
  await expect(page.getByRole('button', { name: 'Создать ссылку-приглашение' })).toBeVisible()

  await setPersistedLocale(page, 'hy')
  await page.reload()
  await expect(page.getByRole('button', { name: 'Ստեղծել հրավերի հղում' })).toBeVisible()
})
