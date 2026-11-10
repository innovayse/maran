import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE } from '../fixtures/open-full-shell'
import {
  stubAccountMemberships,
  stubRefreshFollowsActiveAccount,
  type MembershipsState,
} from '../fixtures/stub-account-memberships-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubMembershipOffers, type MembershipOffersBehaviour } from '../fixtures/stub-membership-offers-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { MembershipOffer } from '../../src/types/membershipOffer'
import type { Page } from '@playwright/test'

// Issue #328: a person offered access to another account answers inside their own signed-in session.
// Reached by clicks only: the sidebar's My account section, then its My account page. Nothing here is
// reached by a link in a mail, and the offer carries no secret; the panel is the boundary, and these
// specs prove what the screen does with its answers.

const OWN_ACCOUNT: MyAccount = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

const SHOP_ACCOUNT = '22222222-2222-2222-2222-222222222222'

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

const SHOP_OFFER: MembershipOffer = {
  id: '33333333-3333-4333-8333-333333333333',
  accountName: 'shop',
  features: [
    { feature: 'databases', featureDisplayName: 'Databases' },
    { feature: 'backups', featureDisplayName: 'Backups' },
  ],
  expiresAt: '2026-11-17T10:00:00Z',
}

/** The page chrome in each language the specs below read it in. */
const CHROME = {
  en: { section: 'Show the My account pages', myAccount: 'My account' },
  ru: { section: 'Показать разделы «Мой аккаунт»', myAccount: 'Мой аккаунт' },
  hy: { section: 'Ցույց տալ «Իմ հաշիվը» բաժինները', myAccount: 'Իմ հաշիվը' },
} as const

/** The accounts the person holds before accepting anything: only their own, so no selector is drawn. */
const ownAccountOnly = (): MembershipsState => {
  return { accounts: [{ accountId: OWN_ACCOUNT.id, name: 'olive' }], activeAccountId: OWN_ACCOUNT.id, switchRequests: [] }
}

/** Signs the customer in with the offers stubbed, then opens My account from the sidebar, or from the bottom bar on a phone. */
const openMyAccount = async (
  page: Page,
  offers: MembershipOffer[],
  state: MembershipsState = ownAccountOnly(),
  behaviour: MembershipOffersBehaviour = {},
  language: keyof typeof CHROME = 'en',
): Promise<Awaited<ReturnType<typeof stubMembershipOffers>>> => {
  await setPersistedLocale(page, language)
  await stubSignedIn(page, CUSTOMER)
  await stubRefreshFollowsActiveAccount(page, CUSTOMER, state)
  await stubHealthy(page)
  // The shell's defaults first: the memberships below replace the empty list of further accounts.
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubLandingReads(page, 'customer')
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, { accountId: OWN_ACCOUNT.id, resources: [] })
  await stubAccountMemberships(page, state)
  const sent = await stubMembershipOffers(page, offers, behaviour)
  await page.goto('/')
  // Below 1024px the sidebar is a drawer and the bottom bar carries the account destination.
  if ((page.viewportSize()?.width ?? 1440) < 1024) {
    await page.getByRole('navigation', { name: 'Sections' }).getByRole('link', { name: 'Account', exact: true }).click()
    return sent
  }
  const toggle = page.getByRole('navigation').getByRole('button', { name: CHROME[language].section }).first()
  if ((await toggle.getAttribute('aria-expanded')) !== 'true') {
    await toggle.click()
  }
  await page.getByRole('navigation').getByRole('link', { name: CHROME[language].myAccount, exact: true }).first().click()
  return sent
}

test('an offer is shown with the account, the features offered and the day it lapses', async ({ page }) => {
  await openMyAccount(page, [{ ...SHOP_OFFER }])

  await expect(page.getByRole('heading', { name: 'Invitations to join an account' })).toBeVisible()
  const card = page.getByRole('listitem').filter({ hasText: 'Features you would get' })
  await expect(card).toContainText('shop')
  await expect(card).toContainText('Features you would get: Databases, Backups')
  await expect(card).toContainText('Open until 17 Nov 2026')
})

