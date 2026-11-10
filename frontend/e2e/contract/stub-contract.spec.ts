import { expect, test } from '@playwright/test'
import { installNetworkGuard } from '../fixtures/network-guard'
import { findOperation, schemaNamed } from '../fixtures/openapi-baseline'
import { checkAgainstSchema } from '../fixtures/openapi-schema-check'
import { FRESH_SERVER, READY_SERVER, CHANGED_SERVER, PINNED_SERVER, MAIL_WARNING, discoveredAccount } from '../fixtures/migration'
import { dnsDraft, dnsRecord, dnsRecordId } from '../fixtures/stub-dns-routes'
import { THE_PLAN, THREE_PLANS } from '../fixtures/stub-plans-route'
import { subUserRow } from '../fixtures/stub-sub-users-routes'

// Issue #519: a stub must not drift from the API it stands in for. The shared fixtures are checked
// against `docs/api/openapi-baseline.json` directly, and the guard that does the same for every
// response of a guarded spec is shown to catch each way a stubbed lane can lie. Each refusal has
// its accepting control beside it (rules/testing.md): a checker that rejected everything would
// pass every line that only hands it broken input.
//
// This file deliberately does NOT use `fixtures/guarded-test.ts`: its subject is the guard itself, so
// it installs its own guards with `installNetworkGuard` and feeds them an unannounced refusal, a
// drifted body, an unstubbed request and a console error on purpose. The automatic guard of the
// guarded fixture would record those same findings and fail the test that provokes them.

/** An endpoint whose documented 200 is a plain array, so a conforming stub is one line. */
const SWITCHABLE_ACCOUNTS = '**/api/v1/me/accounts'

test('the baseline knows the operations the checks below depend on', () => {
  // The vacuity guard on the axis that can go blind: a checker reading an empty or moved baseline
  // would find no operation and every "conforms" below would be vacuous.
  expect(findOperation('GET', '/api/v1/me/accounts')).not.toBeNull()
  expect(findOperation('GET', '/api/v1/plans/choices')).not.toBeNull()
  expect(findOperation('GET', '/api/v1/no-such-endpoint')).toBeNull()
  expect(schemaNamed('Modules.Accounts.Common.PlanChoiceDto')).not.toBeNull()
})

test('the plan fixtures the create-account form is driven with conform to the plan choice contract', () => {
  const schema = schemaNamed('Modules.Accounts.Common.PlanChoiceDto')
  expect(schema).not.toBeNull()

  for (const plan of [THE_PLAN, ...THREE_PLANS]) {
    expect(checkAgainstSchema(schema ?? {}, plan, '$')).toEqual([])
  }
})

test('the checker names a field of the wrong type, a field the contract lacks, and a null where none is allowed', () => {
  const schema = schemaNamed('Modules.Accounts.Common.PlanChoiceDto') ?? {}

  expect(checkAgainstSchema(schema, { ...THE_PLAN, displayName: 5 }, '$')).toEqual([
    '$.displayName: is integer where the contract says string',
  ])
  expect(checkAgainstSchema(schema, { ...THE_PLAN, invented: true }, '$')).toEqual([
    '$.invented: is not a field of the contract',
  ])
  expect(checkAgainstSchema(schema, { ...THE_PLAN, displayName: null }, '$')).toEqual([
    '$.displayName: is null where the contract does not allow null',
  ])
  expect(checkAgainstSchema(schema, { ...THE_PLAN, id: 'not-a-uuid' }, '$')).toEqual([
    '$.id: "not-a-uuid" is not a UUID',
  ])
})

test('the guard accepts a stub that conforms and an announced refusal', async ({ page }) => {
  const guard = await installNetworkGuard(page, 'stubbed')
  await page.route(SWITCHABLE_ACCOUNTS, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })

  await page.goto('/api/v1/me/accounts')

  expect(await guard.problems()).toEqual([])

  // An announced refusal is not a finding; the same refusal unannounced is (next test).
  await page.unroute(SWITCHABLE_ACCOUNTS)
  guard.allow(401, /\/api\/v1\/me\/accounts$/)
  await page.route(SWITCHABLE_ACCOUNTS, async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Unauthorized', status: 401 }),
    })
  })
  await page.goto('/api/v1/me/accounts')

  expect(await guard.problems()).toEqual([])
})

