<script setup lang="ts">
/**
 * The shell's top bar: breadcrumbs for the active screen, a separator, the
 * server picker, and the right-aligned interface controls.
 *
 * Values from the design canvas: 46px tall on `--s1` under a `--b1` bottom
 * border, `padding:0 14px`, a 10px gap, 12px breadcrumb text where the trail
 * is `--t2` and the leaf is `--t1` at weight 600, and a 1px × 18px `--b1`
 * separator before the picker.
 *
 * The canvas is a desktop artboard only, so the compact behaviour here — the
 * menu button below `lg`, and the disabled placeholders dropping out rather
 * than overflowing the 46px band — is this panel's decision, not the design's.
 *
 * The design derives its breadcrumbs from a screen→path map; the equivalent
 * here is {@link SHELL_ROUTE_LABEL_KEYS} for the shell's own screens plus the
 * module catalogue for everything module-backed, so the trail never names a
 * screen the panel did not report.
 */
import { computed, type ComputedRef } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import ShellLocaleSwitcher from './ShellLocaleSwitcher.vue'
import ShellTasksBadge from './ShellTasksBadge.vue'
import ShellThemeSwitcher from './ShellThemeSwitcher.vue'
import type { NavigationEntry } from '../../types/navigation'

/**
 * i18n keys naming the shell's own screens in the breadcrumb trail. Only
 * routes this bundle owns appear here — a module's screen is named by the
 * catalogue entry the panel sent, never by a key invented in the frontend.
 */
const SHELL_ROUTE_LABEL_KEYS: Record<string, string> = {
  'system-status': 'app.nav.systemStatus',
  upgrade: 'app.upgrade.heading',
  'not-found': 'app.notFound.heading',
  // Every screen under /settings. They were missing, so each fell through to its own route name
  // and the first line of the page read "Panel / audit", "Panel / sessions" — a router identifier,
  // lowercase and in English, whatever language the operator chose. It is the same fall-through
  // CHILD_ROUTE_LABEL_KEYS below was written for, one level up, and these are the screens reached
  // most often from the navigation.
  sessions: 'app.sessions.heading',
  audit: 'app.audit.heading',
  'security-policy': 'app.securityPolicy.heading',
  'two-factor': 'app.twoFactor.heading',
  'two-factor-setup': 'app.twoFactor.heading',
  'smtp-settings': 'app.smtp.heading',
  'php-versions': 'php.title',
  'my-account': 'app.shell.menu.myAccount',
}

/**
 * i18n keys naming a screen that sits BENEATH a module, appended after the
 * module's own crumb. Without this a child route fell through to its raw route
 * name and the trail read "Panel / accounts-new" — a router identifier shown to
 * a customer, in English, whatever language they chose.
 */
const CHILD_ROUTE_LABEL_KEYS: Record<string, string> = {
  'accounts-new': 'accounts.form.heading',
}

/** Props accepted by {@link ShellHeader}. */
const props = defineProps<{
  /** Navigation entries the shell is showing, used to name a module-backed screen. */
  entries: readonly NavigationEntry[]
  /** Whether the compact navigation drawer is open, reported as `aria-expanded`. */
  navigationOpen: boolean
}>()

/** Events emitted by {@link ShellHeader}. */
const emit = defineEmits<{
  /** The user pressed the menu button; the layout owns the drawer. */
  (e: 'openNavigation'): void
}>()

const { t } = useI18n()
const route = useRoute()

/**
 * The breadcrumb trail for the active route: the navigation group, then the
 * screen. One group is all the catalogue supports today (it reports no
 * grouping), so the trail is two levels deep — the design's own shape for a
 * top-level screen.
 */
const crumbs: ComputedRef<string[]> = computed(() => {
  const routeName = typeof route.name === 'string' ? route.name : ''
  const shellLabelKey = SHELL_ROUTE_LABEL_KEYS[routeName]
  if (shellLabelKey !== undefined) {
    return [t('app.nav.groups.panel'), t(shellLabelKey)]
  }

  // A module screen is named by what the panel called the module. The route's
  // `meta.module` is the authority — matching on the route NAME missed every
  // child route, whose name ('accounts-new') is not the module's ('accounts').
  const moduleName = typeof route.meta.module === 'string' ? route.meta.module : routeName
  const entry = props.entries.find((candidate) => {
    return candidate.moduleName === moduleName
  })
  const trail = [t('app.nav.groups.panel'), entry?.label ?? moduleName]

  const childLabelKey = CHILD_ROUTE_LABEL_KEYS[routeName]
  if (childLabelKey !== undefined) {
    trail.push(t(childLabelKey))
  }

  return trail
})
</script>

