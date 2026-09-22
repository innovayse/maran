# Maran Public Website Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `website/` — a server-rendered Nuxt 4 site at maran.innovayse.com carrying marketing pages, documentation, a blog and release notes in English, Russian and Armenian.

**Architecture:** A standalone subproject beside `frontend/`, sharing the panel's visual tokens and lint laws but none of its code. Page copy lives as Markdown under `website/content/<locale>/`, read through `@nuxt/content` typed collections; chrome strings live in `website/i18n/locales/`. Rendering is Node SSR (`nitro` preset `node-server`), containerised and run behind the existing reverse proxy.

**Tech Stack:** Nuxt 4, TypeScript (strict), `@nuxt/content` v3 (with `better-sqlite3`), `@nuxtjs/i18n`, `@nuxtjs/seo` (with `@takumi-rs/core` for OG image rendering), `@nuxt/image`, `@nuxt/icon` + `@iconify-json/lucide`, Tailwind 4, ESLint 10 flat config + oxlint, Vitest + `@nuxt/test-utils`.

**Spec:** `docs/superpowers/specs/2026-09-19-maran-website-design.md`

**Status: executed.** The site was built to this plan and then moved out of the monorepo into
`gitlab.com/innovayse/maran-website`. Task 9's GitHub workflow is gone with it — that repository
runs its own pipeline — and the paths below read `website/` because that is where the site is
cloned when both are wanted side by side.

## Global Constraints

- **Implementation first, tests in a dedicated pass** (rules/testing.md). Tasks 1–9 build; Task 10 writes the whole Vitest suite in `website/tests/`. There is no browser suite in this subproject. Do not interleave.
- **Never `git commit` or push** unless the owner explicitly commands it (CLAUDE.md, rules/git.md). Each task below ends with a "stage and report" step; run `git add` only, then stop and report. No AI attribution trailers ever.
- **JSDoc is mandatory on ALL code, private included** (rules/vue.md): every component (a block at the top of `<script setup>`), every arrow-function const, every composable and each function it returns, every props/emits declaration, every type/interface/enum and every non-obvious field, every non-literal module-level constant. `@param` per parameter, `@returns` whenever something is returned. Enforced by `jsdoc/require-jsdoc` with `publicOnly: false` at `error`; only `**/*.spec.ts` is exempt.
- **Every function is a `const` arrow function.** `func-style: ["error", "expression"]` plus `no-restricted-syntax` banning `FunctionDeclaration` and `FunctionExpression`.
- **One unit per file.** One component per `.vue`, one composable per file named after its export, one domain per `types/` file. No barrel `index.ts`.
- **Font sizes are stock Tailwind steps only** — `text-xs`…`text-2xl`. No arbitrary values, no custom scale, no `font-size` in px in scoped CSS.
- **Locales:** `en` (default, unprefixed), `ru`, `hy`. Site origin `https://maran.innovayse.com`.
- **Exact versions** in `package.json` — no `^` or `~`, matching `frontend/package.json` for anything both use.
- **English only in the repository** — code, comments, content, script output (CLAUDE.md). Russian appears only in chat.
- **No competitor or third-party product names** in rules, comments, docs or copy (rules/README.md).
- Node 24 is the local toolchain (`node -v` → v24.14.0); the container base is `node:lts-alpine`.

---

### Task 1: Scaffold `website/` with Nuxt, Tailwind and the lint gate

**Files:**
- Create: `website/package.json`, `website/nuxt.config.ts`, `website/tsconfig.json`, `website/eslint.config.ts`, `website/.oxlintrc.json`, `website/scripts/lint.mjs`, `website/app/app.vue`, `website/app/assets/css/main.css`, `website/.gitignore`
- Modify: `.gitignore` (add `website/.nuxt`, `website/.output`, `website/node_modules`)

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable Nuxt app at `website/`; `npm run lint`, `npm run typecheck`, `npm run build` all defined and green.

- [ ] **Step 1: Create the package manifest**

`website/package.json`:

