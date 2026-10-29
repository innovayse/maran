import { expect, test, type Locator, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubLogin, stubSetupState, stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules, stubModules } from '../fixtures/stub-modules-route'
import { stubSteeredAdministrator } from '../fixtures/stub-password-reset-routes'
import { stubSmtpSettings, storedSmtpSettings } from '../fixtures/stub-smtp-routes'
import type { AuthenticatedUser, Session } from '../../src/types/auth'
import type { AuditEvent } from '../../src/types/audit'
import type { MyAccount } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { PhpVersion } from '../../src/types/phpVersion'
import type { SecurityPolicy } from '../../src/types/securityPolicy'

/**
 * Protects the redesign's phone-width contract (issue #82, acceptance criterion "no horizontal
 * scroll at 390 px on any screen, in all three languages") for every screen that is NOT on the
 * administrator's operations list: the unauthenticated flow (sign-in, its second-factor step,
 * first-run setup, forgot/reset password, accepting an invitation), the settings screens (audit
 * journal, sessions, security policy, PHP versions, mail settings, two-factor), a customer's own
 * account, the licence upgrade page and the not-found page.
 *
 * Why it exists: Russian and Armenian strings are markedly longer than English ones, the data
 * these screens print (user agents, mail hosts, recovery secrets, action names) is long and
 * unbreakable by nature, and the shell clips overflow with `overflow-x: hidden` on its main
 * region. That clipping makes `document.documentElement.scrollWidth` a blind probe on its own — a
 * page that overflows is silently cut off instead of scrolling, and the person on the phone simply
 * cannot reach the clipped part. So each screen is measured three ways, in each of the three
 * languages, at 390 x 844:
 *
 * 1. the document does not scroll sideways (`scrollWidth <= innerWidth`);
 * 2. the main region does not hold content wider than itself (`scrollWidth <= clientWidth`, which
 *    still reports hidden overflow, unlike a scrollbar);
 * 3. no element inside the main region has a box poking out of the viewport, unless an ancestor
 *    below the main region deliberately clips or scrolls it (a table scrolling inside its own
 *    container is allowed and is exactly what that ancestor test lets through).
 *
 * The bare auth layout additionally has its language switcher and its card asserted to lie fully
 * inside the viewport: a visitor who cannot read the sign-in screen must be able to reach the
 * switcher, and the card is what holds every form.
 *
 * Everything is stubbed from `e2e/fixtures/` or inline; no backend is reached. One test per
 * screen and language, generated from the table below, so a failure names the pair.
 */

/** The phone viewport the criterion names. */
const PHONE = { width: 390, height: 844 }

/** The interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

/** One of the shipped interface languages. */
type Locale = (typeof LOCALES)[number]

/**
 * The accessible name of the auth layout's language switcher per language
 * (`app.locale.switcherLabel` in `src/locales/<code>/shell.json`). Duplicated here for the reason
 * `set-locale.ts` duplicates its storage key: a black-box spec reads the built app, not the
 * source tree, and a role query needs the literal name.
 */
const SWITCHER_LABEL: Record<Locale, string> = {
  en: 'Interface language',
  ru: 'Язык интерфейса',
  hy: 'Ինտերֆեյսի լեզուն',
}

/** Which shell a screen is drawn in, which decides the extra assertions it gets. */
type Layout = 'auth' | 'shell'

/** One screen to measure, and how to bring it up with stubbed data. */
interface Screen {
  /** Name used in the generated test title. */
  name: string
  /** The path (with query) to open. */
  path: string
  /** The shell the screen renders in. */
  layout: Layout
  /**
   * Installs every stub the screen needs, before navigation. The locale is already seeded.
   * @param page The page whose network is stubbed.
   * @returns Resolves once the stubs are installed.
   */
  prepare: (page: Page) => Promise<void>
  /**
   * Brings the screen to the state being measured and waits until its content is on screen.
   * @param page The page, already navigated to {@link Screen.path}.
   * @returns Resolves once the content is visible.
   */
  ready: (page: Page) => Promise<void>
}