<template>
  <!-- h-11.5 is the design's 46px header band exactly, on Tailwind's own
       spacing scale; h-12 would have made it 48px. -->
  <header
    class="shell-header flex h-11.5 shrink-0 items-center gap-2.5 border-b border-border-subtle bg-surface-1 px-3.5"
  >
    <!-- The menu button opens the off-canvas navigation and exists only below
         `lg`, where no navigation column is rendered. The icon is drawn inline
         because the kit's icon set has no menu glyph and `components/ui/**` is
         not this component's to extend; it is decorative, so the button's
         accessible name comes from its translated `aria-label`. -->
    <UiButton
      class="shell-header-icon lg:hidden"
      :aria-label="t('app.shell.openNavigation')"
      :aria-expanded="navigationOpen"
      @click="emit('openNavigation')"
    >
      <UiIcon name="menu" size="md" />
    </UiButton>

    <nav class="min-w-0" :aria-label="t('app.shell.breadcrumbLabel')">
      <ol class="flex min-w-0 items-center gap-1.5 text-base">
        <li
          v-for="(crumb, index) in crumbs"
          :key="crumb"
          class="crumb truncate"
          :class="index === crumbs.length - 1 ? 'font-semibold text-text-primary' : 'text-text-secondary'"
        >
          {{ crumb }}
        </li>
      </ol>
    </nav>

    <span class="mx-0.5 h-4.5 w-px shrink-0 bg-border-subtle max-lg:hidden" aria-hidden="true"></span>

    <!-- The panel exposes no server inventory yet, so the picker states that
         plainly and stays disabled rather than showing an invented server. -->
    <UiButton variant="secondary" class="shell-header-picker max-lg:hidden" disabled>
      <UiIcon name="server" size="md" />
      <span>{{ t('app.shell.noServerSelected') }}</span>
      <UiIcon name="chevronDown" size="sm" />
    </UiButton>

    <span class="flex-1"></span>

    <!-- Background work in flight, on every screen. It is a view of the tasks store rather than a
         fetch of its own, which is what lets it move the moment a task's stream reports a change —
         no navigation, no reload. It draws nothing while nothing is running. -->
    <ShellTasksBadge />

    <!-- Below `sm` the theme segment drops out rather than pushing the row wide:
         the drawer's own footer still carries a theme control, so nothing is lost. -->
    <ShellThemeSwitcher class="max-sm:hidden" />
    <ShellLocaleSwitcher />

    <!-- The design's three remaining header controls. Each is rendered and each
         is disabled, with the reason stated once here rather than three times:
         none of them has a backend. An assistant with no assistant behind it, a
         bell with no notification stream, and an avatar with no signed-in user
         would all have to invent what they display — and an invented "3" unread
         or a fabricated set of initials is a lie the panel tells a customer
         every time the page loads. Disabled says "not yet"; populated would say
         something false. -->
    <UiButton class="shell-header-ai max-lg:hidden" disabled>
      <UiIcon name="sparkle" size="md" />
      {{ t('app.shell.askAi') }}
    </UiButton>

    <UiButton class="shell-header-icon max-lg:hidden" :aria-label="t('app.shell.notifications')" disabled>
      <UiIcon name="bell" size="md" />
    </UiButton>

    <UiButton class="shell-header-avatar max-lg:hidden" :aria-label="t('app.shell.account')" disabled>
      <UiIcon name="user" size="md" />
    </UiButton>
  </header>
</template>

<style scoped>
/* The design draws the trail with "/" separators; rendering them from CSS
   keeps them out of the accessible name and out of the locale files, since a
   separator is punctuation rather than copy. */
.crumb + .crumb::before {
  content: '/';
  margin-right: 6px;
  color: var(--t3);
}

/* ONE definition for every control in this row.
 *
 * They had five. Heights of 28px, 26px and whatever `padding: 4px 8px` and `4px 9px` produced;
 * horizontal padding of 8px, 9px and none; the locale switcher with no box at all while its
 * neighbours were boxed; and the avatar on a different surface and border from everything beside
 * it. Nothing about that was a decision — it was five controls written at five times.
 *
 * So the box is stated once, here, where the row is laid out. A control below overrides only what
 * genuinely differs about it — the assistant's violet wash, the square icon's fixed width, the
 * avatar's circle — and nothing restates the height, the surface or the border.
 *
 * The locale switcher's own button is included by name: it renders inside UiDropdown, so the class
 * on the component sits on a wrapper and the box belongs to the button within it.
 */
.shell-header > * {
  flex-shrink: 0;
}

.shell-header-picker,
.shell-header-ai,
.shell-header-icon,
.shell-header-avatar,
:deep(.shell-header-theme),
:deep(.shell-locale button) {
  height: 28px;
  padding: 0 9px;
  gap: 6px;
  border-radius: 6px;
  border: 1px solid var(--b1);
  background: var(--s2);
  color: var(--t2);
  font-size: var(--text-base);
  font-weight: 400;
}

.shell-header-picker:focus-visible,
.shell-header-ai:focus-visible,
.shell-header-icon:focus-visible,
.shell-header-avatar:focus-visible,
:deep(.shell-header-theme:focus-visible),
:deep(.shell-locale button:focus-visible) {
  border-color: var(--ac);
}

/* The design's "Ask AI" button: a violet wash rather than the accent, so it reads as a different
   kind of action from everything else in the header. Only the colours differ. */
.shell-header-ai {
  background: var(--pus);
  border-color: rgb(139 109 240 / 0.35);
  color: var(--pu);
  font-weight: 500;
}

/* The two square controls: the row's height in both directions, and no horizontal padding because
   the glyph is the content. */
.shell-header-icon,
.shell-header-avatar {
  width: 28px;
  padding: 0;
}

/* The avatar is the one control whose SHAPE differs, and that is the distinction it carries — not a
   different surface, which is what it used to have as well. It stays disabled: there is no /me
   endpoint, so the panel has no initials to put in it and draws a neutral glyph rather than
   inventing a person. */
.shell-header-avatar {
  border-radius: 9999px;
}

</style>