```json
{
  "name": "maran-website",
  "version": "0.1.0",
  "private": true,
  "license": "BUSL-1.1",
  "type": "module",
  "scripts": {
    "dev": "nuxt dev",
    "build": "nuxt build",
    "preview": "nuxt preview",
    "postinstall": "nuxt prepare",
    "typecheck": "nuxt typecheck",
    "lint": "node scripts/lint.mjs",
    "lint:fix": "oxlint . --fix && eslint . --fix",
    "lint:content": "node scripts/check-content.mjs",
    "test": "vitest run",
    "test:watch": "vitest"
  },
  "dependencies": {
    "nuxt": "4.2.1",
    "vue": "3.5.42",
    "vue-router": "4.6.3"
  },
  "devDependencies": {
    "@tailwindcss/vite": "4.3.3",
    "@types/node": "26.4.0",
    "eslint": "10.9.1",
    "eslint-config-prettier": "10.1.8",
    "eslint-plugin-import-x": "4.17.1",
    "eslint-plugin-jsdoc": "64.2.1",
    "eslint-plugin-vue": "10.10.0",
    "eslint-plugin-vuejs-accessibility": "2.6.0",
    "oxlint": "1.80.0",
    "tailwindcss": "4.3.3",
    "typescript": "5.9.3",
    "typescript-eslint": "8.46.0",
    "vitest": "5.0.1",
    "@nuxt/test-utils": "4.3.2",
    "happy-dom": "20.14.5",
    "vue-tsc": "3.2.0"
  }
}
```

Resolve each version against the registry as you install (`npm install` without `--save` flags, then pin what landed); the numbers above are the floor, and any bump must stay on the same major.

- [ ] **Step 2: Install and verify the skeleton boots**

```bash
cd website && npm install && npx nuxt prepare
```

Expected: `.nuxt/` generated, no peer-dependency errors.

- [ ] **Step 3: Write the Nuxt config**

`website/nuxt.config.ts` — SSR on, Node preset, Tailwind through the Vite plugin, site origin for later SEO use:

```ts
import tailwindcss from '@tailwindcss/vite'
import { defineNuxtConfig } from 'nuxt/config'

/**
 * Nuxt configuration for the Maran public website.
 *
 * Server-side rendered on a Node runtime: the site is crawled, and the
 * marketing and documentation pages must arrive as HTML rather than as an
 * empty shell a crawler has to execute. Tailwind is wired through its Vite
 * plugin, the same way the panel does it, so one token vocabulary covers
 * both front ends.
 */
export default defineNuxtConfig({
  compatibilityDate: '2026-09-19',
  ssr: true,
  devtools: { enabled: true },
  css: ['~/assets/css/main.css'],
  vite: { plugins: [tailwindcss()] },
  nitro: { preset: 'node-server' },
  typescript: { strict: true, typeCheck: false },
})
```

- [ ] **Step 4: Copy the design tokens**

Copy the `@theme` / custom-property block from `frontend/src/assets/css/main.css` into `website/app/assets/css/main.css`, keeping `@import "tailwindcss";` at the top. Drop any panel-only rules (layout shells, scrollbar tweaks for the admin chrome). Add a file-top comment stating the tokens are a copy and which file they came from.

- [ ] **Step 5: Write the root component**

`website/app/app.vue`:

```vue
<script setup lang="ts">
/**
 * Root component of the public website.
 *
 * Renders the active layout and nothing else — every page picks its own
 * layout (`DefaultLayout` or `DocsLayout`), so the root stays empty of
 * chrome.
 */
</script>

<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
</template>
```

- [ ] **Step 6: Port the lint configuration**

Copy `frontend/eslint.config.ts` to `website/eslint.config.ts` and adapt:

- ignores become `['**/.nuxt/**', '**/.output/**', '**/node_modules/**', '**/dist/**']`;
- the UI-kit override path becomes `app/components/ui/**`;
- delete the `useApi`/`composables/apis` `no-restricted-imports` block — the site has no API layer — and keep `no-restricted-globals` for `fetch`/`XMLHttpRequest` off entirely, since `$fetch` and `useAsyncData` are the framework's own data path;
- keep `jsdoc/require-jsdoc` with `publicOnly: false` at `error`, with `**/*.spec.ts` exempt;
- add a block turning `vue/no-bare-strings-in-template` off for `app/components/marketing/**` and `app/components/docs/**` (spec §4);
- rewrite the config's file-top doc comment so it describes this site, not the panel.

Copy `frontend/scripts/lint.mjs` to `website/scripts/lint.mjs`, adjusting the sub-check list to: oxlint, ESLint, `check-content.mjs`. `check-content.mjs` does not exist until Task 7 — until then the runner must report it as skipped-with-reason rather than crash, so guard it with `existsSync` exactly as the panel's runner guards its binaries.

- [ ] **Step 7: Run the gate**

```bash
cd website && npm run lint && npm run typecheck && npm run build
```

Expected: all three exit 0. Fix anything they report before moving on — a JSDoc or return-type finding here is a real finding.

- [ ] **Step 8: Stage and report**

```bash
git add website/ .gitignore
```

Then stop: report what was added and wait for the owner before any commit.

---

### Task 2: Content collections and the three-locale corpus skeleton

