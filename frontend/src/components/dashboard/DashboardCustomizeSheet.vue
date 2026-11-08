<script setup lang="ts">
/**
 * Where a person arranges their own dashboard: switch each widget and each shortcut on or off and
 * move it up or down. The changes are a DRAFT, copied from the layout on every opening, so closing
 * without saving (Escape, the backdrop, Cancel) discards them; Save hands the whole draft to the
 * page, which stores it.
 *
 * **Reordering is buttons, never drag alone.** Each row has "move up" and "move down" buttons named
 * with the row they move, so the order can be changed by a keyboard and by a screen reader; the
 * first row cannot move up and the last cannot move down (the button is disabled, not hidden, so
 * the row does not change shape). The on/off control is a switch whose label is the row's own name.
 *
 * **Keys this bundle does not know are kept, not shown.** The panel may list a widget a newer
 * release added; this sheet cannot name it, so it is not offered, but it stays in the draft in its
 * place and goes back with the rest, so saving here never silently deletes it.
 *
 * It opens as a `UiModal` at every width. A bottom sheet below `lg` is how list filters open; the
 * kit has no generic sheet yet (only the filter sheet, whose draft is a string map), so a second
 * surface here would copy its chrome.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { QUICK_ACTIONS } from '../../router/quickActions'
import type { DashboardLayout, DashboardLayoutEntry } from '../../types/dashboardLayout'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiModal from '../ui/UiModal.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSwitch from '../ui/UiSwitch.vue'

/** Which of the layout's two lists a row belongs to. */
type ListName = 'widgets' | 'actions'

const props = defineProps<{
  /** Whether the sheet is shown; owned by the page. */
  open: boolean
  /** The layout the panel holds; the draft starts from it on every opening. */
  layout: DashboardLayout
  /** The widget keys this bundle can draw. */
  widgetKeys: readonly string[]
  /** True while a save is on its way; the buttons are then disabled. */
  saving: boolean
  /** The panel's reason the last save was refused, or `null`. */
  errorMessage: string | null
}>()

const emit = defineEmits<{
  /** The person dismissed the sheet without saving. */
  (e: 'close'): void
  /** The person pressed Save; carries the whole draft. */
  (e: 'save', layout: DashboardLayout): void
}>()

/** The two lists, in the order the sheet shows them. */
const LISTS: readonly ListName[] = ['widgets', 'actions']

const { t } = useI18n()

/** The edits not yet saved. */
const draft: Ref<DashboardLayout> = ref({ widgets: [], actions: [] })

/**
 * Whether this bundle can draw or offer a key.
 * @param list Which list the key is in.
 * @param key The panel's key.
 * @returns True when the sheet can name it.
 */
const isKnown = (list: ListName, key: string): boolean => {
  return list === 'widgets'
    ? props.widgetKeys.includes(key)
    : QUICK_ACTIONS.some((action) => {
        return action.key === key
      })
}

/**
 * The rows of one list that the sheet shows (the keys it knows), in draft order.
 * @param list Which list.
 * @returns The known entries.
 */
const shown = (list: ListName): DashboardLayoutEntry[] => {
  return draft.value[list].filter((entry) => {
    return isKnown(list, entry.key)
  })
}

/**
 * The words naming a row.
 * @param list Which list the row is in.
 * @param key The row's key.
 * @returns The translated name.
 */
const nameOf = (list: ListName, key: string): string => {
  const action = QUICK_ACTIONS.find((candidate) => {
    return candidate.key === key
  })
  return list === 'actions' && action !== undefined ? t(action.labelKey) : t(`liveDashboard.widgets.${key}`)
}

/**
 * Turns one row on or off.
 * @param list Which list.
 * @param key The row's key.
 * @param on True to show it.
 * @returns Nothing.
 */
const setShown = (list: ListName, key: string, on: boolean): void => {
  draft.value = {
    ...draft.value,
    [list]: draft.value[list].map((entry) => {
      return entry.key === key ? { ...entry, hidden: !on } : entry
    }),
  }
}

/**
 * Moves a row one place among the rows the sheet shows, swapping with its shown neighbour so keys
 * the sheet does not know keep their own places.
 * @param list Which list.
 * @param key The row's key.
 * @param step -1 to move up, 1 to move down.
 * @returns Nothing.
 */
const move = (list: ListName, key: string, step: -1 | 1): void => {
  const entries = [...draft.value[list]]
  const visible = shown(list)
  const at = visible.findIndex((entry) => {
    return entry.key === key
  })
  const neighbour = visible[at + step]
  if (at === -1 || neighbour === undefined) {
    return
  }
  const from = entries.findIndex((entry) => {
    return entry.key === key
  })
  const to = entries.findIndex((entry) => {
    return entry.key === neighbour.key
  })
  const moving = entries[from]
  entries[from] = entries[to]
  entries[to] = moving
  draft.value = { ...draft.value, [list]: entries }
}

/**
 * Hands the draft to the page.
 * @returns Nothing.
 */
const save = (): void => {
  emit('save', draft.value)
}

// Every opening starts from the layout the panel holds, so edits abandoned last time are gone.
watch(
  (): boolean => {
    return props.open
  },
  (isOpen: boolean): void => {
    if (isOpen) {
      draft.value = {
        widgets: props.layout.widgets.map((entry) => {
          return { ...entry }
        }),
        actions: props.layout.actions.map((entry) => {
          return { ...entry }
        }),
      }
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiModal :open="open" :title="t('liveDashboard.customize.title')" :close-label="t('common.close')" @close="emit('close')">
    <div class="flex flex-col gap-6" data-testid="customize-sheet">
      <UiAlert v-if="errorMessage !== null" variant="error">{{ errorMessage }}</UiAlert>

      <section v-for="list in LISTS" :key="list">
        <UiSectionHeading class="mb-3" :title="t(`liveDashboard.customize.${list}`)" />
        <ul class="m-0 flex list-none flex-col gap-2 p-0">
          <li
            v-for="(entry, index) in shown(list)"
            :key="entry.key"
            class="flex items-center gap-2 rounded-control border border-border-subtle bg-surface-2 px-3 py-2"
            :data-testid="`customize-row-${list}-${entry.key}`"
          >
            <div class="min-w-0 flex-1">
              <UiSwitch
                :model-value="!entry.hidden"
                :label="nameOf(list, entry.key)"
                @update:model-value="setShown(list, entry.key, $event)"
              />
            </div>
            <UiButton
              variant="ghost"
              size="icon-sm"
              :disabled="index === 0"
              :aria-label="t('liveDashboard.customize.moveUp', { name: nameOf(list, entry.key) })"
              @click="move(list, entry.key, -1)"
            >
              <UiIcon name="chevronUp" />
            </UiButton>
            <UiButton
              variant="ghost"
              size="icon-sm"
              :disabled="index === shown(list).length - 1"
              :aria-label="t('liveDashboard.customize.moveDown', { name: nameOf(list, entry.key) })"
              @click="move(list, entry.key, 1)"
            >
              <UiIcon name="chevronDown" />
            </UiButton>
          </li>
        </ul>
      </section>
    </div>

    <template #footer>
      <UiButton variant="secondary" :disabled="saving" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton :disabled="saving" data-testid="customize-save" @click="save">{{ t('liveDashboard.customize.save') }}</UiButton>
    </template>
  </UiModal>
</template>