/** A customer, who owns an account and sees none of the administrator's screens. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** A refusal long enough to wrap on a phone in every language, with one unbreakable run in it. */
const LONG_REFUSAL =
  'The panel could not complete this request because the upstream service answered with an unexpected response: ' +
  'ERR_UPSTREAM_REFUSED_0123456789abcdef0123456789abcdef0123456789abcdef'

/** Journal rows whose cells are long and unbreakable, as real addresses and action names are. */
const AUDIT_EVENTS: AuditEvent[] = [
  {
    id: '00000000-0000-0000-0000-0000000000e1',
    occurredAt: '2026-09-08T18:14:00+00:00',
    actorUsername: 'administrator-with-a-long-login-name',
    action: 'BackupRestoredFromRemoteDestination',
    actionName: 'BackupRestoredFromRemoteDestination',
    subject: 'customer-account-with-a-rather-long-name',
    ipAddress: '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
    succeeded: true,
  },
  {
    id: '00000000-0000-0000-0000-0000000000e2',
    occurredAt: '2026-09-08T18:13:00+00:00',
    actorUsername: 'admin',
    action: 'LoginFailed',
    actionName: 'LoginFailed',
    subject: 'admin',
    ipAddress: '198.51.100.4',
    succeeded: false,
  },
]

/** Two devices; the user agents are the long unbreakable strings real browsers send. */
const SESSIONS: Session[] = [
  {
    id: '00000000-0000-0000-0000-0000000000a1',
    issuedAt: '2026-08-30T09:00:00+00:00',
    expiresAt: '2026-09-13T09:00:00+00:00',
    ipAddress: '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
    userAgent:
      'Mozilla/5.0_(X11;_Linux_x86_64)_AppleWebKit/537.36_(KHTML,_like_Gecko)_Chrome/130.0.0.0_Safari/537.36',
    isCurrent: true,
  },
  {
    id: '00000000-0000-0000-0000-0000000000a2',
    issuedAt: '2026-08-29T18:30:00+00:00',
    expiresAt: '2026-09-12T18:30:00+00:00',
    ipAddress: '198.51.100.4',
    userAgent: 'Safari on iPhone',
    isCurrent: false,
  },
]

/** A policy with ordinary values; the width risk is the labels and the switch, not the numbers. */
const POLICY: SecurityPolicy = {
  minimumPasswordLength: 12,
  forceTwoFactorForAdmins: true,
  maxFailedLoginAttempts: 5,
  lockoutMinutes: 15,
}

/** Installed PHP runtimes, one of them the default and one of unknown default state. */
const PHP_VERSIONS: PhpVersion[] = [
  { version: '8.4', isDefault: true },
  { version: '8.3', isDefault: false },
  { version: '8.2', isDefault: null },
]

/** The customer's own account, with a plan name long enough to wrap and a suspended-free state. */
const MY_ACCOUNT: MyAccount = {
  id: CUSTOMER.accountId ?? '',
  name: 'olive-shop-with-a-long-account-name',
  primaryDomain: 'a-very-long-subdomain.olive-shop-with-a-long-name.example.com',
  status: 'active',
  plan: {
    displayName: 'Professional hosting plan with extended limits',
    diskQuotaMb: 1048576,
    maxSites: 100,
    maxDatabases: 100,
    maxSftpUsers: 100,
    maxFtpUsers: 100,
    maxCronEntries: 100,
  },
}

/** A module the licence does not cover, so the upgrade page has a real catalogue entry to print. */
const LOCKED_MODULES: PanelModule[] = [
  {
    name: 'backups',
    displayName: 'Backups and disaster recovery for every hosted account',
    tier: 'addOn',
    tierDisplayName: 'Professional add-on tier',
    isEnabled: false,
  },
]