**Files:**
- Create: `website/content.config.ts`, `website/content/en/docs/index.md`, `website/content/en/docs/installation.md`, `website/content/ru/docs/index.md`, `website/content/hy/docs/index.md`, `website/content/en/releases/0.1.0.md`, `website/content/en/blog/hello-maran.md`, `website/content/en/platforms.md`
- Modify: `website/package.json` (add `@nuxt/content`), `website/nuxt.config.ts` (register the module)

**Interfaces:**
- Consumes: Task 1's Nuxt app.
- Produces: collections named `docsEn`, `docsRu`, `docsHy`, `blogEn`, `blogRu`, `blogHy`, `releasesEn`, `releasesRu`, `releasesHy`, `platformsEn`, `platformsRu`, `platformsHy`; the docs schema `{ title: string, description: string, order: number, section: string }`, the blog schema `{ title, description, date, author }`, the release schema `{ version, date, channel }` with `channel` one of `'stable' | 'beta'`.

- [ ] **Step 1: Install the content module**

```bash
cd website && npm install @nuxt/content
```

Pin the resolved version in `package.json` and add `'@nuxt/content'` to `modules` in `nuxt.config.ts`.

- [ ] **Step 2: Declare the collections**

`website/content.config.ts`:

```ts
import { defineCollection, defineContentConfig, z } from '@nuxt/content'

/** Locales the site publishes content in; `en` is the default and the fallback source. */
const LOCALES = ['en', 'ru', 'hy'] as const

/** Frontmatter every documentation page carries. */
const docsSchema = z.object({
  title: z.string(),
  description: z.string(),
  section: z.string(),
  order: z.number(),
})

/** Frontmatter every blog post carries. */
const blogSchema = z.object({
  title: z.string(),
  description: z.string(),
  date: z.string(),
  author: z.string(),
})

/** Frontmatter every release note carries. */
const releaseSchema = z.object({
  version: z.string(),
  date: z.string(),
  channel: z.enum(['stable', 'beta']),
  title: z.string(),
  description: z.string(),
})

/**
 * Builds the nine locale-scoped page collections plus the three single-page
 * platform collections.
 *
 * One collection per locale rather than one collection with a `locale`
 * field: the content module's source globs are what make a locale's absence
 * detectable, and the fallback in `useLocalisedContent` relies on querying
 * a locale's collection and getting nothing back.
 *
 * @returns The collection map `defineContentConfig` expects.
 */
const buildCollections = (): Record<string, ReturnType<typeof defineCollection>> => {
  const collections: Record<string, ReturnType<typeof defineCollection>> = {}
  for (const locale of LOCALES) {
    const suffix = locale.charAt(0).toUpperCase() + locale.slice(1)
    collections[`docs${suffix}`] = defineCollection({
      type: 'page',
      source: `${locale}/docs/**/*.md`,
      schema: docsSchema,
    })
    collections[`blog${suffix}`] = defineCollection({
      type: 'page',
      source: `${locale}/blog/**/*.md`,
      schema: blogSchema,
    })
    collections[`releases${suffix}`] = defineCollection({
      type: 'page',
      source: `${locale}/releases/**/*.md`,
      schema: releaseSchema,
    })
    collections[`platforms${suffix}`] = defineCollection({
      type: 'page',
      source: `${locale}/platforms.md`,
    })
  }
  return collections
}

export default defineContentConfig({ collections: buildCollections() })
```

- [ ] **Step 3: Write the seed content**

`website/content/en/docs/index.md`:

```markdown
---
title: Introduction
description: What Maran is, what it manages, and what it expects of the server it runs on.
section: Getting started
order: 1
---

# Introduction

Maran manages a Linux server the way an administrator would: hosting accounts
are real system users, disk quotas are real quotas, and every privileged action
goes through a typed contract rather than a shell string.
```

`website/content/en/docs/installation.md` (`section: Getting started`, `order: 2`) covers the installer command and the first-run token. `website/content/ru/docs/index.md` and `website/content/hy/docs/index.md` carry translations of the introduction with identical frontmatter keys. `website/content/en/releases/0.1.0.md` uses `version: "0.1.0"`, `channel: stable` and the current date, summarising the `Unreleased` section of the root `CHANGELOG.md` as operator-facing prose. `website/content/en/blog/hello-maran.md` is one short launch post. `website/content/en/platforms.md` holds the support matrix as a Markdown table with columns Distribution, Version, Status, Supported until.

- [ ] **Step 4: Verify the collections build**

```bash
cd website && npm run build
```

Expected: build succeeds and the content SQLite index is generated; a frontmatter key that does not match its schema fails the build — confirm that by temporarily removing `order:` from `content/en/docs/index.md`, re-running, seeing it fail, and restoring the key.

- [ ] **Step 5: Stage and report**

```bash
git add website/
```

---

### Task 3: Locale routing

