# Maran public website — design

Date: 2026-09-19
Status: built and shipped. The site now lives in its own repository,
`gitlab.com/innovayse/maran-website`, and this repository git-ignores `website/`; §1 below argued
for a sibling directory in the monorepo, which is what it was until the move. The reasoning for
separating it from the panel holds either way — what changed is the boundary, from a directory to
a repository.
Scope: a new `website/` subproject serving maran.innovayse.com — marketing pages,
documentation, blog and release notes, in English, Russian and Armenian.

## 1. Why a separate subproject

`frontend/` is the authenticated panel: a Vite SPA behind a login, built and shipped with the
product, verified end-to-end by Playwright. The public site has the opposite requirements — it must
be crawlable, server-rendered, content-driven and deployable independently of a panel release.
Merging the two would force the panel through a rendering migration it does not need and would tie
a marketing copy change to a product build.

So: `website/` is a sibling of `frontend/` at the repository root, with its own dependencies, lint
run, build and container image. Nothing in `frontend/` changes.

The two share a visual language but no code. Design tokens are copied into `website/`'s stylesheet
rather than extracted into a shared package: one consumer on each side does not pay for a package,
and the extraction stays available the day a third consumer appears.

## 2. Stack

| Concern | Choice | Reason |
|---|---|---|
| Framework | Nuxt 4, TypeScript strict | SSR, file-based routing, first-party content and SEO modules |
| Rendering | `ssr: true`, nitro preset `node-server` | Chosen by the owner; dynamic pages (licensing, forms) stay possible without a rewrite |
| Content | `@nuxt/content` v3 | Markdown/MDC on disk, typed collections, built-in full-text index |
| Styling | Tailwind 4 via `@tailwindcss/vite` | Same engine and token vocabulary as the panel |
| Icons | `@nuxt/icon` + `@iconify-json/lucide` (local bundle) | Same icon family as the panel's `lucide-vue-next`, no runtime CDN fetch |
| SEO | `@nuxtjs/seo` (sitemap, robots, og-image, schema.org) | Covers sitemap/robots/canonical/OG in one configured module |
| i18n | `@nuxtjs/i18n` | Locale routing plus automatic `hreflang` |
| Dates | `date-fns` (same version as the panel) | Month names decline in Russian and Armenian; the library owns that grammar |
| Images | `@nuxt/image` | Responsive sources and modern formats for marketing imagery |
| Tests | Vitest + `@nuxt/test-utils` | Small, pure logic tested directly; no browser suite |
| Linting | ESLint 10 flat config (`eslint-plugin-vue`, `typescript-eslint` type-checked, `eslint-plugin-jsdoc`, `eslint-plugin-import-x`, `eslint-plugin-vuejs-accessibility`, `eslint-config-prettier` last) with `oxlint` as a fast pre-pass | The panel's configuration, carried over so one set of laws governs both front ends |

Every dependency is pinned to an exact version, as `frontend/package.json` does.

## 3. Layout

```
website/
  content/
    en/{docs,blog,releases}/**.md
    ru/{docs,blog,releases}/**.md
    hy/{docs,blog,releases}/**.md
  app/
    pages/          index, features, pricing, supported-platforms,
                    docs/[...slug], blog/index, blog/[slug],
                    releases/index, releases/[version], legal/[slug]
    components/
      marketing/    hero, feature grid, pricing table, call to action
      docs/         sidebar, table of contents, search modal, version notice
      ui/           the site's own minimal kit (button, link, badge, card)
    layouts/        DefaultLayout, DocsLayout
    composables/    one per file, `use<X>.ts`
    utils/          pure helpers, one purpose per file
    types/          one domain per file
    assets/css/     main.css (design tokens copied from frontend/src/assets/css/main.css)
  i18n/locales/{en,ru,hy}/*.json  # shell UI strings, one file per namespace; page copy lives in content/
  tests/{unit,components}/        # Vitest specs, written in the test pass
  scripts/                        # lint.mjs, check-content.mjs
  nuxt.config.ts, content.config.ts, vitest.config.ts, eslint.config.ts, Dockerfile
```

`content/` holds page copy; `i18n/locales/` holds only chrome (navigation labels, buttons, search
placeholder). Splitting them this way keeps translators out of JSON for prose and out of Markdown
for labels.

## 4. Rules that apply, and the two that do not

`rules/nuxt.md` governs this subproject and is written for it. Everything in `rules/vue.md` that is
about the language rather than the panel binds here unchanged — const arrow functions only,
mandatory JSDoc on every unit, one unit per file, `import type` inline, stock Tailwind text steps
only, no `any`, no `console`, accessibility lint. Two rules are deliberately relaxed, and
`rules/nuxt.md` states both:

