<script setup lang="ts">
/**
 * The filter sheet of a migration list whose only filter is the state a row is in (the imports and
 * the pull grants). The states are a closed set the panel reports and the page names them, so the
 * sheet takes the choices and the field's label as props and draws a "any" choice before them.
 * Props in, events out: the page owns the applied filters in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'

/** Props accepted by {@link MigrationStateFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is open. */
  open: boolean
  /** The filters applied now, which the sheet's draft starts from. */
  filters: Record<string, string>
  /** The state field's label, already translated. */
  label: string
  /** The states a row can be in, each with its already translated name. */
  states: readonly { value: string; label: string }[]
}>()

/** Events emitted by {@link MigrationStateFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft. */
  (e: 'apply', filters: Record<string, string>): void
  /** The reader closed the sheet. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The state choices, led by "any". */
const stateOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [{ value: '', label: t('common.list.any') }, ...props.states]
})

/**
 * Forwards the applied draft.
 * @param filters The draft the reader applied.
 * @returns Nothing; the event is emitted synchronously.
 */
const apply = (filters: Record<string, string>): void => {
  emit('apply', filters)
}

/**
 * Forwards a close.
 * @returns Nothing; the event is emitted synchronously.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiFilterSheet
    :open="open"
    :model-value="filters"
    :title="t('common.list.filters')"
    :close-label="t('common.close')"
    :apply-label="t('common.list.apply')"
    :reset-label="t('common.list.reset')"
    @update:model-value="apply"
    @close="close"
  >
    <template #default="{ draft }">
      <UiSelect v-model="draft.state" :label="label" :options="stateOptions" />
    </template>
  </UiFilterSheet>
</template>
