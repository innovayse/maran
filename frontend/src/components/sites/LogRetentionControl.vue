<script setup lang="ts">
/**
 * How many days of rotated logs a site keeps: one select over the retentions the panel offers and
 * a save button. Props in, one event out; the page owns the request.
 *
 * The options are whatever `GET /api/v1/sites/choices` reported, already labelled by the panel — the
 * SPA holds no list of retentions (rules/vue.md: "the frontend never invents domain data"). The
 * form is `novalidate` and applies no rule of its own: the server decides what is allowed, and when
 * it refuses, its already-localized sentence is shown here as plain text.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { SiteChoice } from '../../types/site'
import type { RetentionOutcome } from '../../types/siteTraffic'

/** Props accepted by {@link LogRetentionControl}. */
const props = defineProps<{
  /** The retention the site keeps now, in days, or `null` while the site is still being read. */
  current: number | null
  /** The retentions the panel offers, in the order they read; empty until the panel has answered. */
  options: readonly SiteChoice[]
  /** Whether a change is being saved. */
  saving: boolean
  /** How the last change ended. */
  outcome: RetentionOutcome
  /** The panel's already-localized reason the last change was refused, or `null` when it did not answer. */
  message: string | null
}>()

/** Events emitted by {@link LogRetentionControl}. */
const emit = defineEmits<{
  /**
   * The operator asked to keep logs for a different number of days.
   * @param e Event name.
   * @param days The retention chosen, in days.
   */
  (e: 'save', days: number): void
}>()

const { t } = useI18n()

/** The option picked in the select, as its value text; empty until a current retention is known. */
const chosen: Ref<string> = ref('')

/** The offered retentions as select options; the value is read as text whatever the wire carried. */
const selectOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.options.map((option) => {
    return { value: String(option.value), label: option.label }
  })
})

/** Whether the pick differs from what the site keeps now, which is the only time saving means anything. */
const canSave: ComputedRef<boolean> = computed(() => {
  return props.current !== null && chosen.value !== '' && chosen.value !== String(props.current)
})

/**
 * Sends the pick up to the page.
 * @returns Nothing; the event is emitted synchronously.
 */
const submit = (): void => {
  if (canSave.value) {
    emit('save', Number(chosen.value))
  }
}

// The pick follows what the site keeps: when the site is read, and again after a save.
watch(
  () => {return props.current},
  (days) => {
    chosen.value = days === null ? '' : String(days)
  },
  { immediate: true },
)
</script>

<template>
  <UiForm :loading="current === null" class="flex min-w-0 flex-col gap-3" @submit="submit">
    <UiSelect
      v-model="chosen"
      :label="t('siteTraffic.retention.label')"
      :placeholder="t('siteTraffic.retention.placeholder')"
      :options="selectOptions"
      :disabled="saving"
    />
    <UiFieldHint>{{ t('siteTraffic.retention.hint') }}</UiFieldHint>
    <UiAlert v-if="outcome === 'failed'" variant="error">
      {{ message ?? t('app.status.unreachable') }}
    </UiAlert>
    <UiAlert v-else-if="outcome === 'saved'" variant="success">
      {{ t('siteTraffic.retention.saved') }}
    </UiAlert>
    <div>
      <UiButton type="submit" :disabled="!canSave || saving">
        {{ saving ? t('siteTraffic.retention.saving') : t('siteTraffic.retention.save') }}
      </UiButton>
    </div>
  </UiForm>
</template>