1. **`vue/no-bare-strings-in-template`** stays an error in the shell (layouts, `components/ui`,
   navigation) and is switched off for `components/marketing/**` and content-rendering components.
   A marketing page's text comes from Markdown or from a slot; forcing it through i18n keys would
   move prose into JSON, where nobody can review it as prose.
2. **UI-kit-only** applies within `website/`, against `website/app/components/ui/**` — not against
   the panel's kit. The two runtimes differ (Nuxt SSR vs Vite SPA) and the public site's controls
   are a much smaller set; importing the panel's kit would drag its stores, router and API
   assumptions across the boundary.

Everything else that a linter can express is configured as an error in `website/eslint.config.ts`,
mirroring the panel's config, and `npm run lint` is a merge gate.

## 5. Routes, i18n and content fallback

- Public routes: `/`, `/features`, `/pricing`, `/faq`, `/security`, `/platforms`,
  `/docs/[...slug]`, `/blog`, `/blog/[slug]`, `/releases`, `/releases/[version]`, `/legal/[slug]`,
  and the error page.
- `/faq` and `/security` exist as pages of their own for SEO reasons rather than as documentation
  chapters: they are the two pages people look for by name, and a questions page is the one kind of
  content a search engine will render directly in a result.
- Locale strategy `prefix_except_default` with `en` as default: `maran.innovayse.com/docs/install`,
  `/ru/docs/install`, `/hy/docs/install`.
- Browser-language detection uses a cookie and **does not redirect** a visitor who arrived on an
  explicit URL. A redirect on first visit hides the requested page from crawlers and from anyone
  sharing a link.
- **Fallback:** when a page is missing in `ru` or `hy`, the English body renders under the
  requested URL with a visible "not translated yet" notice and `noindex` on that URL. An empty page
  is worse than English, and an indexed duplicate is worse than both.

## 6. SEO

Per page, without exception:

- absolute `canonical`;
- `hreflang` for `en`, `ru`, `hy` plus `x-default` → the English URL;
- a generated OG image (`nuxt-og-image` template carrying the page title and section);
- JSON-LD: `SoftwareApplication` on the home page, `TechArticle` in docs, `BlogPosting` in blog
  posts, `FAQPage` on the questions page, `BreadcrumbList` on every nested page.

Site-wide: `sitemap.xml` covering all three locales — including every content-driven URL, which a
server route enumerates from the collections because a page behind a dynamic route is invisible to
the router — and `robots.txt` that serves a blanket
`noindex` unless the deployment sets `NUXT_PUBLIC_SITE_INDEXABLE=true` — read at runtime, default
disallow, so a staging host can never be indexed by mistake.

Docs search is client-side over the `@nuxt/content` section index, opened with a ⌘K modal. No
third-party search service: it would ship visitor queries off-site and add a runtime dependency for
a corpus this small.

## 7. Releases

Release notes are hand-written Markdown at `content/<lang>/releases/<version>.md`, with frontmatter
`version`, `date` and `channel`. They are prose aimed at an operator deciding whether to upgrade —
the root `CHANGELOG.md` stays the technical record.

`scripts/check-content.mjs` runs in `npm run lint` and fails when:

- a version listed in the root `CHANGELOG.md` has no English release page;
- frontmatter does not satisfy the collection's schema;
- an internal link points at a path no page or content file resolves to.

`/supported-platforms` renders a distribution/version support matrix with end-of-support dates,
from a single content file per locale.

## 8. Build, deployment and CI

- Scripts: `dev`, `build`, `preview`, `lint` (oxlint pre-pass, then ESLint as authority),
  `typecheck` (`nuxt typecheck`), `lint:content`, `test` (Vitest).
- Multi-stage `Dockerfile` on `node:lts-alpine` producing `.output`, run behind the existing
  reverse proxy for maran.innovayse.com; a dev service is added to `docker/`.
- A `website` CI job runs lint, typecheck, build and the Vitest suite; `maran check` gains the same
  commands so the checks are reachable the documented way.

## 9. Tests

Implementation first, tests in a dedicated pass afterwards (rules/testing.md). Vitest specs in
`website/tests/` — there is no browser suite in this subproject — cover:

- content resolution returns the requested locale's page when it exists;
- a missing translation falls back to the English body and reports the fallback, so the notice and
  the `noindex` follow from one decision rather than three;
- documentation navigation groups by section and sorts by `order`;
- structured data is built for each page kind, with `BreadcrumbList` derived from the path;
- the three locale files hold identical key trees;
- a rendered documentation page emits canonical, three `hreflang` links and `x-default`.

## 10. Out of scope for this version

Documentation versioning (`/docs/v1`, `/docs/v2`), a customer-facing licence or marketplace flow,
newsletter capture, and a shared design-token package with `frontend/`. Each is a separate spec if
it is ever wanted; nothing here blocks them.
