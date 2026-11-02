<script setup lang="ts">
/**
 * The star laid over a navigation row's end that pins the page to, or removes it from, the
 * person's favourites (issue #318). It is drawn by the sidebar and by the phone drawer, which
 * renders the same sidebar, so one control serves both.
 *
 * **It sits beside the row's link, never inside it**: a button inside a link is invalid markup and
 * would share the link's accessible name. The caller wraps the link and this toggle in a
 * `relative` box, gives the link `trailing` so the label leaves room, and marks the link `peer`;
 * `UiNavRowAction` draws the star and follows the row's lit state.
 *
 * It carries `aria-pressed` and a name that says what pressing does ("Pin X to favourites" /
 * "Remove X from favourites"). When the person already holds as many pins as the panel allows, an
 * unpinned page's star is disabled and says so; that is advice from the limit the panel reported,
 * and the panel refuses the pin itself regardless. A pinned page's star always works, so a full
 * list can always be emptied.
 *
 * Only pages that can be opened get one. A planned page has no destination and a locked one leads
 * to the upgrade screen, so the caller draws no toggle for them.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiIcon from '../ui/UiIcon.vue'
import UiNavRowAction from '../ui/UiNavRowAction.vue'
import { useAuthStore } from '../../stores/auth'
import { useFavouritesStore } from '../../stores/favourites'

/** Props accepted by {@link ShellPinToggle}. */
const props = defineProps<{
  /** The page's key in the navigation tree. */
  itemKey: string
  /** The page's label, already translated, used to name the control. */
  label: string
}>()

const { t } = useI18n()
const store = useFavouritesStore()
const auth = useAuthStore()

/** Whether the page is among the person's pins. */
const pinned: ComputedRef<boolean> = computed(() => {
  return store.isPinned(props.itemKey)
})

/** Whether the star cannot be used: the list is full and this page is not in it. */
const blocked: ComputedRef<boolean> = computed(() => {
  return store.isFull && !pinned.value
})

/** The control's accessible name. */
const name: ComputedRef<string> = computed(() => {
  if (pinned.value) {
    return t('favourites.unpin', { name: props.label })
  }
  return blocked.value ? t('favourites.limitReached', { name: props.label }) : t('favourites.pin', { name: props.label })
})

/**
 * Pins or unpins the page.
 * @returns Resolves once the panel answered.
 */
const toggle = async (): Promise<void> => {
  await store.toggle(props.itemKey)
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
  <UiNavRowAction :label="name" :pressed="pinned" :disabled="blocked" :data-pin-key="itemKey" @press="toggle">
    <UiIcon name="star" size="sm" :class="pinned ? 'fill-current' : ''" />
  </UiNavRowAction>
</template>