**Files:**
- Create: `website/i18n/locales/en.json`, `website/i18n/locales/ru.json`, `website/i18n/locales/hy.json`, `website/app/components/ui/UiLocaleSwitch.vue`
- Modify: `website/package.json`, `website/nuxt.config.ts`

**Interfaces:**
- Consumes: Task 1's app.
- Produces: `useI18n()` and `useLocalePath()` available site-wide; locale codes `'en' | 'ru' | 'hy'`; message keys `nav.docs`, `nav.blog`, `nav.releases`, `nav.pricing`, `nav.features`, `nav.platforms`, `search.open`, `search.placeholder`, `search.empty`, `locale.label`, `fallback.notice`, `footer.rights`.

- [ ] **Step 1: Install and register the module**

```bash
cd website && npm install @nuxtjs/i18n
```

Add to `nuxt.config.ts`:

```ts
  i18n: {
    locales: [
      { code: 'en', language: 'en-US', name: 'English', file: 'en.json' },
      { code: 'ru', language: 'ru-RU', name: 'Русский', file: 'ru.json' },
      { code: 'hy', language: 'hy-AM', name: 'Հայերեն', file: 'hy.json' },
    ],
    defaultLocale: 'en',
    strategy: 'prefix_except_default',
    detectBrowserLanguage: {
      useCookie: true,
      cookieKey: 'maran_locale',
      redirectOn: 'no prefix',
      alwaysRedirect: false,
      fallbackLocale: 'en',
    },
  },
```

`redirectOn: 'no prefix'` with `alwaysRedirect: false` is the spec's rule (§5): a visitor who arrived on an explicit `/ru/...` URL is never bounced, and neither is a crawler following a shared link.

- [ ] **Step 2: Write the message files**

`website/i18n/locales/en.json`:

```json
{
  "nav": {
    "features": "Features",
    "pricing": "Pricing",
    "docs": "Documentation",
    "blog": "Blog",
    "releases": "Releases",
    "platforms": "Supported platforms"
  },
  "search": {
    "open": "Search the documentation",
    "placeholder": "Search the documentation",
    "empty": "Nothing matches that search."
  },
  "locale": { "label": "Language" },
  "fallback": { "notice": "This page is not translated yet and is shown in English." },
  "footer": { "rights": "Innovayse" }
}
```

`ru.json` and `hy.json` carry the same key tree, fully translated. Every key must exist in all three files — Task 7's content check enforces it.

- [ ] **Step 3: Build the locale switch**

