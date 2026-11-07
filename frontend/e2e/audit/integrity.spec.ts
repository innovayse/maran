import { expect, test, type Page } from '@playwright/test'
import {
  AUDIT_VERIFICATION_PATTERN,
  DEFAULT_CHAIN,
  INTACT_VERDICT,
  stubAudit,
  stubAuditChain,
} from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { AuditChainVerification } from '../../src/types/audit'

// The journal's tamper evidence (#145): the chain's counts, and a verification whose verdict is the
// panel's. Both outcomes are driven, so a card that always said "intact" would fail here.

/** A chain whose seventh entry no longer matches. */
const BROKEN_VERDICT: AuditChainVerification = {
  intact: false,
  rowsChecked: 7,
  firstBrokenChainIndex: 7,
  break: 'RowHashMismatch',
  verifiedAt: '2026-09-10T09:00:00+00:00',
}

/**
 * Signs an administrator in over an empty journal.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const openJournal = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubAudit(page, [])
}

test('the card shows how many entries are sealed, waiting, and purged', async ({ page }) => {
  await openJournal(page)

  await page.goto('/settings/audit')

  await expect(page.getByText('Sealed entries')).toBeVisible()
  await expect(page.getByText(String(DEFAULT_CHAIN.sealedRows), { exact: true })).toBeVisible()
  await expect(page.getByText('Not sealed yet')).toBeVisible()
  await expect(page.getByText(String(DEFAULT_CHAIN.unsealedRows), { exact: true })).toBeVisible()
  await expect(page.getByText('Purged through entry')).toBeVisible()
  // Nothing has been verified yet, so no verdict is claimed.
  await expect(page.getByText('The chain is intact')).toHaveCount(0)
  await expect(page.getByText('The chain is broken')).toHaveCount(0)
})

test('verifying an intact chain says so', async ({ page }) => {
  await openJournal(page)
  const verifications = await stubAuditChain(page, DEFAULT_CHAIN, INTACT_VERDICT)

  await page.goto('/settings/audit')
  await page.getByRole('button', { name: 'Verify the chain' }).click()

  await expect(page.getByText('The chain is intact')).toBeVisible()
  await expect(page.getByText('The chain is broken')).toHaveCount(0)
  expect(verifications()).toBe(1)
})

test('verifying a broken chain names where it broke and what did not match', async ({ page }) => {
  await openJournal(page)
  await stubAuditChain(page, DEFAULT_CHAIN, BROKEN_VERDICT)

  await page.goto('/settings/audit')
  await page.getByRole('button', { name: 'Verify the chain' }).click()

  await expect(page.getByText('The chain is broken')).toBeVisible()
  await expect(page.getByText('RowHashMismatch #7')).toBeVisible()
  await expect(page.getByText('The chain is intact')).toHaveCount(0)
})

test('a verification the panel refuses shows its sentence and claims no verdict', async ({ page }) => {
  await openJournal(page)
  await page.route(AUDIT_VERIFICATION_PATTERN, async (route) => {
    await route.fulfill({
      status: 429,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'RateLimited', title: 'Too many', detail: 'Too many requests. Try again later.' }),
    })
  })

  await page.goto('/settings/audit')
  await page.getByRole('button', { name: 'Verify the chain' }).click()

  await expect(page.getByText('Too many requests. Try again later.')).toBeVisible()
  await expect(page.getByText('The chain is intact')).toHaveCount(0)
  await expect(page.getByText('The chain is broken')).toHaveCount(0)
})
