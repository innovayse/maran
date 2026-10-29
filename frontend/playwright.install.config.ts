import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for the INSTALLED-PANEL browser stage (issue #68), and nothing else.
 *
 * The everyday suite (`playwright.config.ts`) builds the SPA and serves it from Vite's preview on
 * plain HTTP, so it can never meet the panel's TLS listener. That blindness is why issue #67 — an
 * Ed25519 self-signed certificate Chromium refuses with `ERR_SSL_VERSION_OR_CIPHER_MISMATCH` —
 * shipped while every curl-based check in the install polygon was green: `curl -k` negotiates it
 * happily. This configuration points a real Chromium at a panel a real installer put on a real
 * systemd, through nginx and its certificate, with no `webServer` of its own.
 *
 * It is a separate file, and the everyday config ignores `e2e/install/`, so the stage is never
 * collected by `maran test spa` (whose per-spec-file baseline would otherwise gain a row that
 * cannot pass without an installed panel). It is driven by `maran installer --browser`
 * (scripts/lib/install-polygon.sh), and can be run by hand:
 *
 *     MARAN_INSTALLED_URL=https://127.0.0.1:18443 MARAN_SETUP_TOKEN=<token> \
 *       npx playwright test -c playwright.install.config.ts
 *
 * `ignoreHTTPSErrors` is on because the installer's certificate is self-signed BY DESIGN and a
 * trust failure is not what this stage measures. It does NOT bypass a handshake failure: a cipher
 * or protocol mismatch still aborts navigation, which is exactly the #67 class this stage exists
 * to catch.
 */

/** The installed panel's origin, e.g. `https://127.0.0.1:18443`. Empty when unset; the spec fails on that. */
const installedUrl = process.env.MARAN_INSTALLED_URL ?? ''

export default defineConfig({
  testDir: './e2e/install',
  fullyParallel: false,
  forbidOnly: true,
  // Never retried: the stage runs once against a panel whose one-time setup it consumes, so a
  // retry would meet a panel that already has an administrator and report on that instead.
  retries: 0,
  workers: 1,
  reporter: 'list',
  timeout: 120_000,
  expect: {
    timeout: 20_000,
  },
  use: {
    baseURL: installedUrl,
    ignoreHTTPSErrors: true,
    trace: 'off',
    actionTimeout: 20_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
