# Nuxt / TypeScript Rules (website)

Normative. Governs the public site at maran.innovayse.com, which lives in its own repository —
`gitlab.com/innovayse/maran-website` — and is cloned into `website/` when both are wanted side by
side. This file stays here because the rules are the product's, not the repository's: the site is a
second front end to the same system, and its laws are decided where the other one's are.

Enforced by ESLint + oxlint + `nuxt typecheck`, run by that repository's own pipeline.

`rules/vue.md` governs the panel. This file governs the site. Everything in `rules/vue.md` that is
about the language rather than the panel — arrow-only functions, mandatory doc comments, one unit
per file, member order, stock text steps, type hygiene, accessible names — binds here unchanged and
is not restated. What follows is what differs, and why.

## Why the site is a second front end and not a second half of the first

The panel is an authenticated Vite SPA: it loads once, behind a login, and nothing outside the
browser ever reads its markup. The site is the opposite — crawled, shared as links, and read by
people who have not installed anything yet. Those two jobs disagree on almost every decision that
matters: rendering, routing, where text lives, what ships in the bundle.

So they are separate subprojects with separate dependency trees, separate lint runs, separate
builds and separate container images. They share a visual vocabulary and these rules; they share no
code. **No file in `website/` may import from `frontend/`, and no file in `frontend/` may import
from `website/`** — including the UI kit. The kits look alike and are not interchangeable: the
panel's primitives assume a router, stores and an API client that do not exist here.

Design tokens are the one deliberate duplication. They are COPIED into
`website/app/assets/css/main.css`, with a comment naming the file they came from. Two consumers do
not pay for a shared package; the extraction becomes right the day a third appears.

## Structure

The layout is flat, the same way the panel's is — there is no `app/modules/`:

- `content/{en,ru,hy}/{docs,blog,releases}/**.md` — page copy, one locale tree each.
- `app/pages/` — file-based routes. A route file is named for its path segment, not suffixed
  `*Page.vue`: Nuxt derives the URL from the file name, so a suffix would appear in the URL.
- `app/layouts/<Name>Layout.vue` — page shells (`DefaultLayout`, `DocsLayout`).
- `app/components/ui/Ui*.vue` — the site's UI kit, the only home of raw HTML controls.
- `app/components/marketing/`, `app/components/docs/` — feature components, PascalCase files.
- `app/composables/use<X>.ts` — flat, one composable per file, the file named after its export.
- `app/utils/<purpose>.ts` — pure helpers, one purpose per file, and the file is named for the
  PURPOSE rather than for one function inside it: `content.ts`, `format.ts`, `navigation.ts`,
  `seo.ts`. A file named after a single helper (`formatContentDate.ts`) has nowhere to put the
  second one, so the second one gets a file of its own and the pair drifts apart; a file named for
  its purpose tells a reader where to look before they know what the function is called. The
  composables are the opposite and stay one-per-file named after their export, because a composable
  IS its export (`useLocalisedContent.ts`).
- `app/types/<domain>.ts` — flat, one domain per file.
- `i18n/locales/{en,ru,hy}/<namespace>.json` — chrome strings only, one directory per locale and one
  file per namespace (`common`, `nav`, `search`, `error`, …), each file wrapping its own top-level
  key. The panel's arrangement, for the same reason: a translator opens one small file per subject,
  and two people editing different areas do not collide in one large file. A new namespace is added
  to `LOCALE_NAMESPACES` in `nuxt.config.ts` and to all three locale directories at once.
- `app/components/content/Prose*.vue` — the elements rendered Markdown produces (a link, a table, a
  code block). They are how a content decision reaches every page at once, and they delegate to the
  kit rather than reimplementing it.
- `tests/{unit,components}/` — Vitest specs.
- `scripts/`, `nuxt.config.ts`, `content.config.ts`, `tailwind.config.ts`, `vitest.config.ts`,
  `eslint.config.ts`, `Dockerfile`.

A feature's files MUST NOT import from another feature's folder — shared things move down into
`components/ui`, `composables`, `utils` or `types`.

## Text lives in Markdown or in a locale file, and the split is not negotiable

Two homes, and each holds exactly one kind of text:

- **`content/`** holds prose: everything a reader reads as sentences — documentation, blog posts,
  release notes, the support matrix, and the body copy of a marketing page.
- **`i18n/locales/`** holds chrome: navigation labels, button text, search placeholders, the
  language picker's accessible name, error-page headings.

