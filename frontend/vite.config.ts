import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'

/**
 * Vite build/dev configuration for the Maran SPA shell.
 *
 * Dev server proxies `/health`, `/api` and `/module-frames` to the Maran.Host backend,
 * which listens on http://localhost:5000 when run without a launch profile
 * (verified by running the Host locally and observing Kestrel's bound URL).
 */
/** Origin the API listens on in development (scripts/run-dev.sh). */
const API_ORIGIN = 'http://127.0.0.1:5080'

/**
 * What both servers forward to the panel's API.
 *
 * The API's address in development must match ASPNETCORE_URLS in scripts/run-dev.sh, which in turn
 * matches the port the installer writes and the nginx vhost proxies to in production — one port,
 * stated in four places that have to agree. It once said 5000 while the API listened on 5080, and
 * every request in development answered 502: the e2e suite stubs the network, so nothing failed
 * until someone opened the panel in a browser.
 */
const API_PROXY = {
  '/health': {
    target: API_ORIGIN,
    changeOrigin: true,
  },
  '/api': {
    target: API_ORIGIN,
    changeOrigin: true,
  },
  // The documents and assets of a packaged module's isolated frame (#185). The frame cannot reach
  // `/api` (its CSP forbids every request), but its own page and script are loaded from here, so
  // dev and preview must forward the prefix exactly as nginx does in production.
  '/module-frames': {
    target: API_ORIGIN,
    changeOrigin: true,
  },
}

/**
 * Whether this build is the public demo's (`VITE_MARAN_DEMO=1`, set by `npm run build:demo`).
 *
 * The flavour is a build-time constant and not a runtime probe, on purpose: the normal bundle
 * then holds no demo component, store, request or text (the layouts guard every mount with
 * `__MARAN_DEMO__ ? defineAsyncComponent(() => import(…)) : null`, which folds to `null` here, so
 * the imports are unreachable and never emitted), and a real panel makes no request to find out
 * whether it is a demo.
 * @param mode The Vite mode the build runs in, which selects the `.env.<mode>` files.
 * @returns Whether `VITE_MARAN_DEMO` is `1` for this build.
 */
const isDemoBuild = (mode: string): boolean => {
  return loadEnv(mode, fileURLToPath(new URL('.', import.meta.url)), 'VITE_').VITE_MARAN_DEMO === '1'
}

export default defineConfig(({ mode }) => {return {
  define: {
    __MARAN_DEMO__: JSON.stringify(isDemoBuild(mode)),
  },
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    rollupOptions: {
      input: {
        main: fileURLToPath(new URL('./index.html', import.meta.url)),
        // `UiChart`'s Playwright-only mount point (e2e/fixtures/chart-harness.html):
        // registered as a real build entry so `vite preview` — what CI's e2e run
        // serves — can reach it at the same path the dev server already does,
        // rather than only the dev server's implicit any-.html-file serving.
        chartHarness: fileURLToPath(new URL('./e2e/fixtures/chart-harness.html', import.meta.url)),
        // The list kit's Playwright-only mount point (e2e/fixtures/list-kit-harness.html), for the
        // same reason.
        listKitHarness: fileURLToPath(new URL('./e2e/fixtures/list-kit-harness.html', import.meta.url)),
        // The loading kit's Playwright-only mount point (e2e/fixtures/skeleton-harness.html), for
        // the same reason.
        skeletonHarness: fileURLToPath(new URL('./e2e/fixtures/skeleton-harness.html', import.meta.url)),
        // The toolbar's Playwright-only mount point (e2e/fixtures/toolbar-harness.html), for the
        // same reason.
        toolbarHarness: fileURLToPath(new URL('./e2e/fixtures/toolbar-harness.html', import.meta.url)),
      },
    },
  },
  server: {
    proxy: API_PROXY,
  },
  // `preview` serves the built output, and it is what the e2e suite runs against — locally as well
  // as in CI (playwright.config.ts). It therefore has to forward the API exactly as `dev` does:
  // without this, `E2E_REAL_BACKEND=1` would hit a static file server that answers 404 to
  // `/api/...`, and the one spec that needs a live stack would fail for a reason that has nothing
  // to do with the panel. Vite reads `server.proxy` for `dev` only — the two are separate options
  // and the preview server inherits nothing.
  preview: {
    proxy: API_PROXY,
  },
}})
