import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for the REAL-STACK lane (issue #519), and nothing else.
 *
 * The everyday suite stubs every API route, so it cannot see a backend or agent failure (#498), an
 * authorization mismatch between the SPA and the panel (#513, #515), a stub that drifted from the
 * API, or a flow that needs several real steps (#514). This lane points a real Chromium at a panel
 * and agent that are really running (a deploy-local host or an installer-polygon panel published on
 * a port), installs no `page.route`, reaches every screen by clicking, and fails a test on any
 * 4xx/5xx the journey did not announce, any console error, any response that disagrees with
 * `docs/api/openapi-baseline.json`, and any English chrome on a Russian or Armenian page.
 *
 * It has no `webServer`: the panel is somebody else's. It is a separate file, and the everyday
 * config ignores `e2e/real/`, so `maran test spa` and its baseline never collect these specs. It is
 * driven by `maran e2e-real` (scripts/lib/e2e-real.sh), and can be run by hand:
 *
 *     MARAN_REAL_URL=https://10.0.0.5:8443 \
 *     MARAN_REAL_ADMIN_USERNAME=... MARAN_REAL_ADMIN_PASSWORD=... \
 *     MARAN_REAL_RESELLER_USERNAME=... MARAN_REAL_RESELLER_PASSWORD=... \
 *     MARAN_REAL_CUSTOMER_USERNAME=... MARAN_REAL_CUSTOMER_PASSWORD=... \
 *       npx playwright test -c playwright.real.config.ts
 *
 * `ignoreHTTPSErrors` is on because a deployed dev panel's certificate is self-signed by design; a
 * handshake Chromium refuses still aborts navigation.
 */

/** The running panel's origin, e.g. `https://10.0.0.5:8443`. Empty when unset; the lane then refuses. */
const realUrl = process.env.MARAN_REAL_URL ?? ''

export default defineConfig({
  testDir: './e2e/real',
  // One worker, in file order: the journeys create and delete real accounts on one panel.
  fullyParallel: false,
  forbidOnly: true,
  // Never retried: a retry would meet the half-made data of the first attempt and report on that.
  retries: 0,
  workers: 1,
  reporter: 'list',
  // A real agent runs real host operations (an account's final backup, an nginx reload).
  timeout: 180_000,
  expect: {
    timeout: 20_000,
  },
  use: {
    baseURL: realUrl,
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    actionTimeout: 20_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