/** A base32 secret as long as real authenticators issue, with no break opportunity in it. */
const LONG_SECRET = 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP'

/**
 * Waits for the first heading of the given level inside the main region.
 * @param level The heading level the screen opens with.
 * @returns A readiness function for {@link Screen.ready}.
 */
const headingReady = (level: number): ((page: Page) => Promise<void>) => {
  return async (page) => {
    await expect(page.getByRole('main').getByRole('heading', { level }).first()).toBeVisible()
  }
}

/**
 * Waits for a piece of stubbed data to be on screen, which proves the load settled rather than
 * that a skeleton was measured.
 * @param text The literal stubbed text to wait for.
 * @returns A readiness function for {@link Screen.ready}.
 */
const textReady = (text: string): ((page: Page) => Promise<void>) => {
  return async (page) => {
    await expect(page.getByRole('main').getByText(text).first()).toBeVisible()
  }
}

/**
 * The stubs every signed-out screen shares: nobody signed in, the panel healthy, no catalogue.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const signedOut = async (page: Page): Promise<void> => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

/**
 * The stubs every administrator screen shares.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const signedInAdministrator = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

/**
 * Types credentials into the sign-in form and submits it with the keyboard, so the screen moves on
 * to the second-factor step. Fields are found by role and type, never by their translated labels.
 * @param page The page, on the sign-in screen.
 * @returns Resolves once the form was submitted.
 */
const submitSignIn = async (page: Page): Promise<void> => {
  await page.getByRole('textbox').first().fill('administrator')
  const password = page.locator('input[type="password"]')
  await password.fill('correct horse battery staple')
  await password.press('Enter')
}

