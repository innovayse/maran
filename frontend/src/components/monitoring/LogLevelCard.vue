<script setup lang="ts">
/**
 * The panel's runtime log level, on the monitoring screen (issue #131): which level the panel's own
 * logger writes at right now, and a choice to change it. A change takes effect for the next line
 * logged, survives a restart, and is recorded in the audit journal — the panel does all three; this
 * card only asks.
 *
 * Dumb by design (rules/vue.md): the level, the choices and the outcome of the last change arrive as
 * props, and a change leaves as an event. The choices and their names are the panel's, already in the
 * reader's language; the card offers exactly what it was sent.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { LoadingState } from '../../types/loading'
import type { LogLevel, PanelLogLevel } from '../../types/logLevel'

/** Props accepted by {@link LogLevelCard}. */
const props = defineProps<{
  /** The running level and the choices, or `null` before the panel has answered. */
  logLevel: LogLevel | null
  /** Where the read stands. */
  state: LoadingState
  /** True while a change is in flight. */
  saving: boolean
  /** True once the last change succeeded. */
  saved: boolean
  /** The panel's own message for the last failure, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link LogLevelCard}. */
const emit = defineEmits<{
  /** The administrator asked for this level. */
  (e: 'change', level: PanelLogLevel): void
}>()

const { t } = useI18n()

/** The level picked in the select, which starts at the running one. */
const selected: Ref<string> = ref('')

/** The panel's choices, as the select's options. */
const options: ComputedRef<SelectOption[]> = computed(() => {
  return (props.logLevel?.choices ?? []).map((choice) => {
    return { value: choice.level, label: choice.levelDisplayName }
  })
})

/** Whether the pick differs from the running level, so applying it would change anything. */
const isChanged: ComputedRef<boolean> = computed(() => {
  return props.logLevel !== null && selected.value !== props.logLevel.level
})

/**
 * Asks for the picked level.
 *
 * The select is a generic primitive and speaks in strings; the cast is confined here, and it holds
 * because every option's value is a level the panel itself listed in `choices`.
 * @returns Nothing; the parent applies it.
 */
const submit = (): void => {
  emit('change', selected.value as PanelLogLevel)
}

// The select follows the panel's answer, including the read-back after a change.
watch(
  () => {
    return props.logLevel?.level
  },
  (level) => {
    selected.value = level ?? ''
  },
  { immediate: true },
)
</script>

<template>
  <UiCard data-testid="log-level-card">
    <UiSectionHeading class="mb-4" :title="t('monitoring.logLevel.title')" :subtitle="t('monitoring.logLevel.hint')" />
    <UiLoadingRegion :state="state">
      <template #error>
        <UiAlert variant="error">{{ errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <UiForm :loading="state === 'loading'" @submit="submit">
        <div class="flex flex-col gap-4">
          <UiAlert v-if="state === 'loaded' && errorMessage !== null" variant="error">{{ errorMessage }}</UiAlert>
          <UiAlert v-if="saved" variant="success">{{ t('monitoring.logLevel.saved') }}</UiAlert>
          <UiSelect v-model="selected" :label="t('monitoring.logLevel.label')" :options="options" />
          <div class="flex justify-end">
            <UiButton type="submit" :disabled="saving || !isChanged">{{ t('monitoring.logLevel.apply') }}</UiButton>
          </div>
        </div>
      </UiForm>
    </UiLoadingRegion>
  </UiCard>
</template>