test('no card is drawn when nobody offered anything', async ({ page }) => {
  await openMyAccount(page, [])

  await expect(page.getByRole('heading', { name: 'olive' })).toBeVisible()
  await expect(page.getByText('Invitations to join an account')).toHaveCount(0)
})

test('accepting an offer removes it and the account appears in the account selector', async ({ page }) => {
  const state = ownAccountOnly()
  const sent = await openMyAccount(page, [{ ...SHOP_OFFER }], state, {
    onAccepted: (offer) => {
      state.accounts.push({ accountId: SHOP_ACCOUNT, name: offer.accountName })
    },
  })
  await expect(page.getByTestId('account-selector')).toHaveCount(0)

  await page.getByRole('button', { name: 'Accept' }).click()

  await expect(page.getByText('You joined shop.')).toBeVisible()
  await expect(page.getByText('Invitations to join an account')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Current account: olive\. Switch account/ })).toBeVisible()
  expect(sent.accepted).toEqual([SHOP_OFFER.id])
  expect(sent.declined).toEqual([])
})

test('declining asks for confirmation and sends nothing until it is given', async ({ page }) => {
  const sent = await openMyAccount(page, [{ ...SHOP_OFFER }])

  await page.getByRole('button', { name: 'Decline' }).click()
  const dialog = page.getByRole('dialog', { name: 'Decline the offer from shop' })
  await expect(dialog).toBeVisible()
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  await expect(dialog).toHaveCount(0)
  await expect(page.getByText('Invitations to join an account')).toBeVisible()
  expect(sent.declined).toEqual([])

  await page.getByRole('button', { name: 'Decline' }).click()
  await page.getByRole('dialog', { name: 'Decline the offer from shop' }).getByRole('button', { name: 'Decline offer' }).click()

  await expect(page.getByText('The offer from shop was declined.')).toBeVisible()
  await expect(page.getByText('Invitations to join an account')).toHaveCount(0)
  expect(sent.declined).toEqual([SHOP_OFFER.id])
  expect(sent.accepted).toEqual([])
})

test('a refused answer is shown verbatim and the offer stays until the panel says it is gone', async ({
  page,
  guard,
}) => {
  guard.allow(409, /\/api\/v1\/me\/membership-offers\/[^/]+\/accept$/)
  await openMyAccount(page, [{ ...SHOP_OFFER }], ownAccountOnly(), {
    refuseWith: { status: 409, detail: 'That account is suspended, so the offer cannot be accepted now.' },
  })

  await page.getByRole('button', { name: 'Accept' }).click()

  await expect(page.getByText('That account is suspended, so the offer cannot be accepted now.')).toBeVisible()
  await expect(page.getByText('You joined shop.')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Accept' })).toBeVisible()
})

test('the offer card fits a 390px screen without scrolling sideways', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await openMyAccount(page, [{ ...SHOP_OFFER, accountName: 'a-very-long-account-name-for-a-small-screen' }])

  await expect(page.getByRole('button', { name: 'Accept' })).toBeVisible()
  const overflow = await page.evaluate(() => {
    return document.documentElement.scrollWidth - document.documentElement.clientWidth
  })
  expect(overflow).toBeLessThanOrEqual(0)
})

test('the offer is drawn in Russian', async ({ page }) => {
  await openMyAccount(page, [{ ...SHOP_OFFER }], ownAccountOnly(), {}, 'ru')

  await expect(page.getByText('Приглашения в аккаунты')).toBeVisible()
  await expect(page.getByText('Что вы получите: Databases, Backups')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Принять' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Отклонить' })).toBeVisible()
})

test('the offer is drawn in Armenian', async ({ page }) => {
  await openMyAccount(page, [{ ...SHOP_OFFER }], ownAccountOnly(), {}, 'hy')

  await expect(page.getByText('Հրավերներ՝ միանալու հաշվին')).toBeVisible()
  await expect(page.getByText('Ինչ կստանաք՝ Databases, Backups')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Ընդունել' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Մերժել' })).toBeVisible()
})