`website/app/components/ui/UiLocaleSwitch.vue` — a `<select>` (permitted here: this is the site's own UI kit) listing `locales`, bound to the active locale, navigating with `switchLocalePath(code)` on change. The control carries a translated `aria-label` from `locale.label`; the label is a required piece of the markup, not an optional nicety (rules/vue.md). JSDoc on the component block and on the change handler.

- [ ] **Step 4: Verify**

```bash
cd website && npm run lint && npm run typecheck && npm run build
```

Expected: all exit 0.

- [ ] **Step 5: Stage and report**

```bash
git add website/
```

---

### Task 4: Layouts, chrome and the site's UI kit

**Files:**
- Create: `website/app/layouts/DefaultLayout.vue`, `website/app/layouts/DocsLayout.vue`, `website/app/components/ui/UiButton.vue`, `website/app/components/ui/UiLink.vue`, `website/app/components/ui/UiBadge.vue`, `website/app/components/ui/UiCard.vue`, `website/app/components/marketing/SiteHeader.vue`, `website/app/components/marketing/SiteFooter.vue`
- Modify: `website/package.json`, `website/nuxt.config.ts` (icon module)

**Interfaces:**
- Consumes: Task 3's i18n keys.
- Produces: `UiButton` props `{ variant: 'primary' | 'secondary', to?: string, label: string }`; `UiLink` props `{ to: string, label: string, external?: boolean }`; `UiBadge` props `{ label: string, tone: 'neutral' | 'accent' }`; `UiCard` slots `header`, `default`; layouts `default` and `docs`.

- [ ] **Step 1: Install icons**

```bash
cd website && npm install @nuxt/icon @iconify-json/lucide
```

Register `'@nuxt/icon'` in `modules`. Configure `icon: { mode: 'svg', clientBundle: { scan: true } }` so icons are inlined at build time rather than fetched from a network endpoint at runtime.

- [ ] **Step 2: Write the kit**

Four small components, each one file, each with a `<script setup lang="ts">` JSDoc block, typed `defineProps` with a documented interface, and stock Tailwind text steps only. `UiButton` renders `<NuxtLink>` when `to` is given and `<button>` otherwise; `label` is a required prop, never a default (rules/vue.md: a default is how a label silently stays English).

- [ ] **Step 3: Write the header and footer**

`SiteHeader.vue`: the Maran wordmark linking to `localePath('/')`, navigation built from a documented module-level constant array of `{ key, path }` pairs rendered through `t()` and `localePath()`, `UiLocaleSwitch`, and a docs-search trigger button that emits `open-search` (wired in Task 6). `SiteFooter.vue`: legal links, the copyright line from `footer.rights`, and the current year computed from `new Date()` at render.

- [ ] **Step 4: Write the layouts**

`DefaultLayout.vue`: header, `<slot />` inside a centred container, footer. `DocsLayout.vue`: header, a two-column grid — sidebar slot on the left, article on the right with a table-of-contents column at `lg` and up — then the footer. Both set `lang` via `useHead({ htmlAttrs: { lang: locale.value } })`.

- [ ] **Step 5: Verify**

```bash
cd website && npm run lint && npm run typecheck && npm run build && npm run dev
```

Open `http://localhost:3000`, confirm the header renders and the locale switch moves between `/`, `/ru` and `/hy`. Stop the dev server.

- [ ] **Step 6: Stage and report**

```bash
git add website/
```

---

### Task 5: Marketing pages

**Files:**
- Create: `website/app/pages/index.vue`, `website/app/pages/features.vue`, `website/app/pages/pricing.vue`, `website/app/pages/platforms.vue`, `website/app/components/marketing/MarketingHero.vue`, `website/app/components/marketing/FeatureGrid.vue`, `website/app/components/marketing/PricingTable.vue`, `website/app/components/marketing/CallToAction.vue`, `website/app/types/marketing.ts`
- Modify: `website/i18n/locales/{en,ru,hy}.json` (page copy keys), `website/app/pages/platforms.vue` reads the `platforms<Locale>` collection from Task 2

**Interfaces:**
- Consumes: Task 4's kit and layouts; Task 2's `platformsEn|Ru|Hy` collections.
- Produces: `website/app/types/marketing.ts` exporting `interface FeatureItem { icon: string; titleKey: string; bodyKey: string }` and `interface PricingTier { nameKey: string; priceKey: string; featureKeys: string[]; highlighted: boolean }`.

- [ ] **Step 1: Define the marketing types**

`website/app/types/marketing.ts` with both interfaces above, JSDoc on each interface and each field.

- [ ] **Step 2: Build the sections**

`MarketingHero.vue` (headline, subhead, two `UiButton`s — install and docs), `FeatureGrid.vue` (takes `items: FeatureItem[]`, renders `UiCard` per item with an `Icon` and translated text), `PricingTable.vue` (takes `tiers: PricingTier[]`), `CallToAction.vue`. Copy comes from i18n keys under `home.*`, `features.*` and `pricing.*`, added to all three locale files. Feature and tier arrays are documented module-level constants in the page that owns them.

- [ ] **Step 3: Build the pages**

Each page sets `definePageMeta({ layout: 'default' })` and its own `useSeoMeta({ title, description })` from translated keys. `platforms.vue` queries `platforms<Suffix>` for the active locale via the composable from Task 6 and renders the Markdown through `<ContentRenderer>`.

- [ ] **Step 4: Verify**

```bash
cd website && npm run lint && npm run typecheck && npm run build
```

Then `npm run dev` and confirm `/`, `/features`, `/pricing`, `/platforms` render in all three locales.

- [ ] **Step 5: Stage and report**

```bash
git add website/
```

---

### Task 6: Docs, blog and releases — routes, fallback and search

**Files:**
- Create: `website/app/composables/useLocalisedContent.ts`, `website/app/composables/useDocsNavigation.ts`, `website/app/pages/docs/[...slug].vue`, `website/app/pages/blog/index.vue`, `website/app/pages/blog/[slug].vue`, `website/app/pages/releases/index.vue`, `website/app/pages/releases/[version].vue`, `website/app/components/docs/DocsSidebar.vue`, `website/app/components/docs/DocsToc.vue`, `website/app/components/docs/DocsSearchModal.vue`, `website/app/components/docs/FallbackNotice.vue`, `website/app/error.vue`
- Modify: `website/app/components/marketing/SiteHeader.vue` (wire the search trigger)

**Interfaces:**
- Consumes: Task 2's collections, Task 4's layouts.
- Produces:
  - `useLocalisedContent` — `(kind: 'docs' | 'blog' | 'releases' | 'platforms', path: string) => Promise<{ doc: ParsedContent | null; isFallback: boolean }>`; queries the active locale's collection, and when it returns nothing and the locale is not `en`, re-queries the English collection and reports `isFallback: true`.
  - `useDocsNavigation` — `() => Promise<DocsNavSection[]>` where `interface DocsNavSection { section: string; items: { title: string; path: string; order: number }[] }`, sorted by `order`.

- [ ] **Step 1: Write the composables**

One export per file, named after the file. `useLocalisedContent` is the single place the fallback rule lives — no page re-implements it. JSDoc on the composable and on every function it returns, `@param` per parameter, `@returns` on both.

- [ ] **Step 2: Build the docs route**

`docs/[...slug].vue`: `definePageMeta({ layout: 'docs' })`, resolve the slug, call `useLocalisedContent('docs', slug)`. When `doc` is `null`, `throw createError({ statusCode: 404 })`. When `isFallback` is true, render `FallbackNotice` above the article and add `useSeoMeta({ robots: 'noindex, follow' })` — spec §5. Sidebar from `useDocsNavigation`, table of contents from the document's own `body.toc`.

- [ ] **Step 3: Build the blog and release routes**

`blog/index.vue` lists posts newest first; `blog/[slug].vue` mirrors the docs page's fallback handling. `releases/index.vue` lists releases by descending `version` with a `UiBadge` carrying the `channel`; `releases/[version].vue` renders one release note.

- [ ] **Step 4: Build search**

`DocsSearchModal.vue`: fetches the section index once with `queryCollectionSearchSections` for the active locale, filters in memory on the query, renders results as links, closes on `Escape` and on navigation. Opens from the header trigger and from `Cmd/Ctrl+K` bound in `onMounted` and released in `onUnmounted`. The modal traps focus and returns it to the trigger on close.

- [ ] **Step 5: Build the error page**

`website/app/error.vue` renders a translated 404 (and a generic message for other status codes) inside `DefaultLayout`, with a link home. Keys `error.notFound.title`, `error.notFound.body`, `error.generic.title` added to all three locale files.

- [ ] **Step 6: Verify**

```bash
cd website && npm run lint && npm run typecheck && npm run build
```

Then `npm run dev` and check by hand: `/docs/installation`, `/ru/docs/installation` (English body plus the notice, since no `ru` translation exists yet), `/blog`, `/releases`, `/releases/0.1.0`, an unknown path, and ⌘K search.

- [ ] **Step 7: Stage and report**

```bash
git add website/
```

---

### Task 7: The content check

**Files:**
- Create: `website/scripts/check-content.mjs`
- Modify: `website/scripts/lint.mjs` (remove the existsSync guard's skip path now that the script exists)

**Interfaces:**
- Consumes: Task 2's content tree, Task 3's locale files.
- Produces: `npm run lint:content`, exiting non-zero with a per-failure report.

- [ ] **Step 1: Write the checks**

Four, each collecting all failures before reporting (never stopping at the first — same reasoning as `frontend/scripts/lint.mjs`):

1. **Release coverage.** Parse `## <version>` headings out of the root `CHANGELOG.md`, ignoring `Unreleased`; every version must have `website/content/en/releases/<version>.md`.
2. **Frontmatter.** Every `.md` under `content/` carries the keys its collection's schema requires, with `channel` in `{stable, beta}` and `date` parseable as an ISO date.
3. **Internal links.** Every Markdown link starting `/` resolves to a page file under `website/app/pages/` or a content file under the same locale.
4. **Locale key parity.** `en.json`, `ru.json` and `hy.json` have identical key trees; report keys missing on either side, in both directions.

Output is a printed verdict line per check plus a collected total, the way the panel's runner reports — an exit code alone hides which check ran.

- [ ] **Step 2: Prove each check catches its violation**

For each of the four: introduce the violation, run `npm run lint:content`, see it fail naming that file, revert. A check that has never rejected anything is not known to work.

- [ ] **Step 3: Verify the gate**

```bash
cd website && npm run lint
```

Expected: four sub-checks reported, exit 0 on a clean tree.

- [ ] **Step 4: Stage and report**

```bash
git add website/
```

---

### Task 8: SEO — canonical, hreflang, sitemap, robots, OG images, JSON-LD

**Files:**
- Modify: `website/package.json`, `website/nuxt.config.ts`, `website/app/pages/*.vue`, `website/app/pages/docs/[...slug].vue`, `website/app/pages/blog/[slug].vue`, `website/app/pages/releases/[version].vue`
- Create: `website/app/composables/useStructuredData.ts`, `website/app/components/OgImageDefault.vue`

**Interfaces:**
- Consumes: every page from Tasks 5 and 6.
- Produces: `useStructuredData` — `(kind: 'software' | 'article' | 'post', input: StructuredDataInput) => void`, where `interface StructuredDataInput { title: string; description: string; path: string; datePublished?: string }`; it injects the JSON-LD graph plus a `BreadcrumbList` built from the path segments.

- [ ] **Step 1: Install the SEO module**

```bash
cd website && npm install @nuxtjs/seo
```

Configure:

```ts
  site: {
    url: 'https://maran.innovayse.com',
    name: 'Maran',
    defaultLocale: 'en',
    indexable: process.env.MARAN_SITE_ENV === 'production',
  },
```

`indexable: false` makes `robots.txt` a blanket disallow and adds `noindex` headers — spec §6's staging guarantee, driven by `MARAN_SITE_ENV`, which the Dockerfile and CI both set explicitly.

- [ ] **Step 2: Wire canonical and hreflang**

`@nuxtjs/i18n` emits `hreflang` alternates for all three locales once `baseUrl` is the site URL; add `x-default` pointing at the English URL through `useHead`'s `link` array in `app.vue`. Confirm by viewing source on `/ru/docs/installation` that four alternate links are present.

- [ ] **Step 3: Write the structured-data composable**

One file, one export, JSDoc on the composable and on every helper it defines. `software` emits `SoftwareApplication` (name, applicationCategory, operatingSystem, offers omitted), `article` emits `TechArticle`, `post` emits `BlogPosting` with `datePublished`. Every kind also emits `BreadcrumbList`.

- [ ] **Step 4: Call it from each page**

Home → `software`. Docs → `article`. Blog post → `post`. Releases and marketing pages → breadcrumbs via `article` with the release/page title.

- [ ] **Step 5: OG images**

`OgImageDefault.vue` — a component rendering the page title, the section label and the Maran wordmark on the brand background. Pages call `defineOgImageComponent('Default', { title, section })`.

- [ ] **Step 6: Verify**

```bash
cd website && npm run build && npm run preview
```

Fetch `/sitemap.xml` (all three locales present), `/robots.txt` (disallow-all without `MARAN_SITE_ENV=production`, allow with it), and `/__og-image__/image/og.png` for one page.

- [ ] **Step 7: Stage and report**

```bash
git add website/
```

---

### Task 9: Container, CI job and the `maran` command

**Files:**
- Create: `website/Dockerfile`, `website/.dockerignore`, `.github/workflows/website.yml`
- Modify: `docker/` compose file (add a `website` service), `scripts/` (register the `maran website` check — follow the existing command registration; never call `scripts/lib/*` directly), `README.md` (one line pointing at `website/`)

**Interfaces:**
- Consumes: Tasks 1–8.
- Produces: `maran website` running lint, typecheck and build; a `website` CI job; an image serving `.output/server/index.mjs` on port 3000.

- [ ] **Step 1: Write the Dockerfile**

Multi-stage on `node:lts-alpine`: a build stage running `npm ci && npm run build`, a runtime stage copying only `.output`, running as a non-root user, `ENV NODE_ENV=production MARAN_SITE_ENV=production`, `EXPOSE 3000`, `CMD ["node", ".output/server/index.mjs"]`. `.dockerignore` excludes `node_modules`, `.nuxt`, `.output`, `e2e`, `test-results`.

- [ ] **Step 2: Add the dev compose service**

Mirror how `docker/` defines the existing services: build from `website/`, map 3000, mount the source for live reload in the dev profile.

- [ ] **Step 3: Write the CI job**

`.github/workflows/website.yml` modelled on `.github/workflows/frontend.yml`: trigger on changes under `website/`, Node from the repo's pinned version, `npm ci`, then `npm run lint`, `npm run typecheck`, `npm run build`, and `npm test`. Include the test step now; Task 10 is what fills the suite.

- [ ] **Step 4: Register the `maran` command**

Add `website` to the CLI the documented way, running the same three commands. Confirm with `source scripts/dev && maran website`.

- [ ] **Step 5: Verify**

```bash
docker build -t maran-website:dev website/
docker run --rm -p 3000:3000 maran-website:dev
```

Fetch `/` and confirm server-rendered HTML contains the headline text. Stop the container.

- [ ] **Step 6: Stage and report**

```bash
git add website/ .github/workflows/website.yml docker/ scripts/ README.md
```

---

### Task 10: The test pass

**Files:**
- Create: `website/tests/unit/localised-content.spec.ts`, `website/tests/unit/docs-navigation.spec.ts`, `website/tests/unit/structured-data.spec.ts`, `website/tests/unit/locale-keys.spec.ts`, `website/tests/components/docs-page.spec.ts`
- Modify: nothing — `vitest.config.ts` and the runner land in Task 1

**Interfaces:**
- Consumes: the whole site.
- Produces: `npm test` green.

- [ ] **Step 1: Write the content-resolution specs**

`tests/unit/localised-content.spec.ts` — test names are behaviour sentences; these files are the one
place exempt from JSDoc.

```ts
import { describe, expect, it } from 'vitest'
import { resolveLocalisedDocument } from '~/utils/resolveLocalisedDocument'

describe('resolving a localised document', () => {
  it('returns the requested locale when that page exists', async () => {
    const result = await resolveLocalisedDocument({
      locale: 'ru',
      queryLocale: async (locale) => (locale === 'ru' ? { title: 'Установка' } : null),
    })
    expect(result).toEqual({ doc: { title: 'Установка' }, isFallback: false })
  })

  it('falls back to English and reports the fallback when the translation is missing', async () => {
    const result = await resolveLocalisedDocument({
      locale: 'hy',
      queryLocale: async (locale) => (locale === 'en' ? { title: 'Installation' } : null),
    })
    expect(result).toEqual({ doc: { title: 'Installation' }, isFallback: true })
  })

  it('returns no document when the page is missing in English too', async () => {
    const result = await resolveLocalisedDocument({ locale: 'ru', queryLocale: async () => null })
    expect(result).toEqual({ doc: null, isFallback: false })
  })
})
```

The pure resolver lives in `app/utils/resolveLocalisedDocument.ts` and takes its query as a
parameter; `useLocalisedContent` (Task 6) supplies the real `queryCollection` call. That split is
what makes the fallback rule testable without booting the content database — extract it in this
task if Task 6 left the logic inside the composable.

- [ ] **Step 2: Write the navigation and structured-data specs**

`tests/unit/docs-navigation.spec.ts` asserts that documentation pages group by `section` and sort by
`order` inside each group, and that a page with a duplicate `order` keeps a stable title-based
tiebreak. `tests/unit/structured-data.spec.ts` asserts each kind: `software` produces
`@type: 'SoftwareApplication'`, `article` produces `TechArticle`, `post` produces `BlogPosting` with
its `datePublished`, and every kind carries a `BreadcrumbList` whose `itemListElement` matches the
path segments in order.

- [ ] **Step 3: Write the locale-parity spec**

`tests/unit/locale-keys.spec.ts` reads the three files from `i18n/locales/`, flattens each to a key
list, and asserts the three lists are identical — reporting the differing keys in both directions
when they are not.

```ts
import { describe, expect, it } from 'vitest'
import en from '../../i18n/locales/en.json'
import hy from '../../i18n/locales/hy.json'
import ru from '../../i18n/locales/ru.json'
import { flattenKeys } from '~/utils/flattenKeys'

describe('the locale files', () => {
  it('hold identical key trees', () => {
    const keys = flattenKeys(en)
    expect(flattenKeys(ru)).toEqual(keys)
    expect(flattenKeys(hy)).toEqual(keys)
  })
})
```

This duplicates the lint-gate check from Task 7 on purpose: the lint check fails the branch, the
test names the missing key in the runner an engineer already has open. If that feels redundant after
Task 7, keep the test and delete the duplicated part of the script — one gate, not none.

- [ ] **Step 4: Write the component spec**

`tests/components/docs-page.spec.ts` starts with `// @vitest-environment nuxt` and mounts the
documentation page with a stubbed document: it asserts the article renders its `<h1>`, that a
fallback document renders the notice, and that the rendered head contains a canonical link, three
`hreflang` alternates and `x-default`.

- [ ] **Step 5: Run the suite**

```bash
cd website && npm test
```

Expected: every spec passes. A failure here is a real defect in Tasks 1–9 — fix the site, not the
assertion.

- [ ] **Step 6: Run the full gate once more**

```bash
cd website && npm run lint && npm run typecheck && npm run build && npm test
```

Report the actual output of each command. Do not claim the site is done without those four results
in hand (rules/testing.md).

- [ ] **Step 7: Stage and report**

```bash
git add website/ .github/workflows/website.yml
```

Then stop and hand the branch to the owner for the commit decision.

---

## Self-Review

- **Spec coverage:** §1 → Task 1; §2 → Tasks 1–3, 5, 8; §3 → Tasks 1–6; §4 → Task 1 Step 6; §5 → Tasks 3 and 6; §6 → Task 8; §7 → Tasks 2 and 7; §8 → Task 9; §9 → Task 10; §10 is explicitly out of scope and has no task.
- **Placeholders:** none — every code step carries the code, every check step carries the command and its expected output.
- **Type consistency:** `useLocalisedContent`, `useDocsNavigation`, `useStructuredData`, `FeatureItem`, `PricingTier`, `DocsNavSection`, `StructuredDataInput` are each defined once in the task that creates them and referenced under the same names afterwards. Collection names follow one rule (`<kind><LocaleSuffix>`) across Tasks 2, 5 and 6.
