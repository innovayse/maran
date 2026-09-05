import type { Page } from '@playwright/test'
import type { Ban, FirewallRule, WhitelistEntry } from '../../src/types/firewall'

/** The rules collection. The trailing `*` covers the DELETE, which names its rule in the query string. */
const RULES_PATTERN = '**/api/v1/firewall/rules*'

/** The bans collection. The trailing `*` covers the DELETE, which names its address in the query string. */
const BANS_PATTERN = '**/api/v1/firewall/bans*'

/** The whitelist collection, which answers the listing and the create. */
const WHITELIST_PATTERN = '**/api/v1/firewall/whitelist'

/** One whitelist row, which answers the removal. A `*` never spans a `/`, so this is the narrower pattern. */
const WHITELIST_ENTRY_PATTERN = '**/api/v1/firewall/whitelist/*'

/**
 * The three lists a stubbed panel is holding, mutated in place by the stubbed writes so that a
 * re-read after a change reports what the change did.
 *
 * The store re-reads the rules after every rule change on purpose (a deny whose source range does
 * not match removes nothing while still reporting success), so a stub that answered the same list
 * every time would let a screen that never sent the change at all pass.
 */
export interface StubbedFirewall {
  /** The port rules the stubbed panel reports. */
  rules: FirewallRule[]
  /** The bans the stubbed panel reports, newest first. */
  bans: Ban[]
  /** The exempt ranges the stubbed panel reports, oldest first. */
  whitelist: WhitelistEntry[]
}

/** The id a stubbed `POST /api/v1/firewall/whitelist` answers with. */
export const stubbedWhitelistEntryId = '77777777-7777-7777-7777-777777777777'

/** The instant a stubbed write records against the row it creates. */
export const stubbedWriteInstant = '2026-09-01T12:00:00+00:00'

/**
 * The note the panel writes against the row it seeds from the address the installer was run from,
 * copied verbatim from `WhitelistSeeder.SeedNote` on the server.
 *
 * It is a server-side string, so it belongs in the fixture rather than in the SPA: nothing on the
 * wire marks that row as the panel's, and the note is the only thing that says so.
 */
export const seededWhitelistNote = 'Seeded from the address this server was installed from'

/**
 * Whether two rules are the same rule.
 *
 * All three values are compared because all three of them ARE the rule — there is no identifier on
 * either side of the wire to compare instead.
 * @param left One rule.
 * @param right The other.
 * @returns True when they name the same port, protocol and source range.
 */
const isSameRule = (left: FirewallRule, right: FirewallRule): boolean => {
  return (
    left.port === right.port && left.protocol === right.protocol && left.sourceCidr === right.sourceCidr
  )
}

/**
 * Fulfils the three firewall collections with the given lists, and applies every write to them.
 *
 * The writes answer exactly what the module answers: the rules endpoints and the unban answer a
 * bare `true`, and the whitelist create answers `201` with the row it made — the identity a later
 * removal has to name.
 * @param page The Playwright page whose network the routes are installed on.
 * @param state The three lists the stub reports, mutated in place by the writes.
 * @returns Resolves once every route is installed.
 */
export const stubFirewall = async (page: Page, state: StubbedFirewall): Promise<void> => {
  await page.route(RULES_PATTERN, async (route) => {
    const request = route.request()

    if (request.method() === 'POST') {
      const submitted = request.postDataJSON() as FirewallRule
      state.rules.push(submitted)
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }

    if (request.method() === 'DELETE') {
      const query = new URL(request.url()).searchParams
      const removed: FirewallRule = {
        port: Number(query.get('port')),
        protocol: query.get('protocol') === 'udp' ? 'udp' : 'tcp',
        sourceCidr: query.get('sourceCidr') ?? '',
      }
      state.rules = state.rules.filter((rule) => {
        return !isSameRule(rule, removed)
      })
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(state.rules),
    })
  })

  await page.route(BANS_PATTERN, async (route) => {
    const request = route.request()

    if (request.method() === 'POST') {
      const submitted = request.postDataJSON() as { address: string; durationMinutes: number | null }
      state.bans = [
        {
          id: `ban-${state.bans.length + 1}`,
          ipAddress: submitted.address,
          // The reason is the panel's, never the caller's: a ban placed from this form is a manual
          // one, and the request carries no reason field for the SPA to have chosen it with.
          reason: 'manual',
          failures: 0,
          bannedAt: stubbedWriteInstant,
          expiresAt: null,
        },
        ...state.bans,
      ]
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }

    if (request.method() === 'DELETE') {
      const address = new URL(request.url()).searchParams.get('address') ?? ''
      state.bans = state.bans.filter((ban) => {
        return ban.ipAddress !== address
      })
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(state.bans),
    })
  })

  await page.route(WHITELIST_PATTERN, async (route) => {
    const request = route.request()

    if (request.method() === 'POST') {
      const submitted = request.postDataJSON() as { cidr: string; note: string }
      const created: WhitelistEntry = {
        id: stubbedWhitelistEntryId,
        cidr: submitted.cidr,
        note: submitted.note,
        createdAt: stubbedWriteInstant,
      }
      state.whitelist.push(created)
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(created),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(state.whitelist),
    })
  })

  await page.route(WHITELIST_ENTRY_PATTERN, async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').pop() ?? ''
    state.whitelist = state.whitelist.filter((entry) => {
      return entry.id !== id
    })
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })
}

/**
 * Refuses all three firewall collections with one RFC 7807 problem body.
 *
 * Written for the customer case, which the module answers `403` and not `404`: a firewall rule is a
 * fact about the whole machine, there is no tenant to hide behind, and the module says so in as
 * many words. The SPA renders whatever the panel sent (rules/vue.md), so the stub's job is to send
 * the panel's own already-localized text and nothing else.
 * @param page The Playwright page whose network the routes are installed on.
 * @param status The status the panel answers with.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once every route is installed.
 */
export const stubFirewallRefusal = async (
  page: Page,
  status: number,
  detail: string,
): Promise<void> => {
  const body = JSON.stringify({ code: 'Forbidden', title: 'Forbidden', detail })
  for (const pattern of [RULES_PATTERN, BANS_PATTERN, WHITELIST_PATTERN]) {
    await page.route(pattern, async (route) => {
      await route.fulfill({ status, contentType: 'application/problem+json', body })
    })
  }
}