Prose does not go in JSON. A locale file is a key-value list optimised for lookup, not for reading:
a paragraph inside it cannot be reviewed as a paragraph, diffed as a paragraph, or handed to a
translator as one. Chrome does not go in Markdown either — a button label is not a document.

This is the one place the panel's law is relaxed: `vue/no-bare-strings-in-template` stays `error`
for the shell (layouts, `components/ui/**`, navigation) and is `off` for
`app/components/marketing/**` and `app/components/docs/**`, whose text arrives through a slot or
through `<ContentRenderer>`. The relaxation is scoped by path in `eslint.config.ts` and nowhere
else; a bare string in a layout is still a build failure.

**Every locale carries the same keys.** `scripts/check-content.mjs` compares the three locale
directories namespace by namespace, in both directions, and fails on a key present in one and
missing from another — a namespace file missing entirely included — a missing key renders
as the key itself, and an Armenian visitor reads `nav.docs` where a menu item belongs.

## A missing translation shows English and asks not to be indexed

When a page exists in `en` but not in the requested locale, the English body renders at the
requested URL, above it a visible notice saying so, and the page emits `noindex, follow`.

All three parts are load-bearing. An empty page is worse than English for the reader. An indexed
English page under a Russian URL is worse than both for the site: it competes with the page it
duplicates and teaches a crawler that `/ru/` means nothing.

The fallback lives in exactly one composable (`useLocalisedContent`), over one pure function
(`utils/content.ts`). A page that re-implements it gets a different answer somewhere, and the
difference is invisible until a crawler finds it.

**Links inside content are localized at render, not in the Markdown.** A writer writes
`/platforms`; `ProseA` turns it into `/ru/platforms` for a reader who is in Russian. Writing the
prefix into the file would be wrong the moment that file is translated, and a link that drops the
reader back into English is how a localized site quietly stops being one.

**Language is offered at the site root only** (`redirectOn: 'root'`). A deep link stays in the
language it was shared in: under the looser setting, a returning reader whose cookie said `ru` was
redirected away from `/docs/installation` — the English canonical URL — to the Russian one, which
is both a shared link changing language under the person who followed it and a canonical URL that
answers with a redirect.

## SEO is a build gate, not a later pass

Every routed page emits, without exception:

- an absolute `canonical`;
- `hreflang` alternates for `en`, `ru` and `hy`, plus `x-default` pointing at the English URL;
- a title and description — translated, never a template literal built from a slug;
- structured data for what the page is: `SoftwareApplication` on the home page, `TechArticle` in
  documentation, `BlogPosting` for a post, `FAQPage` on the questions page, and `BreadcrumbList` on
  every nested page.

**The sitemap is built from the content, not from the router.** A page served by a dynamic route
exists because a Markdown file exists, and the sitemap module cannot see that: left alone it listed
the static pages and silently omitted the documentation, the posts, the release notes and the legal
pages. `server/api/__sitemap__/urls.ts` enumerates them from the collections, and each URL carries
`_i18nTransform` so all three locale sitemaps get it rather than English alone.

**A page whose content is also data belongs in frontmatter.** The questions page publishes its
questions twice — as text a reader reads and as the `FAQPage` graph a search engine may render in a
result — and both come from one list in the content file. Two copies of a question answer
differently within a week.

A page that ships without them is incomplete in the same sense as a page that fails to compile. The
tags are asserted in `tests/`, so "I forgot the canonical" is a red run rather than a discovery made
in a search console six weeks later.

**Indexability is decided by the running environment, never by a file someone edits before
deploying.** `site.indexable` is `false` in the configuration, and only a deployment that sets
`NUXT_PUBLIC_SITE_INDEXABLE=true` is indexed. Two properties matter and both are deliberate: the
default is the safe one, and the value is read at runtime rather than baked into the build — so one
image can serve staging and production, and a staging host cannot become indexable because somebody
built the image on the wrong machine. A staging host that was indexed once stays in an index long
after it is taken down.

## Styling: tokens in CSS, scale in `tailwind.config.ts`

The panel has no Tailwind config file at all (rules/vue.md). This subproject has one, and the split
between the two files is the rule:

- `app/assets/css/main.css` owns the **design tokens** — the colour variables and their light and
  dark values. They must be CSS custom properties, because the theme switches at runtime and a
  build-time constant cannot.
