import { expect, test, type Page } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { setPersistedLocale } from '../fixtures/set-locale'
import { signInAs, type DemoRole } from './sign-in-as'

// Every top-level screen of both areas, against the seeded demo (issue #95): each one opens, and
// each list holds at least one row, because a screen that opens empty on a demo is a seed that
// forgot it. Run only through playwright.demo.config.ts against a running demo.

/** One screen to open. */
interface DemoScreen {
  /** What a failure calls it. */
  name: string
  /** Where it lives. */
  path: string
  /** Whether the seeded demo must show at least one data row in a table on it. */
  populated: boolean
}

/** The administrator's top-level screens. */
const ADMINISTRATOR_SCREENS: readonly DemoScreen[] = [
  { name: 'overview', path: '/', populated: false },
  { name: 'accounts', path: '/accounts', populated: true },
  { name: 'plans', path: '/plans', populated: true },
  { name: 'resellers', path: '/resellers', populated: true },
  { name: 'sites', path: '/sites', populated: true },
  { name: 'databases', path: '/databases', populated: true },
  { name: 'backups', path: '/backups', populated: true },
  { name: 'SFTP logins', path: '/file-transfer/sftp', populated: true },
  { name: 'FTPS logins', path: '/file-transfer/ftps', populated: true },
  { name: 'firewall', path: '/firewall', populated: true },
  { name: 'firewall bans', path: '/firewall/bans', populated: true },
  { name: 'cron', path: '/cron', populated: true },
  { name: 'tasks', path: '/tasks', populated: true },
  { name: 'monitoring', path: '/monitoring', populated: false },
  { name: 'pending updates', path: '/servers/updates', populated: false },
  { name: 'sessions', path: '/settings/sessions', populated: true },
  { name: 'audit journal', path: '/settings/audit', populated: true },
  { name: 'security policy', path: '/settings/security-policy', populated: false },
  { name: 'mail settings', path: '/settings/smtp', populated: false },
  { name: 'PHP versions', path: '/settings/php', populated: false },
]

/** The customer's top-level screens. */
const CUSTOMER_SCREENS: readonly DemoScreen[] = [
  { name: 'overview', path: '/', populated: false },
  { name: 'sites', path: '/sites', populated: true },
  { name: 'databases', path: '/databases', populated: true },
  { name: 'backups', path: '/backups', populated: true },
  { name: 'SFTP logins', path: '/file-transfer/sftp', populated: true },
  { name: 'cron', path: '/cron', populated: true },
  { name: 'tasks', path: '/tasks', populated: true },
  { name: 'my account', path: '/my-account', populated: false },
  { name: 'sessions', path: '/settings/sessions', populated: true },
]

/**
 * Opens one screen and asserts it drew a page heading and, when the seed must populate it, a row.
 * @param page A signed-in page.
 * @param screen The screen to open.
 * @returns Resolves once the assertions held.
 */
const expectScreen = async (page: Page, screen: DemoScreen): Promise<void> => {
  await page.goto(screen.path)
  const main = page.getByRole('main')
  await expect(main.getByRole('heading', { level: 1 }), `${screen.name} drew no page heading`).toBeVisible()
  if (screen.populated) {
    // Row 0 is the header row of the table, so a data row is the second.
    await expect(
      main.getByRole('row').nth(1),
      `${screen.name} opened with no data row: the demo seed is missing it`,
    ).toBeVisible()
  }
}

/**
 * Registers one test per screen of one area.
 * @param role The login the area is opened with.
 * @param screens The screens to open.
 * @returns Nothing; the tests are registered with Playwright.
 */
const coverArea = (role: DemoRole, screens: readonly DemoScreen[]): void => {
  test.describe(`the ${role}'s screens`, () => {
    test.beforeEach(async ({ page }) => {
      await signInAs(page, role)
    })

    for (const screen of screens) {
      test(`${screen.name} opens with the seeded data`, async ({ page }) => {
        await expectScreen(page, screen)
      })
    }

    test('every screen shows the demo banner with the next reset', async ({ page }) => {
      await expect(page.getByTestId('demo-banner')).toBeVisible()
      await expect(page.getByTestId('demo-next-reset')).toContainText(/\d{4}/)
    })
  })
}

coverArea('administrator', ADMINISTRATOR_SCREENS)
coverArea('customer', CUSTOMER_SCREENS)

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the signed-in overview with the banner does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await setPersistedLocale(page, locale)
    await signInAs(page, 'administrator')
    await expect(page.getByTestId('demo-banner')).toBeVisible()

    await expectNoHorizontalOverflow(page)
  })
}
