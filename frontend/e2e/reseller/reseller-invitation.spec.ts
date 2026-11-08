import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation, type InvitationStub } from '../fixtures/stub-invitation-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { ACME_HOSTING, BRAVO_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { InvitationState } from '../../src/types/invitation'
import type { ResellerDetail } from '../../src/types/reseller'

// Issue #374: an administrator can resend or reissue a reseller's invitation from the reseller's
// own page. The state is read from the panel every time, a link is shown once and replaced on the
// next ask, an accepted invitation offers nothing, and a suspended reseller is offered nothing.

/**
 * Signs the administrator in and opens one reseller's detail page, with its invitation in the given state.
 * @param page The Playwright page under test.
 * @param reseller The reseller whose detail page is opened.
 * @param state The state the reseller's invitation starts in.
 * @returns Handles for the invitation stub's call counts.
 */
const openReseller = async (page: Page, reseller: ResellerDetail, state: InvitationState): Promise<InvitationStub> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubPlans(page)
  await stubResellers(page, [{ ...reseller }])
  // Installed after the resellers fixture so its reseller-specific routes win over the generic "accepted" one.
  const invitation = await stubInvitation(page, reseller.id, state, 'reseller')
  await page.goto(`/resellers/${reseller.id}`)
  await expect(page.getByTestId('reseller-invitation-card')).toBeVisible()
  return invitation
}

test('a pending invitation says when it lapses and offers a link and a resend', async ({ page }) => {
  await openReseller(page, ACME_HOSTING, 'pending')

  await expect(page.getByTestId('reseller-invitation-state')).toContainText('Waiting for the reseller to set a password')
  await expect(page.getByRole('button', { name: 'Create invitation link' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Resend invitation' })).toBeVisible()
})

test('creating a link shows it once, and creating another replaces it', async ({ page }) => {
  const invitation = await openReseller(page, ACME_HOSTING, 'expired')

  await page.getByRole('button', { name: 'Create invitation link' }).click()
  await expect(page.getByTestId('reseller-invitation-link')).toContainText('/accept-invitation?token=token-1')

  await page.getByRole('button', { name: 'Create a new link' }).click()
  await expect(page.getByTestId('reseller-invitation-link')).toContainText('token=token-2')
  expect(invitation.linkRequests()).toBe(2)
})

test('resending mails a new invitation, drops the link on screen and says it was sent, not delivered', async ({ page }) => {
  const invitation = await openReseller(page, ACME_HOSTING, 'pending')
  await page.getByRole('button', { name: 'Create invitation link' }).click()
  await expect(page.getByTestId('reseller-invitation-link')).toBeVisible()

  await page.getByRole('button', { name: 'Resend invitation' }).click()

  await expect(page.getByText('A new invitation was sent. Delivery is not confirmed')).toBeVisible()
  await expect(page.getByTestId('reseller-invitation-link')).toHaveCount(0)
  expect(invitation.resendRequests()).toBe(1)
})

test('an accepted invitation offers nothing to hand over', async ({ page }) => {
  await openReseller(page, ACME_HOSTING, 'accepted')

  await expect(page.getByTestId('reseller-invitation-state')).toContainText('The reseller has set a password')
  await expect(page.getByRole('button', { name: 'Create invitation link' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resend invitation' })).toHaveCount(0)
})

test('a suspended reseller is told to be reactivated first and offered no buttons', async ({ page }) => {
  await openReseller(page, BRAVO_HOSTING, 'pending')

  await expect(page.getByTestId('reseller-invitation-suspended')).toContainText('Reactivate the reseller first')
  await expect(page.getByRole('button', { name: 'Create invitation link' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Resend invitation' })).toHaveCount(0)
})
