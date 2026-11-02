<script setup lang="ts">
/**
 * The app bar's favourites menu (issue #318): the pages the person pinned, one press from any
 * screen. A menu rather than a row of links, because up to eight labels do not fit an app bar
 * that already gives way to the breadcrumb, the search field and the language name on a phone.
 *
 * **The server stores keys; this menu decides what is drawn.** A pin is drawn only when its key
 * names a page the person can open right now, found in the navigation `useNavigation` built for
 * their role, the module catalogue and the plan. A pin for a page that has since been withdrawn
 * from them, or is still planned, stays stored and is simply not listed, so it comes back if the
 * page is offered again. Offering is advice: every endpoint behind a page enforces access itself.
 *
 * The menu is always present once the person is signed in, empty or not, so the bar does not
 * shift when the first pin lands: an empty menu says how to pin. A pin that could not be made
 * (the panel's own refusal, already localized) is shown as the menu's last row.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import { useAuthStore } from '../../stores/auth'
import { useFavouritesStore } from '../../stores/favourites'
import { navigationLabelText } from '../../utils/navigationLabelText'
import { openNavigationPages } from '../../utils/openNavigationPages'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellFavourites}. */
const props = defineProps<{
  /** The navigation entries the shell built for the signed-in person, as `useNavigation` returns them. */
  entries: readonly NavigationEntry[]
}>()

const { t } = useI18n()
const router = useRouter()
const store = useFavouritesStore()
const auth = useAuthStore()

/** The pinned pages that are openable now, in the order the person pinned them. */
const pages: ComputedRef<NavigationEntry[]> = computed(() => {
  const openable = new Map(
    openNavigationPages(props.entries).map((entry): [string, NavigationEntry] => {
      return [entry.key, entry]
    }),
  )
  return store.keys.flatMap((key): NavigationEntry[] => {
    const entry = openable.get(key)
    return entry === undefined ? [] : [entry]
  })
})

/**
 * Opens a pinned page.
 * @param entry The page's navigation entry.
 * @returns Resolves once the router settled.
 */
const open = async (entry: NavigationEntry): Promise<void> => {
  if (entry.state.kind === 'open') {
    await router.push(entry.state.target)
  }
}

/**
 * Makes sure the signed-in person's pins are held. Whichever component mounts first reads them;
 * the rest find them already held.
 * @returns Resolves once the pins are held or the read settled.
 */
const loadPins = async (): Promise<void> => {
  if (auth.user !== null) {
    await store.ensureLoaded(auth.user.id)
  }
}

onMounted(loadPins)
</script>

<template>
  <UiDropdown
    data-testid="favourites-menu"
    align="end"
    trigger-variant="ghost"
    trigger-size="bar"
    :label="t('favourites.menuLabel')"
    :aria-label="t('favourites.menuLabel')"
    :chevron="false"
  >
    <template #leading>
      <UiIcon name="star" size="lg" />
    </template>
    <template #trigger>
      <span class="max-lg:hidden @max-[62rem]:hidden">{{ t('favourites.menuLabel') }}</span>
    </template>

    <UiDropdownItem v-for="page in pages" :key="page.key" @select="open(page)">
      {{ navigationLabelText(page.label, t) }}
    </UiDropdownItem>
    <UiDropdownItem v-if="pages.length === 0" disabled>
      {{ t('favourites.empty') }}
    </UiDropdownItem>
    <UiDropdownItem v-if="store.errorMessage !== null" disabled>
      {{ store.errorMessage }}
    </UiDropdownItem>
  </UiDropdown>
</template>
