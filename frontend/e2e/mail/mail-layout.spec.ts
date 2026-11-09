import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubMailAliases, stubMailboxes, stubMailChoices, stubMailDomains } from '../fixtures/stub-mail-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

// Issue #177 and rules/vue.md: no screen scrolls horizontally at 390px in any of the three
// languages. The Armenian labels are the long ones and the usual cause.

const LICENSED: PanelModule[] = [{ name: 'mail', displayName: 'Mail', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

const DOMAIN_ID = '11111111-1111-1111-1111-111111111111'

/**
 * Signs in and serves one row on every mail list.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const openWithRows = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMailChoices(page)
  await stubMailDomains(page, [
    {
      id: DOMAIN_ID,
      accountId: ALICE.id,
      ownerName: ALICE.name,
      name: 'a-rather-long-domain-name-for-a-narrow-phone.example.com',
      status: 'active',
      statusName: 'Active',
      dkimSelector: 'maran1',
      mailboxCount: 1,
      createdAt: '2026-08-01T10:00:00Z',
    },
  ])
  await stubMailboxes(page, [
    {
      id: 'aaaaaaaa-0000-0000-0000-000000000001',
      accountId: ALICE.id,
      ownerName: ALICE.name,
      mailDomainId: DOMAIN_ID,
      address: 'a-long-local-part@a-rather-long-domain-name-for-a-narrow-phone.example.com',
      quotaMib: 1024,
      usedMib: 512,
      sendLimitPerHour: 100,
      status: 'active',
      statusName: 'Active',
      enabled: true,
      createdAt: '2026-08-01T10:00:00Z',
    },
  ])
  await stubMailAliases(page, [
    {
      id: 'bbbbbbbb-0000-0000-0000-000000000001',
      accountId: ALICE.id,
      ownerName: ALICE.name,
      mailDomainId: DOMAIN_ID,
      address: 'team@a-rather-long-domain-name-for-a-narrow-phone.example.com',
      kind: 'forwarder',
      kindName: 'Forwarder',
      targets: ['someone-with-a-long-name@another-rather-long-domain-name.example.org'],
      createdAt: '2026-08-01T10:00:00Z',
    },
  ])
}

for (const locale of ['en', 'ru', 'hy'] as const) {
  for (const { path } of [{ path: '/mail/domains' }, { path: '/mail/mailboxes' }, { path: '/mail/aliases' }]) {
    test(`${path} does not scroll the page sideways at 390px in ${locale}`, async ({ page }) => {
      await setPersistedLocale(page, locale)
      await openWithRows(page)

      await page.goto(path)
      // Positive control: the list is drawn before the page width is measured.
      await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toBeVisible()
      await expect(page.getByText('a-rather-long-domain-name-for-a-narrow-phone.example.com').first()).toBeVisible()

      const extent = await page.evaluate(() => {
        const root = window.document.documentElement
        return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth }
      })
      expect(extent.scrollWidth).toEqual(extent.clientWidth)
    })
  }
}
