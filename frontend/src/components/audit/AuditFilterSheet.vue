<script setup lang="ts">
/**
 * The audit journal's filter sheet: action, outcome, and the date window. There is no account
 * field (the journal records no account) and no actor field (the search reaches the actor). The
 * actions offered are the ones the panel says the journal holds, named by the panel. The window is
 * picked as calendar days with `UiDateInput`; turning a day into an instant is the API composable's
 * job. Values go out on Apply exactly as `UiFilterSheet` emits them; the page writes them to the URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDateInput from '../ui/UiDateInput.vue'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import { AUDIT_OUTCOMES, type AuditActionChoice } from '../../types/audit'

/** Props accepted by {@link AuditFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is shown. */
  open: boolean
  /** The applied filters by name (`action`, `outcome`, `from`, `to`), empty when unset. */
  modelValue: Record<string, string>
  /** The actions the journal holds, as the panel named them. */
  actions: AuditActionChoice[]
}>()

/** Events emitted by {@link AuditFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the filters; carries every field. */
  (e: 'update:modelValue', value: Record<string, string>): void
  /** The reader dismissed or applied the sheet. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The actions the filter offers, after an "any action" choice. */
const actionOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.actions.map((choice) => {
      return { value: choice.action, label: choice.actionName }
    }),
  ]
})

/** The outcomes the filter offers, after an "any outcome" choice. */
const outcomeOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...AUDIT_OUTCOMES.map((outcome) => {
      return { value: outcome, label: t(`app.audit.${outcome}`) }
    }),
  ]
})

/**
 * Passes the applied filters up.
 * @param value Every field of the sheet.
 * @returns Nothing.
 */
const apply = (value: Record<string, string>): void => {
  emit('update:modelValue', value)
}

/**
 * Passes the dismissal up.
 * @returns Nothing.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiFilterSheet
    :open="open"
    :model-value="modelValue"
    :title="t('common.list.filters')"
    :close-label="t('common.close')"
    :apply-label="t('common.list.apply')"
    :reset-label="t('common.list.reset')"
    @update:model-value="apply"
    @close="close"
  >
    <template #default="{ draft }">
      <UiSelect v-model="draft.action" :label="t('app.audit.filters.action')" :options="actionOptions" />
      <UiSelect v-model="draft.outcome" :label="t('app.audit.filters.outcome')" :options="outcomeOptions" />
      <UiDateInput v-model="draft.from" :label="t('app.audit.filters.from')" />
      <UiDateInput v-model="draft.to" :label="t('app.audit.filters.to')" />
    </template>
  </UiFilterSheet>
</template>
