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
 * The trail is derived, not stored, from the same sources the sidebar is drawn from
 * (`utils/locateNavigation.ts`, issue #209): the group the screen's entry is drawn under, the
 * entry, the submenu page, and — for a screen one level below those, such as a record or a form —
 * that screen too. A record's crumb is its name (an account's user name, a site's domain), read from
 * the page's own heading (`utils/pageHeadingKey.ts`), so the two are one string; while the record
 * loads the crumb is a placeholder, never the id from the address. A screen outside the navigation is named by its own title (`meta.title`). No crumb
 * is ever a route name or a URL segment: the settings screens once read "Panel / audit", a router
 * identifier in English whatever the language (issue #72), and a child screen "Panel / accounts-new".
 * Labels come from i18n keys and the panel's own localized module names, so the trail follows the
 * interface language.
 */
import { computed, inject, nextTick, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import UiSkeleton from '../ui/UiSkeleton.vue'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { moduleNavigationChildren } from '../../router/moduleNavigationChildren'
import { FALLBACK_GROUP } from '../../utils/groupNavigation'
import { locateNavigation } from '../../utils/locateNavigation'
import { PAGE_HEADING_KEY } from '../../utils/pageHeadingKey'
import type { NavigationEntry } from '../../types/navigation'
import type { PageHeading } from '../../types/pageHeading'

/** One crumb of the trail. */
interface Crumb {
  /** Stable identifier for `:key`: the crumb's level. */
  key: 'group' | 'entry' | 'child' | 'leaf'
  /** The crumb's text, or `null` while the record it names is loading (drawn as a placeholder). */
  text: string | null
}

/** Props accepted by {@link ShellBreadcrumb}. */
const props = defineProps<{
  /** Navigation entries the shell is showing, used to name a module-backed screen. */
  entries: readonly NavigationEntry[]
}>()

const { t } = useI18n()
const route = useRoute()

/** The page heading on screen (`utils/pageHeadingKey.ts`); a record screen's trail ends on its title. */
const pageHeading: Ref<PageHeading | null> | null = inject(PAGE_HEADING_KEY, null)

/** The `<nav>`: its width is the room the header gave the trail. */
const root: Ref<HTMLElement | null> = ref(null)

/** The trail's list, laid out at its natural width whatever the room. */
const list: Ref<HTMLElement | null> = ref(null)

/** Whether the whole trail fits the room it was given; while it does not, it is not drawn. */
const fits: Ref<boolean> = ref(true)

/** Watches the room the header gives the trail. */
let observer: ResizeObserver | null = null

/**
 * The trail for the active route: group, entry, submenu page, leaf — as deep as the screen sits.
 */
const crumbs: ComputedRef<Crumb[]> = computed(() => {
  const location = locateNavigation(route)
  const trail: Crumb[] = [{ key: 'group', text: t(`app.nav.groups.${location?.groupKey ?? FALLBACK_GROUP}`) }]
  if (location === null) {
    return [...trail, { key: 'leaf', text: t(route.meta.title ?? 'app.title') }]
  }

  // The entry is named as the sidebar names it. Until the catalogue has arrived it is not drawn,
  // so the trail never shows a module's machine name in its place.
  const entry = props.entries.find((candidate): boolean => {
    return candidate.key === location.entryKey
  })
  if (entry !== undefined) {
    trail.push({ key: 'entry', text: entryLabel(entry) })
  }

  const child = moduleNavigationChildren(location.entryKey).find((candidate): boolean => {
    return candidate.route === location.childRoute
  })
  if (child !== undefined) {
    trail.push({ key: 'child', text: t(child.labelKey) })
  }

  if (location.leaf) {
    trail.push({ key: 'leaf', text: leafText() })
  }
  return trail
})

/**
 * Whether a record crumb's placeholder is drawn: only once the record has been on its way for the
 * same 150 ms every other placeholder waits, so a quick answer never flashes one in the app bar.
 */
const placeholderShown: Readonly<Ref<boolean>> = useDelayedFlag((): boolean => {
  return route.meta.titledByRecord === true && pageHeading?.value?.loading === true
})

/**
 * Names a navigation entry the way the sidebar does: the shell's own key, else the panel's name.
 * @param entry The entry to name.
 * @returns Its label.
 */
const entryLabel = (entry: NavigationEntry): string => {
  return entry.labelKey === null ? (entry.label ?? entry.key) : t(entry.labelKey)
}

/**
 * The last crumb of a screen one level below a navigation destination. On a record screen it is the
 * record's name as the page heading shows it, a placeholder while that heading is loading, and the
 * screen's title when no heading names a record (the record does not exist, or could not be read).
 * Anywhere else it is the screen's title.
 * @returns The leaf's text, or `null` while the record is loading.
 */
const leafText = (): string | null => {
  const title = t(route.meta.title ?? 'app.title')
  const heading = pageHeading?.value ?? null
  if (route.meta.titledByRecord !== true || heading === null) {
    return title
  }
  if (heading.loading) {
    return null
  }
  return heading.title === '' ? title : heading.title
}

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
        :key="crumb.key"
        class="[&+&]:before:mr-1.5 [&+&]:before:text-text-muted [&+&]:before:content-['/']"
        :class="index === crumbs.length - 1 ? 'font-semibold text-text-primary' : 'text-text-secondary'"
      >
        <!-- A record still on its way: a placeholder the width of a short name, not its raw id. -->
        <span v-if="crumb.text === null" data-ui="crumb-loading" class="inline-flex w-24 align-middle">
          <UiSkeleton v-if="placeholderShown" shape="line" />
        </span>
        <template v-else>{{ crumb.text }}</template>
      </li>
    </ol>
  </nav>
</template>
