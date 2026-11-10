import { expect, test } from '../fixtures/guarded-test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { expectTablesAsCards, type CardActions } from '../fixtures/expect-table-cards'
import { DNS_SETTINGS, dnsDraft, dnsRecord, type DnsStubState } from '../fixtures/stub-dns-routes'
import { EXAMPLE_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'
import { CUSTOM_TEMPLATE, WEB_TEMPLATE, stubDnsTemplates, templateRecord } from '../fixtures/stub-dns-templates-routes'
import { SIGNED_DNSSEC, stubDnsDnssec } from '../fixtures/stub-dns-dnssec-routes'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { Page } from '@playwright/test'

// The phone-width contract (issue #82) for the DNS screens (issue #178): no screen scrolls
// sideways at 390px in any of the three languages. Armenian and Russian labels are long, and a
// zone name, a TXT value and a DS record are long and unbreakable by nature, so the rows below
// carry exactly those. The route census (`ui-kit/route-coverage.spec.ts`) reads the `path:`
// literals of this file, so a DNS route added without a row fails there.

/** The phone viewport the criterion names. */
const PHONE = { width: 390, height: 844 }

/** The interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

/** One screen to measure, and how to bring it up. */
interface Screen {
  /** Name used in the generated test title. */
  name: string
  /** The path to open. */
  path: string
  /** For a screen with tables: whether the tables are cards with an actions column. */
  cards?: CardActions
  /**
   * Brings the screen to the state being measured and waits until its content is on screen.
   * @param page The page, already navigated to {@link Screen.path}.
   * @returns Resolves once the content is visible.
   */
  ready: (page: Page) => Promise<void>
}

/** A zone name as long and unbreakable as a real customer's can be. */
const LONG_ZONE = 'a-rather-long-subdomain-for-a-customer.example-hosting-company-with-a-long-name.com'

/** A TXT value as long and unbreakable as a real DMARC policy can be. */
const LONG_VALUE = 'v=DMARC1;p=quarantine;rua=mailto:reports@a-rather-long-subdomain-for-a-customer.example.com'

/** What the panel holds for every row: a long zone, a long TXT record and long name servers. */
const SEED: Partial<DnsStubState> = {
  zones: [{ ...EXAMPLE_ZONE, name: LONG_ZONE }],
  records: {
    [EXAMPLE_ZONE.id]: [dnsRecord(EXAMPLE_ZONE.id, 'rec-txt', dnsDraft({ name: '_dmarc', type: 'txt', value: LONG_VALUE }))],
  },
  settings: { ...DNS_SETTINGS, primaryNameserver: `ns1.${LONG_ZONE}`, secondaryNameserver: `ns2.${LONG_ZONE}` },
}

/**
 * Opens a pane of the zone page by its position in the tab control, which is the same in every
 * language where the labels are not.
 * @param page The page, on the zone's detail screen.
 * @param position 0 for the overview, 1 for the records, 2 for the DNSSEC pane.
 * @returns Resolves once the pane is pressed.
 */
const openPane = async (page: Page, position: number): Promise<void> => {
  await page.getByRole('main').locator('[role="group"] > button').nth(position).click()
}

/** A template with a long name and a long, unbreakable value, the way an administrator can write one. */
const LONG_TEMPLATE = {
  ...CUSTOM_TEMPLATE,
  name: LONG_ZONE,
  description: LONG_VALUE,
  records: [templateRecord({ name: '_dmarc', type: 'txt', value: LONG_VALUE })],
}

const SCREENS: Screen[] = [
  {
    name: 'the zones list',
    path: '/dns/zones',
    cards: 'actions',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('link', { name: LONG_ZONE })).toBeVisible()
    },
  },
  {
    name: 'the new zone form',
    path: '/dns/zones/new',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('textbox').first()).toBeVisible()
    },
  },
  {
    name: 'the zone overview',
    path: '/dns/zones/11111111-1111-1111-1111-111111111111',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('heading', { level: 1, name: LONG_ZONE })).toBeVisible()
    },
  },
  {
    name: 'the zone records',
    path: '/dns/zones/11111111-1111-1111-1111-111111111111',
    cards: 'actions',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('heading', { level: 1, name: LONG_ZONE })).toBeVisible()
      await openPane(page, 1)
      await expect(page.getByText('v=DMARC1;p=quarantine', { exact: false })).toBeVisible()
    },
  },
  {
    name: 'the zone DNSSEC pane',
    path: '/dns/zones/11111111-1111-1111-1111-111111111111',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('heading', { level: 1, name: LONG_ZONE })).toBeVisible()
      await openPane(page, 2)
      await expect(page.getByTestId('ds-digest')).toBeVisible()
    },
  },
  {
    name: 'the zone templates list',
    path: '/dns/templates',
    cards: 'actions',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('link', { name: LONG_ZONE })).toBeVisible()
    },
  },
  {
    name: 'the new template form',
    path: '/dns/templates/new',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('textbox').first()).toBeVisible()
    },
  },
  {
    name: 'the template form with a long record',
    path: `/dns/templates/${CUSTOM_TEMPLATE.id}`,
    cards: 'actions',
    ready: async (page): Promise<void> => {
      await expect(page.getByText('v=DMARC1;p=quarantine', { exact: false }).first()).toBeVisible()
    },
  },
  {
    name: 'the nameservers page',
    path: '/dns/nameservers',
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('textbox').first()).toBeVisible()
    },
  },
]

for (const screen of SCREENS) {
  for (const locale of LOCALES) {
    test(`${screen.name} does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
      await page.setViewportSize(PHONE)
      await setPersistedLocale(page, locale)
      const dns = await openDnsScreen(page, SEED)
      await stubDnsTemplates(page, { templates: [WEB_TEMPLATE, LONG_TEMPLATE] })
      await stubDnsDnssec(page, dns, { dnssec: SIGNED_DNSSEC })

      await page.goto(screen.path)
      await screen.ready(page)
      await expectNoHorizontalOverflow(page)

      if (screen.cards !== undefined) {
        await expectTablesAsCards(page, screen.cards)
      }
    })
  }
}
