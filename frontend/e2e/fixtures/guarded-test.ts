import { expect, test as base } from '@playwright/test'
import { installNetworkGuard, type NetworkGuard } from './network-guard'

/**
 * The stubbed lane's `test` with the network and console guard switched on (issue #519).
 *
 * A spec imports `test` and `expect` from here instead of from `@playwright/test`. After the test
 * body, the guard requires that: every API request was answered by a stub the spec installed, no
 * response was a 4xx/5xx the spec did not announce with `guard.allow(...)`, no console error or
 * uncaught page error happened, and every JSON body a stub (or the panel) returned conforms to
 * `docs/api/openapi-baseline.json`. A spec whose subject IS a refusal announces it:
 *
 *     guard.allow(403, /\/api\/v1\/sessions$/)
 *
 * Opting in is per spec file on purpose: a spec that predates the guard may rely on an unstubbed
 * read failing quietly, and each such spec is moved over together with the stub it was missing.
 */
export const test = base.extend<{ guard: NetworkGuard }>({
  guard: [
    async ({ page }, use): Promise<void> => {
      const guard = await installNetworkGuard(page, 'stubbed')
      await use(guard)
      // Asserted after the body so the findings are the whole test's, not the first screen's.
      expect(await guard.problems(), 'the page asked for, or was given, something it should not have').toEqual([])
    },
    { auto: true },
  ],
})

export { expect }