- `tailwind.config.ts` owns the **scale extensions** — values that are identical in every theme
  (the site's `8xl`/`9xl` container widths), where a typed file is easier to read and to change
  than a comment block in a stylesheet. It is wired in with `@config` at the top of `main.css`.

Arbitrary values are forbidden wherever a token exists, and that includes the bracket escape hatch
for a token that is already in the theme: `shadow-focus`, not `shadow-[var(--shadow-focus)]`;
`tracking-title`, not `tracking-[var(--tracking-title)]`. A theme value the utility layer already
generates, written as a bracket value instead, is a second spelling of the same thing — and the
day the token changes, only one of the two spellings follows it.

Body sizes remain stock Tailwind steps, at every breakpoint: `text-sm sm:text-base` is a responsive
paragraph, `text-[15px]` is a vocabulary this repository would own alone.

**Above them sit three DISPLAY steps** — `text-display-sm`, `-md`, `-lg` — defined in
`tailwind.config.ts` with their own line height and tracking, and they are the one place this
subproject goes past the panel's scale. The panel stops at 24px and is right to: it is a dense
working surface read at a desk all day. A landing page is the opposite situation — one screen to
say what the product is, to somebody who has never heard of it — and a 24px hero on a 1440px
monitor reads as a paragraph that forgot to be a title. They are display steps, not a replacement
scale: prose, labels and interface text keep using Tailwind's own.

**Elevation is a token, and it is theme-dependent.** `shadow-card` and `shadow-raised` are almost
nothing in the dark theme, where surfaces separate by hairline border and a shadow on near-black is
invisible, and carry real weight in the light theme, where a border alone leaves every card flat on
the page.

## Typefaces are downloaded at build and served from this origin

`@nuxt/fonts` fetches the families at build time and serves them from this domain. Two reasons, and
the second is a promise the site makes in writing: a font fetched from a third party is a
render-blocking request to somebody else's uptime, and it tells them who is reading the site, which
the privacy page states plainly does not happen.

This was not a refinement. Until it was wired, the stylesheet asked for typefaces the browser had
never heard of and every page rendered in the system font — `document.fonts.size` was zero, and the
site looked exactly as unconsidered as that sounds. A `font-family` declaration is not a font; if
nothing loads it, it is a comment.

## Responsive is the default case, not an afterthought

The panel is used at a desk. The public site is opened from a phone, from a search result, in a
split window — so every screen is designed from the narrow end up:

- **Nothing is hidden without a replacement.** A navigation that collapses to nothing below `md`
  leaves a reader with a wordmark and no way anywhere; the masthead and the documentation
  navigation each collapse into a disclosure that holds the same links. "Hidden on mobile" is only
  acceptable for something a narrow screen genuinely does not need, such as a table of contents
  beside an article that is already one column.
- **Touch targets are at least 44px.** Kit controls carry it (`min-h-11`), and a link that behaves
  as a control gets it too. A 28px row is a row somebody misses twice before hitting it.
- **Padding grows with the viewport** — `px-4 sm:px-6 lg:px-8` — rather than one value that is
  cramped on a phone or lost on a monitor.
- **Width is decided once, by the page's container** (`max-w-8xl`), never by the components inside
  it. A paragraph, a list or a hero that carries its own `max-w-*` is a second width competing with
  the first, and the two disagree the day the container moves.
- **What cannot be narrowed scrolls inside itself**: tables through `UiTable` and code blocks
  through `ProsePre`, each in its own focusable, labelled region. A page that scrolls sideways as a
  whole takes its navigation with it, and a keyboard user who cannot reach a scroll region cannot
  read what is inside it.

## What a documentation page is made of

A documentation page is not an article with a menu beside it. Five parts, and each earns its place
by what a reader actually does on one:

- **The section above the title.** A reader arriving from a search result needs to know which part
  of the documentation they landed in before they read a word of it.
- **An anchor on every heading**, always visible and wrapped in a full touch target. It is what
  makes a section citable: somebody pointing a colleague at one part of a page should be able to
  send a link to that part.
- **"On this page"**, the article's own headings, in a column from `xl` up. Below that width the
  article is already one column, and a list of its headings would only push the text down.
- **A copyable code block with its language labelled.** The commands on these pages are meant to be
  run, and a command retyped by hand is a command with a character missing.
- **The pages either side, at the foot.** Documentation is read in order more often than it is
  searched, and the order comes from the same navigation the sidebar renders — a pager with its own
  sort disagrees with the menu the first time a section moves.

The title, the description and the section come from frontmatter and are rendered by the page, so a
Markdown body must NOT open with its own `# Heading`: two `<h1>` elements on a page is a structural
error a crawler reads before a person does.

## Every content page takes the same frame

`ContentArticle` renders the anatomy above, and the routes hand it a document. One component rather
than one per route, because these pages differ in what they are ABOUT and not in how they are read
— and the parts a reader depends on are exactly the ones that drift when each page keeps its own
copy: a single `<h1>`, an anchor per heading, a table of contents that matches the body, a pager
that matches the index it came from.

What a route decides is its eyebrow and its order: a documentation page shows its section and walks
the sidebar, a post shows its date and walks the blog index, a release shows its channel and date
and walks the release list.

## The theme is a cookie, not local storage

The reader's dark/light choice is stored in a cookie, and this is not interchangeable with local
storage: the server renders the page, so it has to know the theme BEFORE it writes the HTML. Local
storage is readable only after the page has loaded, which is one paint too late — the reader would
watch the dark page flash before their light one arrived.

A stored value is narrowed before use. A cookie is a string somebody can edit, and a hand-typed one
must not leave the document as `data-theme="whatever"`, which resolves to no palette rather than to
a wrong one.

A theme control is named for what pressing it will DO, not for the state it is in: "switch to the
light theme" tells a screen-reader user the outcome, and "dark theme" tells them nothing.

## Colour is measured, not judged by eye

Every token pair a reader has to read through is checked with a contrast ratio before it ships, and
the numbers are in the stylesheet beside the values. Three of ours failed the first time they were
measured, and all three looked fine on screen:

- the muted text was 3.98:1 on the page floor, under the 4.5:1 a body-size string needs — and it
  carries dates, captions and the table of contents' label;
- white on the accent was 3.89:1, so every filled button's label was under the bar;
- the light theme's link colour was 4.32:1, just under it.

The button case is the general lesson: **the accent as a link colour and the accent as a filled
background are two jobs and need two tokens.** One value cannot be both a bright link on a dark
floor and a background dark enough to carry white text — `--ac` is the first, `--acb` the second.

Thresholds: 4.5:1 for body text, 3:1 for large text and for a UI boundary that carries meaning. A
decorative hairline between cards is not one of those and is allowed to be quiet.

## Depth in the dark theme is a lighter surface, not a shadow

The surface ladder (`--bg` → `--s1` → `--s2` → `--s3`) climbs toward lighter, and that is what
separates a card from the page. A shadow on near-black is invisible, so `shadow-card` is close to
nothing there and carries real weight only in the light theme, where a hairline border leaves
everything flat.

## The page has texture, and the header floats over it

Two deliberate pieces of decoration, both drawn in CSS against the theme's own tokens rather than
shipped as images — an image of a gradient cannot follow a theme switch:

- a 72px grid at about 2% opacity, fixed to the window rather than the document, so it reads as
  something the page sits on rather than as a pattern printed on it;
- one accent wash behind the top of the page.

The masthead is sticky and translucent with a backdrop blur. A reader halfway down a documentation
page should reach search and the language picker without scrolling back up, and the blur is what
keeps the bar's own text legible over whatever passes under it — translucency without it is a
contrast defect with a nice name.

Anything past this is a no: no parallax, no scroll-jacking, no animated gradient. The measure is
whether a reader who came for the install command gets to it faster.

## Search is finished with the keyboard

Anybody who opens a search box with ⌘K expects to finish without touching the mouse, so the modal
is a real combobox over a listbox: the arrows move the highlight and wrap at both ends, Enter opens
it, Escape closes and returns focus to the control that opened it. Focus stays in the input and the
highlighted result is named by `aria-activedescendant` — moving DOM focus into the list would take
the caret out of the field the reader is still typing in.

Two things it deliberately does NOT do:

- **Hover does not move the highlight.** It was written and removed: a pointer resting anywhere
  over the list makes the keyboard's selection jump back under the cursor while the reader arrows
  past it. The pointer gets a hover state and a click; the arrows own the highlight.
- **The browser's own search decorations are off.** `type="search"` draws a cancel glyph whose
  shape and colour are the engine's choice, and the site draws its own from the icon set with a
  translated name and a real touch target. The type stays, for the keyboard it asks for and for
  what assistive technology announces.

The shortcuts are printed along the bottom of the dialog. A shortcut nobody knows about is a
shortcut nobody has.

**Search matches WORDS, not the whole string.** Matching the query as one substring is the obvious
first implementation and it is wrong in a way that looks fine in testing: every single-word query
works, so nothing seems broken — and then "install php" returns nothing while "install" and "php"
each return several sections. A reader typing two words expects both to count, in either order.
Every word must appear (a result matching half the query is a result that ignored the reader), and
where it appears decides the order: a word in a heading outranks a word in a paragraph, and a
heading that starts with the word outranks both. The matching lives in `utils/search.ts` and is
tested there, because this is exactly the kind of defect a rendered modal hides.

## The brand mark is a file, never markup

`docs/brand/` is the source of truth for the identity and explains the drawing; the site consumes it
and does not redraw it. The mark is served from `public/` and used as `<img src="/favicon.svg">` in
the masthead and in the social-card template, so the tab, the header and every preview read the
same bytes and the identity cannot drift by somebody editing one copy. Inlining it into a component
is forbidden anyway (rules/vue.md bans a hand-written `<svg>`).

It is decorative wherever the word "Maran" is beside it — `alt=""` plus `aria-hidden`, so a screen
reader does not announce the brand twice.

Anything `public/` ships must be REACHED by something: the maskable icon existed for weeks with no
web app manifest to declare it, which meant an Android home-screen shortcut fell back to a
screenshot of the page. A shipped asset nothing links to is a file that only looks like branding.

## Every page's meta is complete, and measured

The tags below are not optional decoration; each one changes what somebody sees before they reach
the page:

- `og:type` is `article` for documentation, posts and release notes, with `article:published_time`
  and `article:author` where the content has them. Left at the default `website`, a dated piece of
  writing previews as a homepage.
- `og:image` is 1200x630 with an `og:image:alt`. The module's own default was 1200x600 — close
  enough to look right in a preview and wrong in the frame that matters, which crops a 2:1 image
  and takes the mark off the edge.
- `theme-color` is declared twice, once per scheme, because a phone paints its address bar with it
  and a single value leaves half the readers a dark bar over a light page. `color-scheme` is the
  other half.
- Icons are declared at several PNG sizes beside the SVG: the browsers that ignore the SVG are
  exactly the ones that pick badly from a single size.

**A title and an `<h1>` are two jobs.** `title` in frontmatter is the heading a reader sees, and
"Security" reads well there and says nothing in a list of ten search results. Pages may carry an
optional `seoTitle` for the tab and the result; every page on this site does, and the measured
range is 37-57 characters against the 50-60 the result snippet allows. Descriptions run 120-160 —
they were measured too, and four pages that were under 100 were rewritten rather than left.

## An index page has a description, not a menu label

`nav.blog` is the word in the navigation. It is not a page title, and it is certainly not a meta
description — using it as one shipped `/blog` with a four-character description and `/releases`
with eight, which is what a search engine would have printed under the result. Index pages carry
their own `index.<page>.title` and `index.<page>.description`, written as a sentence, and show the
description on the page as well.

## Heading levels follow the document, not the styling

A card's heading is `h3` under a section that has an `h2`, and `h2` when its grid has no heading of
its own and sits directly under the page's `h1`. Skipping a level is a hole in the outline, and the
outline is the table of contents a screen reader offers. The audit that caught this ran over every
page's server-rendered HTML rather than over the components — the defect only exists once a page
composes them.

## Scrollbars are styled, and they have no arrows

The site draws its own scrolling regions — a code block, a wide table, the search results — and a
platform-default scrollbar inside one of them is a light strip on a dark surface that reads as a
rendering fault rather than as a control. Every scrollbar takes the design's own border colour on a
transparent track, set twice: `scrollbar-width`/`scrollbar-color` for one engine family and the
`::-webkit-` rules for the other, because a browser honouring only one would keep half the default
appearance.

`::-webkit-scrollbar-button` is removed. The stepper arrows are a default nobody presses, they eat
the first and last nine pixels of every short scrolling region, and at this width they render as
two indistinct smudges.

## Data fetching

- `useAsyncData` / `useFetch` / `$fetch` are the only ways to fetch. Never `window.fetch`, never an
  HTTP client of your own.
- **Every data key carries the locale.** `useAsyncData` caches by key, so `docs-/docs/installation`
  hands the Russian page the English payload fetched a moment earlier: the body is right, and
  `isFallback` is the English page's `false`, so the notice and the `noindex` both disappear on a
  language switch while the page looks perfectly correct. This was a real defect, found by driving
  the site in a browser rather than by any check that existed at the time.
- **A locale with no content of a kind has no table in the content database, and the query throws.**
  That is the normal state of a language nobody has translated yet, so every content read treats
  the failure as "this locale has nothing" and lets the English fallback run. Untreated, every
  localized URL answered 500.
- Content is read through `@nuxt/content`'s `queryCollection*` helpers, in a composable — not in a
  component. A component receives what a composable resolved.
- The site has **no API client and no store layer**. It is a content site: if a page needs state
  that outlives a request, say why in the pull request before adding it.
- Nothing in `website/` may call the panel's API, read its cookies or link to an authenticated
  route as if it were public.

## Rendering and the server runtime

- `ssr: true`, nitro preset `node-server`. A page must render its `<h1>` and its body in the
  server's HTML response; a section that only appears after hydration is invisible to a crawler and
  to anyone whose JavaScript failed.
- `process.server` / `process.client` branches need a comment saying why the two sides differ.
- The server runtime holds no secrets. Anything in `runtimeConfig.public` is published; anything
  that must not be published does not belong on this site at all.

## Dates are formatted by `date-fns`, never by hand

Content dates are written in frontmatter as plain ISO strings — right for a file humans edit and
diff, wrong for a page, where `2026-09-19` reads as a machine's date. `app/utils/format.ts` turns
one into the reader's own form with `date-fns`, the same library the panel uses.

Hand-writing the pattern is forbidden, and not as a style preference: Armenian and Russian decline
month names, so a template that concatenates a day, a month name and a year produces
"19 Сентябрь 2026" where the language wants "19 сентября 2026". The library knows the grammar for
all three locales; a component does not have to.

A value that is not a date is returned unchanged rather than replaced by a placeholder — the
frontmatter is what the writer typed, and showing it back is what makes the mistake findable. The
lint gate's frontmatter check is what actually refuses it.

## Icons

One source: `@nuxt/icon` with the locally bundled `@iconify-json/lucide` set, `mode: 'svg'` and
`clientBundle.scan` on, so an icon is inlined at build time. A hand-written `<svg>` in a component
is forbidden, including a three-line chevron path. Runtime fetching of icons from a network
endpoint is forbidden — it makes a third party's availability a dependency of a page render, and
tells them who is reading the site.

An icon is decorative by default and carries `aria-hidden`. A control whose only content is an icon
carries its own translated accessible name.

## Components are named by their file, never by their folder

`components: [{ path: '~/components', pathPrefix: false }]` in `nuxt.config.ts`, and it is not a
preference. Nuxt's default prefixes a component with its directory, so
`components/marketing/SiteHeader.vue` registers as `MarketingSiteHeader` — and a template that
writes `<SiteHeader>` then resolves to nothing and renders NOTHING, with no error and no warning.
The site's header and footer were missing from every page this way while every page still answered
200 with its `<h1>` intact. One component per file, named after the file (rules/vue.md): the folder
is where a reader looks, the file name is what a template writes.

## Content checks run in the lint gate

`npm run lint` runs every sub-check to completion and prints a verdict per check — an exit code
alone hides which check ran, the same reasoning as `frontend/scripts/lint.mjs`. The content check
fails when:

- a version in the root `CHANGELOG.md` has no English release page under `content/en/releases/`;
- a Markdown file's frontmatter does not satisfy its collection's schema;
- an internal link resolves to no page and no content file;
- the three locale files' key trees differ, in either direction;
- a frontmatter value contains an unquoted colon. YAML reads `description: The first version: the
  contract` as a nested map, and the consequence is not a parse error — the key silently becomes an
  object and every key after it on that line is lost. It cost a release page its date, its channel
  and its version, all reading `null` in the content database while the file looked correct.

Release notes are prose written for an operator deciding whether to upgrade. `CHANGELOG.md` remains
the technical record; neither is generated from the other, and the check is what keeps them from
drifting apart silently.

## Tests

The site is tested with Vitest, in `website/tests/`, run by `npm test`. This differs from the panel,
which has no unit runner by design (rules/testing.md): the panel's logic lives behind a login and is
exercised end to end, while this subproject's logic is small, pure and directly callable — content
resolution and its English fallback, navigation shaping, structured-data construction, locale-key
parity. Those are the things that break silently here, and each is a function a test can call.

- `tests/unit/` — helpers, composables and content-shaping logic.
- `tests/components/` — component tests.
- A file needing the Nuxt runtime declares `// @vitest-environment nuxt` at the top; everything else
  runs in the cheaper default environment.

Test names are behaviour sentences, and test files are the only code here exempt from doc comments.
There is no browser suite in this subproject: what a crawler receives is asserted from the rendered
markup in tests, not by driving a browser.

Implementation first, tests in a dedicated pass afterwards — the order in rules/testing.md applies
to this subproject unchanged.
