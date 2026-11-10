import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { expect, test } from '../fixtures/guarded-test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubMailAliases, stubMailboxes, stubMailChoices, stubMailDomains } from '../fixtures/stub-mail-routes'
import { MAIL_ON, stubMailServer } from '../fixtures/stub-mail-server-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #177 and rules/vue.md "Accessibility": axe scans each new top-level list with a real row
// in it, in both themes, and finds no serious or critical WCAG 2.2 AA violation. Kept in its own
// file so it joins `axe-scan.spec.ts` without editing it; it uses the same helper.

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
 * Signs an administrator in and serves one row on every mail list.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const signInWithData = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMailServer(page, MAIL_ON)
  await stubMailChoices(page)
  await stubMailDomains(page, [
    {
      id: DOMAIN_ID,
      accountId: ALICE.id,
      ownerName: ALICE.name,
      name: 'alpha.example.com',
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
      address: 'info@alpha.example.com',
      quotaMib: 1024,
      usedMib: 900,
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
      address: 'team@alpha.example.com',
      kind: 'alias',
      kindName: 'Alias',
      targets: ['info@alpha.example.com'],
      createdAt: '2026-08-01T10:00:00Z',
    },
  ])
}

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the mail domains list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/mail/domains')
      await expect(page.getByRole('cell', { name: 'alpha.example.com', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the mailboxes list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/mail/mailboxes')
      await expect(page.getByRole('cell', { name: 'info@alpha.example.com', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the aliases list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/mail/aliases')
      await expect(page.getByRole('cell', { name: 'team@alpha.example.com', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })
  })
}