/** Every screen under test, in the order the brief lists them. */
const SCREENS: Screen[] = [
  {
    name: 'sign-in',
    path: '/login',
    layout: 'auth',
    prepare: signedOut,
    ready: headingReady(2),
  },
  {
    name: 'sign-in with a long refusal',
    path: '/login',
    layout: 'auth',
    prepare: async (page): Promise<void> => {
      await signedOut(page)
      await stubLogin(page, { status: 401, code: 'InvalidCredentials', detail: LONG_REFUSAL })
    },
    ready: async (page): Promise<void> => {
      await submitSignIn(page)
      await expect(page.getByRole('main').getByText('ERR_UPSTREAM_REFUSED', { exact: false })).toBeVisible()
    },
  },
  {
    name: 'two-factor code step',
    path: '/login',
    layout: 'auth',
    prepare: async (page): Promise<void> => {
      await signedOut(page)
      await stubLogin(page, { session: null })
    },
    ready: async (page): Promise<void> => {
      await submitSignIn(page)
      await expect(page).toHaveURL(/\/login\/two-factor$/)
      await expect(page.getByRole('main').getByRole('textbox').first()).toBeVisible()
    },
  },
  {
    name: 'first-run setup',
    path: '/setup',
    layout: 'auth',
    prepare: async (page): Promise<void> => {
      await signedOut(page)
      // Installed after the shared stub so it wins: the panel has no administrator yet.
      await stubSetupState(page, { isComplete: false })
    },
    ready: headingReady(2),
  },
  {
    name: 'forgot password',
    path: '/forgot-password',
    layout: 'auth',
    prepare: signedOut,
    ready: headingReady(2),
  },
  {
    name: 'reset password',
    path: '/reset-password?token=stub-reset-token-0123456789abcdef',
    layout: 'auth',
    prepare: signedOut,
    ready: headingReady(2),
  },
  {
    name: 'accept invitation',
    path: '/accept-invitation?token=stub-invitation-token-0123456789abcdef',
    layout: 'auth',
    prepare: signedOut,
    ready: headingReady(2),
  },
  {
    name: 'steered two-factor enrolment',
    path: '/two-factor-setup',
    layout: 'auth',
    prepare: async (page): Promise<void> => {
      await stubSteeredAdministrator(page)
      await stubHealthy(page)
      await stubEmptyModules(page)
    },
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('button').first()).toBeVisible()
    },
  },
  {
    name: 'audit journal',
    path: '/settings/audit',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await signedInAdministrator(page)
      await page.route('**/api/v1/audit*', async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(AUDIT_EVENTS),
        })
      })
    },
    ready: textReady('198.51.100.4'),
  },
  {
    name: 'sessions',
    path: '/settings/sessions',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await signedInAdministrator(page)
      await page.route('**/api/v1/sessions', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SESSIONS) })
      })
    },
    ready: textReady('Safari on iPhone'),
  },
  {
    name: 'security policy',
    path: '/settings/security-policy',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await signedInAdministrator(page)
      await page.route('**/api/v1/security-policy', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(POLICY) })
      })
    },
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('textbox').first()).toHaveValue('12')
    },
  },
  {
    name: 'PHP versions',
    path: '/settings/php',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await signedInAdministrator(page)
      await page.route('**/api/v1/sites/php-versions', async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(PHP_VERSIONS),
        })
      })
    },
    ready: textReady('8.2'),
  },
  {
    name: 'mail settings',
    path: '/settings/smtp',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await signedInAdministrator(page)
      await stubSmtpSettings(page, storedSmtpSettings)
    },
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('textbox').first()).toHaveValue(storedSmtpSettings.host)
    },
  },
  {
    name: 'two-factor settings',
    path: '/settings/two-factor',
    layout: 'shell',
    prepare: signedInAdministrator,
    ready: headingReady(1),
  },
  {
    name: 'two-factor settings mid-enrolment',
    path: '/settings/two-factor',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await signedInAdministrator(page)
      await page.route('**/api/v1/auth/two-factor/enrol', async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            secret: LONG_SECRET,
            provisioningUri: `otpauth://totp/Maran:admin?secret=${LONG_SECRET}&issuer=Maran`,
          }),
        })
      })
    },
    ready: async (page): Promise<void> => {
      // The first button on the idle screen is the one that starts enrolment.
      await page.getByRole('main').getByRole('button').first().click()
      await expect(page.getByRole('main').getByText(LONG_SECRET)).toBeVisible()
    },
  },
  {
    name: 'my account (customer)',
    path: '/my-account',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await stubSignedIn(page, CUSTOMER)
      await stubHealthy(page)
      await stubEmptyModules(page)
      await page.route('**/api/v1/accounts/me', async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(MY_ACCOUNT),
        })
      })
    },
    ready: textReady(MY_ACCOUNT.primaryDomain),
  },
  {
    name: 'upgrade',
    path: '/upgrade/backups',
    layout: 'shell',
    prepare: async (page): Promise<void> => {
      await stubSignedIn(page)
      await stubHealthy(page)
      await stubModules(page, LOCKED_MODULES)
    },
    ready: headingReady(1),
  },
  {
    name: 'not found',
    path: '/this-route-does-not-exist-0123456789abcdef0123456789abcdef',
    layout: 'shell',
    prepare: signedInAdministrator,
    ready: headingReady(1),
  },
]

/** One element whose box leaves the viewport, described well enough to find it from a report. */
interface Offender {
  /** Tag name, accessible role hint and the first words of its text. */
  element: string
  /** Left edge in CSS pixels. */
  left: number
  /** Right edge in CSS pixels. */
  right: number
}

/**
 * Collects every element inside the main region whose box pokes out of the viewport and is not
 * contained by an ancestor (below the main region) that clips or scrolls its overflow. That
 * exemption is what lets a table scroll inside its own container without being reported, while
 * an unclipped long word, an over-wide card or a rigid grid is.
 * @param main The main region.
 * @returns The offending elements; an empty list means the region fits.
 */
