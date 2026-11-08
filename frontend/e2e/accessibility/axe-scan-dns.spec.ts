import { test, expect } from '@playwright/test'
import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { dnsDraft, dnsRecord } from '../fixtures/stub-dns-routes'
import { EXAMPLE_ZONE, SHOP_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'

// Issue #149 for the DNS screens (issue #178): axe runs on the zones list, a zone with its
// records and the nameservers page, with real rows in them,
// in both themes, and finds no serious or critical violation. A contrast failure lives in one
// theme's tokens and an empty list draws no table at all, so every screen is scanned with data.

const RECORDS = [
  dnsRecord(EXAMPLE_ZONE.id, 'rec-www', dnsDraft({ name: 'www', type: 'a', value: '203.0.113.10' })),
  dnsRecord(EXAMPLE_ZONE.id, 'rec-ns', dnsDraft({ name: '@', type: 'ns', value: 'ns1.panel.example', ttl: 86400 }), 'system'),
]

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the zones list has no serious accessibility violation', async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 })
      await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })

      await page.goto('/dns/zones')
      await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the records of a zone have no serious accessibility violation', async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 })
      await openDnsScreen(page, { zones: [EXAMPLE_ZONE], records: { [EXAMPLE_ZONE.id]: RECORDS } })

      await page.goto(`/dns/zones/${EXAMPLE_ZONE.id}`)
      await page.getByRole('button', { name: 'Records', exact: true }).click()
      await expect(page.getByRole('row').filter({ hasText: '203.0.113.10' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the record dialog has no serious accessibility violation', async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 })
      await openDnsScreen(page, { zones: [EXAMPLE_ZONE], records: { [EXAMPLE_ZONE.id]: RECORDS } })

      await page.goto(`/dns/zones/${EXAMPLE_ZONE.id}`)
      await page.getByRole('button', { name: 'Records', exact: true }).click()
      await page.getByRole('button', { name: 'Add record' }).click()
      await expect(page.getByRole('dialog', { name: 'Add a record' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the nameservers page has no serious accessibility violation', async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 })
      await openDnsScreen(page)

      await page.goto('/dns/nameservers')
      await expect(page.getByRole('button', { name: 'Save settings' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })
  })
}
