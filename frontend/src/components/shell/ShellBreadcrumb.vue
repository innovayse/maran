<script setup lang="ts">
/**
 * The app bar's breadcrumb: the navigation group, then the screen the person is on.
 *
 * It sits in the app bar only while the bar has room beyond the search field (the header
 * decides, by the bar's own width, so a large text size takes it away exactly as a
 * narrower window does), and it is drawn WHOLE or not at all: when the trail is wider than
 * the room it was given it hides itself rather than shortening. The page's own title already
 * says where the person is, and a trail truncated to "Pa…" says nothing. It is hidden with
 * `invisible`, not `display: none`, because its natural width must stay measurable to know
 * when it fits again, and `visibility: hidden` also removes it from the accessibility tree.
 * It is a `<nav>` with its own
 * translated name so it stays distinguishable from the sidebar's navigation landmark.
 *
 * The trail is derived, not stored. The equivalent of a screen→path map is
 * {@link SHELL_ROUTE_LABEL_KEYS} for the shell's own screens plus the module
 * catalogue for everything module-backed, so the trail never names a screen the
 * panel did not report. Because the labels come from i18n keys and the panel's
 * own localized module names, the trail follows the interface language.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { NavigationEntry } from '../../types/navigation'

/**
 * i18n keys naming the shell's own screens in the breadcrumb trail. Only routes this
 * bundle owns appear here — a module's screen is named by the catalogue entry the
 * panel sent, never by a key invented in the frontend.
 */
const SHELL_ROUTE_LABEL_KEYS: Record<string, string> = {
  'system-status': 'app.nav.systemStatus',
  upgrade: 'app.upgrade.heading',
  'not-found': 'app.notFound.heading',
  // Every screen under /settings. They were missing once, so each fell through to its own route
  // name and the first line of the page read "Panel / audit" — a router identifier, lowercase and
  // in English, whatever language the operator chose. These are the screens reached most often
  // from the account menu.
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
 * i18n keys naming a screen that sits BENEATH a module, appended after the module's own
 * crumb. Without this a child route fell through to its raw route name and the trail read
 * "Panel / accounts-new" — a router identifier shown to a customer, in English, whatever
 * language they chose.
 */
const CHILD_ROUTE_LABEL_KEYS: Record<string, string> = {
  'accounts-new': 'accounts.form.heading',
  plans: 'plans.list.heading',
  'plans-new': 'plans.form.headingNew',
  'plans-edit': 'plans.form.headingEdit',
}

/** Props accepted by {@link ShellBreadcrumb}. */
const props = defineProps<{
  /** Navigation entries the shell is showing, used to name a module-backed screen. */
  entries: readonly NavigationEntry[]
}>()

const { t } = useI18n()
const route = useRoute()

/** The `<nav>`: its width is the room the header gave the trail. */
const root: Ref<HTMLElement | null> = ref(null)

/** The trail's list, laid out at its natural width whatever the room. */
const list: Ref<HTMLElement | null> = ref(null)

/** Whether the whole trail fits the room it was given; while it does not, it is not drawn. */
const fits: Ref<boolean> = ref(true)

/** Watches the room the header gives the trail. */
let observer: ResizeObserver | null = null

/**
 * The trail for the active route. One group is all the catalogue supports today (it
 * reports no grouping), so the trail is two levels deep, three on a child screen.
 */
const crumbs: ComputedRef<string[]> = computed(() => {
  const routeName = typeof route.name === 'string' ? route.name : ''
  const shellLabelKey = SHELL_ROUTE_LABEL_KEYS[routeName]
  if (shellLabelKey !== undefined) {
    return [t('app.nav.groups.panel'), t(shellLabelKey)]
  }

  // A module screen is named by what the panel called the module. The route's `meta.module` is
  // the authority — matching on the route NAME missed every child route, whose name
  // ('accounts-new') is not the module's ('accounts').
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

/**
 * Compares the trail's natural width with the room it has.
 * @returns Nothing; {@link fits} is updated.
 */
const measure = (): void => {
  if (root.value === null || list.value === null) {
    return
  }
  fits.value = list.value.offsetWidth <= root.value.clientWidth
}

onMounted((): void => {
  if (root.value !== null) {
    observer = new ResizeObserver(measure)
    observer.observe(root.value)
  }
  measure()
})

onBeforeUnmount((): void => {
  observer?.disconnect()
})

// A new route or a new language changes the trail's own width without changing the room.
watch(crumbs, async (): Promise<void> => {
  await nextTick()
  measure()
})
</script>

<template>
  <nav
    ref="root"
    class="min-w-0 overflow-hidden"
    :class="fits ? '' : 'invisible'"
    :aria-label="t('app.shell.breadcrumbLabel')"
  >
    <ol ref="list" class="flex w-max items-center gap-1.5 text-sm whitespace-nowrap">
      <!-- The "/" separators are drawn from CSS so they stay out of the accessible name and out
           of the locale files: a separator is punctuation, not copy. -->
      <li
        v-for="(crumb, index) in crumbs"
        :key="crumb"
        class="[&+&]:before:mr-1.5 [&+&]:before:text-text-muted [&+&]:before:content-['/']"
        :class="index === crumbs.length - 1 ? 'font-semibold text-text-primary' : 'text-text-secondary'"
      >
        {{ crumb }}
      </li>
    </ol>
  </nav>
</template>