const findOverflowing = async (main: Locator): Promise<Offender[]> => {
  return main.evaluate((root: Element): Offender[] => {
    const limit = window.innerWidth
    const found: Offender[] = []

    for (const element of Array.from(root.querySelectorAll('*'))) {
      const rect = element.getBoundingClientRect()
      const style = window.getComputedStyle(element)
      if (
        rect.width === 0 ||
        rect.height === 0 ||
        style.visibility === 'hidden' ||
        style.display === 'none'
      ) {
        continue
      }
      if (rect.right <= limit + 0.5 && rect.left >= -0.5) {
        continue
      }

      let contained = false
      for (
        let ancestor = element.parentElement;
        ancestor !== null && ancestor !== root;
        ancestor = ancestor.parentElement
      ) {
        const overflowX = window.getComputedStyle(ancestor).overflowX
        if (overflowX !== 'visible') {
          contained = true
          break
        }
      }
      if (contained) {
        continue
      }

      const words = (element.textContent ?? '').trim().replace(/\s+/g, ' ').slice(0, 40)
      found.push({
        element: `${element.tagName.toLowerCase()} "${words}"`,
        left: Math.round(rect.left),
        right: Math.round(rect.right),
      })
    }

    return found
  })
}

/**
 * Asserts a locator is visible and its whole box lies inside the viewport.
 * @param page The page, for the viewport size.
 * @param locator The element to measure.
 * @returns Resolves once asserted.
 */
const expectInsideViewport = async (page: Page, locator: Locator): Promise<void> => {
  await expect(locator).toBeVisible()
  const box = await locator.boundingBox()
  expect(box, 'the element has a box').not.toBeNull()
  const viewport = page.viewportSize()
  expect(viewport, 'the page has a viewport').not.toBeNull()
  if (box === null || viewport === null) {
    return
  }
  expect(box.x, 'left edge is inside the viewport').toBeGreaterThanOrEqual(-0.5)
  expect(box.x + box.width, 'right edge is inside the viewport').toBeLessThanOrEqual(viewport.width + 0.5)
}

for (const screen of SCREENS) {
  for (const locale of LOCALES) {
    test(`${screen.name} does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
      await page.setViewportSize(PHONE)
      await setPersistedLocale(page, locale)
      await screen.prepare(page)

      await page.goto(screen.path)
      await screen.ready(page)
      // Webfont swaps change line breaks; measuring before they settle would measure a transient.
      await page.evaluate(() => {
        return document.fonts.ready
      })

      const main = page.getByRole('main')
      await expect(main).toBeVisible()

      // 1. The document itself.
      const documentWidth = await page.evaluate(() => {
        return {
          scrollWidth: window.document.documentElement.scrollWidth,
          innerWidth: window.innerWidth,
        }
      })
      expect(documentWidth.scrollWidth, 'the document is no wider than the viewport').toBeLessThanOrEqual(
        documentWidth.innerWidth,
      )

      // 2. The main region, whose `overflow-x: hidden` would otherwise hide a failure from (1).
      const region = await main.evaluate((element) => {
        return {
          scrollWidth: element.scrollWidth,
          clientWidth: element.clientWidth,
        }
      })
      expect(region.scrollWidth, 'the main region holds nothing wider than itself').toBeLessThanOrEqual(
        region.clientWidth,
      )

      // 3. Every unclipped box inside it.
      expect(await findOverflowing(main), 'no element leaves the viewport').toEqual([])

      if (screen.layout === 'auth') {
        // The bare layout: the switcher is the visitor's only way to change language, and the
        // card is what holds the form. Both must be fully reachable.
        await expectInsideViewport(page, page.getByRole('button', { name: SWITCHER_LABEL[locale] }))
        const column = main.locator(':scope > div')
        await expectInsideViewport(page, column)
        const columnChildren = column.locator(':scope > div')
        for (let index = 0; index < (await columnChildren.count()); index += 1) {
          await expectInsideViewport(page, columnChildren.nth(index))
        }
      }
    })
  }
}