test('the guard records an unannounced refusal, a drifted body, an unstubbed request and a console error', async ({
  page,
}) => {
  const guard = await installNetworkGuard(page, 'stubbed')

  await page.route(SWITCHABLE_ACCOUNTS, async (route) => {
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Forbidden', status: 403 }),
    })
  })
  await page.goto('/api/v1/me/accounts')
  await page.unroute(SWITCHABLE_ACCOUNTS)

  await page.route(SWITCHABLE_ACCOUNTS, async (route) => {
    // An object where the contract documents an array: the drift a stub can hide for months.
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ items: [] }) })
  })
  await page.goto('/api/v1/me/accounts')
  // A script added to the page rather than a function evaluated in it, so the page itself raises the error.
  await page.addScriptTag({ content: "console.error('a console error the page raised')" })
  await page.unroute(SWITCHABLE_ACCOUNTS)

  // Nothing answers this one: the context-level catch-all finds it and refuses it. The navigation
  // itself fails, which is the page's view of an unanswered read.
  await page.goto('/api/v1/plans/choices').catch(() => {return undefined})

  const problems = await guard.problems()
  expect(problems).toContain('unintended 403: GET /api/v1/me/accounts')
  expect(problems.some((problem) => {return problem.startsWith('contract: GET /api/v1/me/accounts -> 200 $: is object')})).toBe(
    true,
  )
  expect(problems).toContain('console error: a console error the page raised')
  expect(problems).toContain('unstubbed request: GET /api/v1/plans/choices')
})

test('the guard does not judge a framed module route against the panel contract, and does judge the same route unframed', async ({ page }) => {
  const guard = await installNetworkGuard(page, 'stubbed')
  await page.route('**/api/v1/me/accounts', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
  await page.route('**/api/v1/hello/greeting', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '{"greeting":"hi"}' })
  })
  await page.goto('/api/v1/me/accounts')

  await page.evaluate(async () => {
    await fetch('/api/v1/hello/greeting', { headers: { 'X-Maran-Frame': 'hello' } })
  })
  expect(await guard.problems()).toEqual([])

  await page.evaluate(async () => {
    await fetch('/api/v1/hello/greeting')
  })
  expect(await guard.problems()).toEqual([
    'contract: GET /api/v1/hello/greeting -> 200 is not an operation of docs/api/openapi-baseline.json',
  ])
})

test('the guard records a body that claims JSON and does not parse, and accepts one that does', async ({ page }) => {
  const guard = await installNetworkGuard(page, 'stubbed')
  await page.route(SWITCHABLE_ACCOUNTS, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[' })
  })
  await page.goto('/api/v1/me/accounts')

  expect(await guard.problems()).toEqual(['contract: GET /api/v1/me/accounts -> 200 claims JSON but the body does not parse'])

  // The accepting control: the same route with a body that parses adds nothing.
  await page.unroute(SWITCHABLE_ACCOUNTS)
  await page.route(SWITCHABLE_ACCOUNTS, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
  await page.goto('/api/v1/me/accounts')

  expect(await guard.problems()).toHaveLength(1)
})

test('the remote server, DNS record and sub-user fixtures conform to the contract they stand in for', () => {
  const detail = schemaNamed('Modules.ServerMigration.Common.RemoteServerDetailDto') ?? {}
  for (const server of [FRESH_SERVER, READY_SERVER, CHANGED_SERVER, PINNED_SERVER]) {
    expect(checkAgainstSchema(detail, server, '$')).toEqual([])
  }
  // The inverse control: the casing the panel does not send is refused, so the line above can fail.
  expect(checkAgainstSchema(detail, { ...READY_SERVER, state: 'Ready' }, '$')).toHaveLength(1)

  const warning = schemaNamed('Modules.ServerMigration.Common.DiscoveryWarningDto') ?? {}
  expect(checkAgainstSchema(warning, MAIL_WARNING, '$')).toEqual([])
  const site = schemaNamed('Modules.ServerMigration.Common.DiscoveredSiteDto') ?? {}
  expect(checkAgainstSchema(site, discoveredAccount('alice').sites[0], '$')).toEqual([])

  const record = schemaNamed('Modules.Dns.Common.ZoneRecordDto') ?? {}
  const made = dnsRecord(dnsRecordId('zone'), 'rec-www', dnsDraft({ name: 'www', type: 'a', value: '203.0.113.10' }))
  expect(checkAgainstSchema(record, made, '$')).toEqual([])
  expect(checkAgainstSchema(record, { ...made, id: 'rec-www' }, '$')).toHaveLength(1)

  const subUser = schemaNamed('Modules.Identity.Common.SubUserDto') ?? {}
  expect(checkAgainstSchema(subUser, subUserRow('deploy', ['databases']), '$')).toEqual([])
  expect(checkAgainstSchema(subUser, { ...subUserRow('deploy', []), id: 'deploy' }, '$')).toHaveLength(1)
})
