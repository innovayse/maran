# Vue / TypeScript Rules (frontend)

Normative. Enforced by ESLint + oxlint + `vue-tsc` on CI.

## Lint enforces these rules — a rule the linter can catch MUST be a lint rule

Reviewers are the last line, not the first. Every law below that a linter can express is configured in `frontend/eslint.config.ts` as **error** severity, and `npm run lint` is a merge gate. `--fix` may be run, but suppressions (`eslint-disable`) require an inline reason comment and are treated as findings in review.

Required configuration, at minimum:

- `eslint-plugin-vue` (flat, `vue3-recommended`) + `typescript-eslint` (`recommended-type-checked`, with the project's tsconfig wired for type-aware rules) + `eslint-plugin-jsdoc` + `eslint-config-prettier` last.
- **Arrow-only functions:** `func-style: ["error", "expression"]` plus
  `no-restricted-syntax: ["error", {selector: "FunctionDeclaration", message: "Use a const arrow function (rules/vue.md)"}, {selector: "FunctionExpression", message: "Use an arrow function (rules/vue.md)"}]`.
- **No raw HTML:** `vue/no-v-html: "error"`, and `no-restricted-properties` / `no-restricted-syntax` blocking `innerHTML` and `outerHTML` assignment.
- **UI kit only:** `vue/no-restricted-html-elements` set to error for `button`, `input`, `select`, `textarea`, `table`, `form` — configured to apply everywhere EXCEPT `src/components/ui/**`, which overrides it off.
- **APIs only in stores:** `no-restricted-imports` with patterns `**/composables/useApi*` and `**/composables/apis/*` erroring across **all of `src/**`** (not just `.vue` — a util or another composable must not reach the API layer either), lifted only in `src/stores/**`, `src/composables/apis/**` and `src/composables/useApi.ts`. Plus `no-restricted-globals` for `fetch`/`XMLHttpRequest`, and a `no-restricted-syntax` selector blocking `window.fetch`, `globalThis.fetch` and `self.fetch`, which the globals rule does not catch.
- **Docs:** `jsdoc/require-jsdoc` (error) covering arrow functions, function expressions, methods and class declarations with `publicOnly: false`; `jsdoc/require-param`, `jsdoc/require-returns`, `jsdoc/check-param-names`, `jsdoc/require-description` — all error. Test files (`**/*.spec.ts`) override these off.
- **Type hygiene:** `@typescript-eslint/no-explicit-any`, `no-non-null-assertion`, `consistent-type-imports` (enforcing `import type`), `explicit-function-return-type` (error, `allowExpressions: false`) — plus `@typescript-eslint/no-floating-promises` and `no-misused-promises` from the type-checked preset.
- **i18n:** `vue/no-bare-strings-in-template: "error"` so a hardcoded user-visible string fails the build rather than review.
- **Cleanliness:** `no-console` (error, `allow: []`), `no-debugger`, `eqeqeq`, `no-unused-vars` via `@typescript-eslint/no-unused-vars` with `argsIgnorePattern: "^_"`, `vue/multi-word-component-names`, `vue/component-api-style: ["error", ["script-setup"]]`.

`oxlint` runs first as a fast pre-pass; ESLint is the authority. Neither may be skipped in CI, and `npm run lint` must exit non-zero on any error.

## Structure

- One SPA, two zones (admin, customer cabinet). The layout is FLAT — there is no `src/modules/`:
  - `components/ui/Ui*.vue` — the UI kit, the only home of raw HTML controls.
  - `components/<feature>/` — feature components, kebab-case folder per backend module (`audit-logs`, `sites`, `databases`…), files PascalCase (`SiteCard.vue`). Page shells live in `layouts/`, never in `components/layout/`.
  - `pages/` — routed pages named `<X>Page.vue`; feature pages in kebab-case folders. Naming patterns: `<X>sListPage.vue`, `<X>DetailPage.vue`, `<X>FormPage.vue`, tab panels `<X>Tab.vue`; simple top-level pages flat (`LoginPage.vue`, `DashboardPage.vue`, `NotFoundPage.vue`).
  - `layouts/<Name>Layout.vue` — page shells (`DefaultLayout`, `AuthLayout`).
  - `composables/useApi.ts`, `composables/apis/use<Feature>Api.ts`, other `composables/use<X>.ts` — flat, one per file.
  - `stores/<feature>.ts` — flat, camelCase (`auditLogs.ts`, `sites.ts`), one Pinia setup store per file.
  - `types/<domain>.ts` — flat, camelCase, one domain per file, named after it.
  - `utils/<purpose>.ts` — pure helpers, one purpose per file.
  - `assets/css/main.css` (+ tokens), `assets/icons/`, `locales/{en,ru,hy}/`, `router/index.ts`.
- A feature's files MUST NOT import from another feature's folders — shared things move down (`components/ui`, `composables`, `utils`, `types/common.ts`), same rule as the backend.

## Modules in the frontend: one bundle, licence-gated

Every module's interface — free and paid alike — is written in the flat structure above and compiled
into the single SPA bundle. There is no runtime loading of module JavaScript, no module federation,
and no per-module frontend build. Why this and not dynamic loading:

- **Security.** Injecting a downloaded bundle into the admin SPA would run third-party code inside
  the administrator's session, with its cookies and CSRF context — the exact thing our root agent
  refuses to do (rules/architecture.md). It also breaks the strict CSP that rules/security.md
  requires.
- **Enforcement lives on the server anyway.** The backend refuses a request for an unlicensed
  module regardless of what the browser holds, so shipping the UI buys no bypass — the panel is
  source-available, so hiding frontend code protects nothing.
- **Simplicity.** One build, one Vue/Pinia/i18n instance, one type graph, one lint pass.

What licence gating means in practice:

- The panel exposes the active module list (id, licence tier, enabled). A store holds it; the
  router uses it in a guard, and the navigation renders only enabled modules.
- A disabled module's routes resolve to the upgrade page, never to a blank screen or a 403 dump.
- The frontend gate is **cosmetic only** — it hides what the user cannot use. It is never the
  security boundary: the backend checks the licence on every request, independently.
- Feature code must not assume its module is enabled: any entry point (menu item, deep link, saved
  bookmark) passes through the same guard.

Third-party modules from outside Innovayse are out of scope for now. When they arrive, they get an
isolated page (own origin/iframe with a narrow postMessage contract), never an import into this
bundle — decided so nobody reaches for federation later "just this once".

## Types

- All types live in `src/types/`, **one domain per file**, the file named after the domain in camelCase: `account.ts` holds `Account`, `AccountStatus` and `CreateAccountRequest`, because they describe one thing from three angles — the entity, the state it can be in, and the shape that creates it. A type is never declared inline in a component, store or composable that isn't its home.
- This is deliberately NOT the backend's one-type-per-file law (rules/csharp.md, rules/rust.md), and the difference is not an oversight. A C# class or a Rust type carries behaviour, so splitting the file splits a responsibility; a TypeScript type is a shape, and `AccountStatus` means nothing apart from `Account.status`. Splitting shapes buys a longer file tree and an import line per field, and costs the thing that matters: a reader who opens `account.ts` sees the whole contract with the backend at once.
- A domain file gains a second domain only when a type genuinely belongs to both. Then the shared type moves to its own file — never duplicated into two.
- **A component's own types live in the component file**, not in `src/types/`. A badge's `variant` union, a select's option shape, a modal's size — these describe that component's props and mean nothing without it, so they are declared in its `<script setup>` and exported from there if a parent needs them. `src/types/` is for the domain and the API contract: what the backend sends and what the panel sends back. Putting a component's prop union there splits one component across two files and invites a second component to reuse a type that was never meant to be shared.
- Types are imported with `import type { … }` for a type-only module, and **inline** when the same module also provides values: `import { computed, type ComputedRef } from 'vue'`, never a second `import type` line for a module already imported. Two lines importing one module are noise a reader must reconcile, and they drift apart the moment someone edits only one. Enforced by `@typescript-eslint/consistent-type-imports` with `fixStyle: 'inline-type-imports'` plus `import-x/no-duplicates` with `prefer-inline` — `npm run lint:fix` rewrites most of them for you.
- No barrel `index.ts` re-exporting a folder — import from the file that owns the type.
- **Every accessible name exists, and every one of them is translated.** `alt`, `aria-label`, `title`, `placeholder`, a `<label>` for every control — these are user-visible strings that happen not to be visible, and both halves are enforced mechanically:
  - `vue/no-bare-strings-in-template` is configured with an `attributes` map, so a literal in `alt`, `title`, `aria-label` or `placeholder` fails the build exactly as a literal in the markup does. An English `aria-label` in a Russian interface is a screen-reader user being handed a language they did not choose — and it passes review every time, because nothing on screen looks wrong.
  - `eslint-plugin-vuejs-accessibility` checks the other half — that the attribute is there at all: `alt-text`, `form-control-has-label`, `label-has-for`, `anchor-has-content`, `heading-has-content`, `iframe-has-title`, `role-has-required-aria-props`, `aria-props`, `aria-role`, `mouse-events-have-key-events`, `no-autofocus`.
  - A decorative image or icon is the one exception, and it is declared rather than omitted: `alt=""` plus `aria-hidden="true"` on an inline SVG says "skip me" on purpose. An icon that carries meaning is labelled instead.
  - Kit components take the text as a **required prop** (`UiNav`'s `label`, `UiToast`'s `closeLabel`) rather than defaulting it: a default is how a label silently stays English forever.

- Every type, interface and enum carries JSDoc, and so does every non-obvious field.
- One component per file, `PascalCase.vue`, target < 250 lines. Views compose components; components stay dumb (props in, emits out).

## One unit per file & mandatory doc comments

- One component per `.vue` file, one store per `store.ts`, one composable per file (`useSiteLogs.ts` exports exactly `useSiteLogs`). No barrel files re-exporting half a module, no multi-purpose `utils.ts` — every file's name states its single content.
- **JSDoc is mandatory on ALL frontend code, without exception.** Every single one of these gets its own JSDoc block:
  - every component: a block at the top of `<script setup>` stating what the component is for;
  - every function and arrow-function const — exported, internal, handler, callback assigned to a name;
  - every composable, and every function it returns;
  - every Pinia store, and every state field, getter and action inside it;
  - every `props`/`emits` declaration (document each prop and each event);
  - every type, interface and enum, and every non-obvious field within them;
  - every module-level constant whose meaning is not literal.

  A `@param` line for each parameter and a `@returns` line whenever something is returned. Test files are the only exemption — a behavior-sentence test name is their documentation.

- **Code comments inside function bodies** are required wherever the code is not self-evident: a non-obvious branch, a workaround, an ordering requirement, a security-relevant check. Explain WHY, not what the next line does.

```ts
/**
 * Loads the panel's health status and stores it for the status screen.
 * Failures are captured into `error` rather than thrown: the shell must
 * render even when the API is unreachable.
 *
 * @returns Resolves once the request settled, successfully or not.
 */
const load = async (): Promise<void> => {
  loading.value = true
  try {
    status.value = await api.health()
    error.value = null
  } catch (cause) {
    // Health is advisory — a failure is a display state, never a thrown error.
    error.value = toApiError(cause)
  } finally {
    loading.value = false
  }
}
```

```ts
/**
 * Polls the agent-backed task until it reaches a terminal state.
 * Resolves with the final task; rejects on SSE disconnect after 3 retries.
 */
export const useTaskProgress = (taskId: string): Promise<TaskView> => { ... }
```

## Text sizes are Tailwind's own steps, and nothing else

Every font size in the SPA is a stock Tailwind step — `text-xs`, `text-sm`, `text-base`, `text-lg`,
`text-xl`, `text-2xl`. There is no custom scale, no size between two stock ones, and no size written
in a component:

```html
<!-- WRONG — the arbitrary-value escape hatch -->
<h2 class="text-[15px]">…</h2>

<!-- RIGHT -->
<h2 class="text-base">…</h2>
```

```css
/* WRONG — a size in scoped CSS */
.shell-header-picker { font-size: 12px; }

/* RIGHT, when a scoped rule genuinely has to restate the size */
.shell-header-picker { font-size: var(--text-sm); }
```

When the whole panel reads too small, the fix is to move components **up a stock step**, not to
redefine what a step means. A redefined scale is a second vocabulary only this repository
understands: `text-sm` would stop meaning what it means in every Tailwind project, in the docs, and
to the next person who reads the class. Sizes written in scoped CSS are worse again — they stay
behind when the components around them move, and sit a step off with nothing to say why.

**Which step:** `text-sm` is the body step — anything a person reads as a sentence, including muted
secondary lines. `text-xs` is for uppercase micro-labels, table headers, badges and key chips, never
for a phrase. Titles start at `text-lg` and go up.

`maran structure` rejects a literal `font-size` and the `text-[…]` form.

## Member order — functions come last

Inside a `<script setup>` block, a store, or a composable, declarations appear in this order, and a
file that mixes them is a review reject:

1. Imports
2. `defineProps` / `defineEmits` / `defineModel`
3. Module-level constants
4. Injected dependencies (`useI18n`, `useRouter`, the API composable in a store)
5. Reactive state (`ref`, `reactive`), then derived state (`computed`)
6. Functions — every `const` arrow function, including handlers
7. Lifecycle hooks and `watch`
8. A store's `return { ... }`

```ts
// RIGHT — state, then what changes it
const accounts: Ref<Account[]> = ref([])
const loading: Ref<boolean> = ref(false)

const load = async (): Promise<void> => { ... }

// WRONG — a ref declared after the function that uses it
const load = async (): Promise<void> => { ... }

const loading: Ref<boolean> = ref(false)   // rejected in review
```

Same reason as the backend rule (rules/csharp.md "Member order"): a reader opening a component or a
store wants to know what it holds before what it does. It also removes a real hazard —
`const` declarations are not hoisted, so a function placed above the state it closes over reads as
if the order were free when it is not.

## Every function is a `const` arrow function

- **`function` declarations are forbidden in frontend code.** Every function — exported, local, handler, composable, store action — is declared as a `const` bound to an arrow function with an explicit return type.
- This holds inside `<script setup>`, composables, stores, and utils alike. Hoisting-dependent code is a design smell: declare before use.

```ts
// RIGHT
const remove = async (): Promise<void> => { ... }

// WRONG — rejected in review
async function remove(): Promise<void> { ... }
```

## UI comes from `components/ui`, never raw markup

- Views and feature components compose the shared UI kit in `src/components/ui/` (`UiButton`, `UiInput`, `UiTable`, `UiModal`, `UiCard`, …). **Raw `<button>`, `<input>`, `<select>`, `<table>`, `<a>`-as-button and hand-rolled markup are forbidden outside `components/ui/` itself.**
- Only the primitives inside `components/ui/` may use raw HTML elements — that is where the accessibility and styling rules are implemented once.
- A missing primitive is not a licence to inline markup: add the primitive to `components/ui/`, then use it.
- **No raw HTML strings, ever**: `v-html`, `innerHTML`, and building markup from strings are forbidden — they are an XSS hole in a panel that renders customer-supplied names, domains and log lines.

## Icons come from `lucide-vue-next`, and nothing hand-draws one

- **The panel has exactly ONE icon source: the `lucide-vue-next` package**, reached through `UiIcon`
  in `src/components/ui/`. A hand-written `<svg>` in a component is forbidden — including a
  "just this once" three-line path for a chevron or a close cross. icon SVG comes only from lucide
  via `UiIcon`; `UiChart` is the single non-icon SVG site.
- This replaced the previous arrangement, in which `UiIcon` held ten glyphs copied from the design
  canvas as inline `<path>` data and nine more were written inline in the components that needed
  them. Those nineteen are gone: each was remapped to its lucide equivalent. Two glyphs had no exact
  counterpart and were mapped by judgement — `pulse` to lucide's `Activity`, `sparkle` to its
  `Sparkle`. The reason for the change is that a hand-drawn set has no next icon: every new one was
  a small drawing exercise decided by whoever needed it, at whatever stroke weight they typed, and
  the set drifted the moment two people added to it. A named import from a maintained set has none
  of those failure modes and costs about 1 kB gzipped for the icons actually used, because the
  package tree-shakes per icon.
- **`UiIcon` stays the only place lucide is imported.** Screens pass a `name` string; the size, the
  stroke weight and the decorative-by-default treatment are decided in that one file, so changing
  the icon set again is one file's work rather than the whole SPA's. Adding a glyph means adding a
  name to `UiIconName` and its lucide component to the map — never an import of a lucide component
  into a screen.
- What is unchanged: UI still comes from `components/ui/` and nothing outside it writes raw
  interactive markup; `v-html` and string-built markup are still forbidden; and an icon is still
  decorative by default (`aria-hidden`), so **a control whose only content is an icon must carry its
  own translated `aria-label`** — an icon-only button with no accessible name is a regression, not a
  simplification.

## Composables and the API layer

- **All HTTP lives in composables, split in two layers:**
  - `src/composables/useApi.ts` — the single low-level client composable (base URL, auth headers, RFC 7807 error decoding, cancellation). Exactly one of these in the whole application.
  - `src/composables/apis/use<Feature>Api.ts` — one file per backend feature (`useSitesApi.ts`, `useDatabasesApi.ts`, `useBackupsApi.ts`), each exporting a single composable that builds on `useApi` and exposes typed endpoint calls and nothing else.
- **API composables are consumed by Pinia stores ONLY.** A `.vue` file MUST NOT import or call an API composable, `useApi`, `fetch`, or any HTTP client. Components read state and call actions on stores; stores own every request, its loading state, and its error handling.

```ts
// stores/sites.ts — RIGHT
const api = useSitesApi()
const load = async (): Promise<void> => { items.value = await api.list() }
```

```vue
<!-- SitesView.vue — WRONG: rejected in review -->
<script setup lang="ts">
const api = useSitesApi()          // API composable in a component
const sites = await api.list()     // request from a .vue file
</script>
```

## Components

`<script setup lang="ts">` only, in this order: imports → props/emits → stores/composables → local state → computed → functions. No Options API, no `any`, no `@ts-ignore` (use `@ts-expect-error` with a reason comment if truly unavoidable).

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { useSitesStore } from '../store'
import type { Site } from '../types'

const props = defineProps<{ site: Site }>()
const emit = defineEmits<{ (e: 'deleted', id: string): void }>()

const store = useSitesStore()
const canDelete = computed(() => !store.busy && props.site.status !== 'suspended')

const remove = async (): Promise<void> => {
  await store.deleteSite(props.site.id)
  emit('deleted', props.site.id)
}
</script>
```

## One locale, one source of truth

- The interface language lives in `stores/locale.ts` and nowhere else. It feeds BOTH `i18n.global.locale` (the app's own chrome) and the `Accept-Language` header `useApi` sends (the server-produced text).
- Never read `navigator.language` outside that store, and never hardcode a starting locale in the i18n factory: a Russian interface with English server errors — or the reverse — is a bug the user sees immediately.
- The store resolves the initial language as: previously chosen (persisted) → browser preference when supported → `en`. Storage access is wrapped in try/catch; a language preference must never be able to break the shell.
- **Every locale carries the same keys.** A key added to `locales/en/` and forgotten in `locales/hy/` is not an error anywhere: vue-i18n renders the key itself, so the Armenian user reads `app.audit.heading` where a heading belongs. `maran structure` compares the three trees key by key and fails on either direction — a key missing from a locale, or one that exists in only one. English is the reference, because the keys are written in it.

## Forms: the browser never validates

- `UiForm` renders `<form novalidate>`, always. Native constraint validation is off across the
  panel, and no component may re-enable it.
- Do not use the native validation attributes (`required`, `pattern`, `min`, `max`, `minlength`,
  `maxlength`, `type="email"`, `type="url"`) as validation. Use `aria-required` and `aria-invalid`
  for assistive technology, and put the actual rules in the page's own validation.
- Why: browser bubbles are unstyled, positioned by the browser, appear in the BROWSER's language
  rather than the user's chosen interface language, and short-circuit submission before our code
  runs — so the user would see one message from the browser and a different one from the panel for
  the same field.
- Client-side rules mirror the server's validator (read it; do not guess). The server remains the
  authority: whatever the client allows through, the backend re-validates, and its message — already
  localized — is what the user sees on failure.

## Data comes from the backend; the SPA only displays it

- The frontend **never invents domain data**: no hardcoded identifiers, no placeholder GUIDs, no
  client-side lists of plans, statuses, tiers or limits. If a screen needs a set of values, the
  panel exposes them and the SPA renders what it receives.
- A free-text field asking a human to type an identifier is a design smell, not a feature: it means
  the contract is missing an endpoint. Report the gap and add the endpoint — do not paper over it
  in the UI.
- The SPA owns exactly two kinds of text: its own chrome (navigation, buttons, table headers, empty
  states) and the single "server unreachable" message. Everything describing server-side things —
  module names, plan names, statuses, error messages — arrives already localized from the backend.
- Constants in the SPA are for UI concerns only (a debounce interval, a page size default). A
  constant holding a domain value belongs on the server.

## Errors from the server — the backend owns their text

- **Error messages are produced by the backend, already localized, and rendered as-is. The frontend MUST NOT hold locale keys for server error messages.** There is no `errors.*` section in the frontend locale files, and no code that maps an error `code` to a frontend string.
- `useApi` sends the user's language (`Accept-Language`) and decodes the RFC 7807 payload; the message the backend returns in `title`/`detail` is what the UI shows. Stores keep that message in state; components render it as plain text (never `v-html`).
- The `code` field stays useful for behavior — retry, redirect, highlight a field — never for text lookup.
- Frontend locale files therefore cover only the application's own static chrome: navigation, buttons, table headers, empty states, form labels. Anything that describes a server outcome comes from the server.

```ts
// RIGHT — the backend already localized it
error.value = problem.detail

// WRONG — rejected in review: frontend must not own server-message text
error.value = t(`errors.${problem.code}`)
```

## State

- Pinia setup-style stores, one per module (`useSitesStore`). No global god-store. Cross-module reactions go through explicit store method calls in views, not store-to-store imports.

## i18n

- ALL user-visible strings go through i18n keys (`en`, `ru`, `hy`), including button labels and errors: `t('sites.create.title')`. A hardcoded literal in a template is a review reject. Keys are grouped by module.

## Styling

- Tailwind v4, CSS-first: there is no `tailwind.config.ts`. Design tokens live in an `@theme` block in `src/assets/css/main.css`; arbitrary values (`w-[137px]`) are forbidden when a token exists.
- Dark/light follow the design tokens; components MUST NOT hardcode hex colors. Until the design tokens land, the default palette is acknowledged technical debt — do not spread new palette choices across components in the meantime.

## Accessibility

- **Exactly one `<main>` per document.** The shell (or its layout) owns the landmark; pages render `<section>`. A nested `<main>` breaks screen-reader landmark navigation.
- The UI kit carries accessibility, so views inherit it: `UiButton` renders a real `<button>`, `UiInput` binds a real label, `UiTable` renders a real `<table>`. Fixing a primitive fixes every screen at once.
- Destructive actions get a confirmation dialog (`UiConfirm`). Keyboard path and focus order are checked in review for every new view.

## TypeScript stays on 5.x until vue-tsc can read 7

The repository pins `typescript` exactly, and deliberately not to the newest release.

TypeScript 7 moves the compiler's entry points, and `vue-tsc` resolves `tsc` through the export map
that changed: with 7 installed, `npm run typecheck` dies with `ERR_PACKAGE_PATH_NOT_EXPORTED` before
it reads a single file. The type check is the frontend's only compiler-level gate — the SPA has no
unit tests by design (rules/testing.md) — so losing it to a version bump costs more than the bump
buys.

Verified, not assumed: 7.0.2 was installed and the gate was run before this pin was written back.
Revisit when `vue-tsc` ships support; the pin is an exact version so the day it changes is a day
somebody chose.
