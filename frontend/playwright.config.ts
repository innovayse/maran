import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for the Maran SPA's end-to-end suite — the
 * only kind of test this frontend has (rules/testing.md: "no colocated unit
 * tests — the SPA is verified end-to-end").
 *
 * Runs with no backend present by default: golden-path specs stub `/health`
 * and `/api/v1/modules` themselves via `e2e/fixtures/`, and the `webServer`
 * below only needs Vite's own server, not `Maran.Host`. To instead
 * exercise a real running backend (proxied by Vite per `vite.config.ts`),
 * set `E2E_REAL_BACKEND=1` when invoking `npm run test:e2e`; specs that
 * install network stubs are expected to be skipped or adapted by the caller
 * in that mode — this flag only changes what the config itself assumes, it
 * does not alter existing specs.
 */
/**
 * The port the suite's own `preview` server is started on.
 *
 * 5173 by default, in CI and locally alike, so a plain `npm run test:e2e` needs no environment.
 * `E2E_PORT` moves it, for the one case the default cannot serve: a demonstration stack already
 * holding 5173 on the same machine. It moves the suite rather than reusing what is there —
 * `reuseExistingServer` stays false, for the reason spelled out below — so the run still measures
 * a freshly built `dist/` and never somebody else's dev server.
 */
const port = Number(process.env.E2E_PORT ?? 5173)
const baseURL = `http://127.0.0.1:${port}`
const isCi = process.env.CI === 'true' || process.env.CI === '1'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  workers: isCi ? 1 : undefined,
  reporter: 'list',
  timeout: 30_000,
  expect: {
    timeout: 5_000,
  },
  use: {
    baseURL,
    trace: 'on-first-retry',
    actionTimeout: 10_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    // Golden-path specs stub the network themselves, so no backend needs to be reachable.
    //
    // The suite runs against `preview` — the built app — EVERYWHERE, locally exactly as in CI, and
    // builds first so the served output can never be a stale one. This is not a preference:
    //
    // - The dev server transforms each route's modules on its first visit, and every local failure
    //   this suite has ever produced was a first-paint timeout on a route's first visit. The same
    //   suite on `preview` was green at a HIGHER host load than the `dev` run that failed, and the
    //   failing specs each passed in isolation in a fraction of the timeout. So the flake was the
    //   server, not the product and not the box — and a flaky suite is a suite whose green is not
    //   evidence.
    // - Raising the local timeouts instead would keep the flake and hide real slowness: the budget
    //   that gates a merge would no longer be the budget a developer runs against.
    // - `preview` WITHOUT the build folded in would trade a loud flake for a silent wrong answer —
    //   a run against yesterday's `dist/` reports on code nobody is editing. The build is
    //   incremental and takes ~1s, which is cheaper than one flaky retry.
    //
    // Hot reload is worth nothing here: every test opens a fresh page against a fresh context.
    command: `npm run build && npm run preview -- --port ${port.toString()} --strictPort --host 127.0.0.1`,
    url: baseURL,
    // Deliberately false LOCALLY TOO. Reusing whatever happens to hold 5173 — a `npm run dev` left
    // open in another terminal — silently puts the suite back on the dev server and back into the
    // flake, and the run says nothing about which server it measured. Refusing the port is a loud,
    // one-line failure; serving the wrong artefact is the failure mode this repository has paid
    // for more than once (rules/testing.md).
    reuseExistingServer: false,
    // Generous on a cold CI runner: a slow start must not read as a broken app.
    timeout: 120_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
