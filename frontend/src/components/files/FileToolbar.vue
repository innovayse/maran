<script setup lang="ts">
/**
 * The row of controls above the listing: a way up one folder, the name search, the toggle for names
 * beginning with a dot, select-all for the page and a refresh. Props in, events out; the page owns
 * the state and the URL.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSearchInput from '../ui/UiSearchInput.vue'
import UiSwitch from '../ui/UiSwitch.vue'

/** Props accepted by {@link FileToolbar}. */
defineProps<{
  /** The search box's text. */
  search: string
  /** Whether names beginning with a dot are shown. */
  showHidden: boolean
  /** Whether every row of the page is ticked. */
  allSelected: boolean
  /** Whether there is a row to tick. */
  canSelect: boolean
  /** Whether a folder above the current one exists. */
  canGoUp: boolean
}>()

/** Events emitted by {@link FileToolbar}. */
const emit = defineEmits<{
  /** The search text changed. */
  (e: 'update:search', value: string): void
  /** The dot-name toggle changed. */
  (e: 'update:showHidden', value: boolean): void
  /** Select-all was ticked or cleared. */
  (e: 'selectAll', selected: boolean): void
  /** The person asked for the folder above. */
  (e: 'up'): void
  /** The person asked to read the folder again. */
  (e: 'refresh'): void
}>()

const { t } = useI18n()
</script>

<template>
  <div class="flex flex-wrap items-end gap-3">
    <UiButton variant="secondary" :disabled="!canGoUp" @click="emit('up')">
      <UiIcon name="chevronLeft" size="sm" />{{ t('filesBrowser.toolbar.up') }}
    </UiButton>
    <UiSearchInput
      :model-value="search"
      class="min-w-55 max-w-85 flex-1"
      :label="t('filesBrowser.toolbar.searchLabel')"
      :placeholder="t('filesBrowser.toolbar.searchPlaceholder')"
      :clear-label="t('filesBrowser.toolbar.searchClear')"
      @update:model-value="emit('update:search', $event)"
    />
    <UiSwitch
      :model-value="showHidden"
      :label="t('filesBrowser.toolbar.showHidden')"
      @update:model-value="emit('update:showHidden', $event)"
    />
    <UiCheckbox
      :model-value="allSelected"
      :label="t('filesBrowser.toolbar.selectAll')"
      :disabled="!canSelect"
      @update:model-value="emit('selectAll', $event)"
    />
    <UiButton variant="ghost" size="icon" :aria-label="t('filesBrowser.toolbar.refresh')" @click="emit('refresh')">
      <UiIcon name="rotateCw" size="md" />
    </UiButton>
  </div>
</template>
