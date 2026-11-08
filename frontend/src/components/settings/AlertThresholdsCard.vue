<script setup lang="ts">
/**
 * The thresholds at which the panel alarms about the disk, the processor and the memory. The disk
 * is always watched; the processor and the memory each have an on/off box. The bounds come from the
 * panel, so this screen holds no constant of its own for them.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useAlertThresholdsStore } from '../../stores/alertThresholds'
import type { AlertThresholds } from '../../types/alertSettings'

const { t } = useI18n()
const store = useAlertThresholdsStore()

/** The disk threshold, as typed. Text, because the form is `novalidate` and the server bounds it. */
const disk: Ref<string> = ref('')

/** The processor threshold, as typed. */
const cpu: Ref<string> = ref('')

/** The memory threshold, as typed. */
const memory: Ref<string> = ref('')

/** Whether the processor is watched. */
const watchCpu: Ref<boolean> = ref(false)

/** Whether the memory is watched. */
const watchMemory: Ref<boolean> = ref(false)

/** The sentence under a field whose value is outside the bounds, keyed by field; empty when fine. */
const errors: Ref<Record<'disk' | 'cpu' | 'memory', string | null>> = ref({ disk: null, cpu: null, memory: null })

/**
 * Copies thresholds the panel reported into the form's fields.
 * @param thresholds The thresholds to show, or `null` while none have been read.
 * @returns Nothing; the fields are updated synchronously.
 */
const fill = (thresholds: AlertThresholds | null): void => {
  if (thresholds === null) {
    return
  }

  disk.value = String(thresholds.diskPercent)
  watchCpu.value = thresholds.cpuPercent !== null
  cpu.value = thresholds.cpuPercent === null ? '' : String(thresholds.cpuPercent)
  watchMemory.value = thresholds.memoryPercent !== null
  memory.value = thresholds.memoryPercent === null ? '' : String(thresholds.memoryPercent)
}

/**
 * Reads a typed threshold as a whole number inside the panel's bounds.
 * @param text The field as typed.
 * @param thresholds The thresholds whose bounds apply.
 * @returns The number, or `null` when the text is not a whole number inside the bounds.
 */
const percentOf = (text: string, thresholds: AlertThresholds): number | null => {
  const value = Number(text.trim())
  const inside = value >= thresholds.minimumPercent && value <= thresholds.maximumPercent

  return text.trim() !== '' && Number.isInteger(value) && inside ? value : null
}

/**
 * Saves the thresholds as typed, after the bounds check the server repeats.
 * @returns Resolves once the request has settled, or at once when a field is out of bounds.
 */
const submit = async (): Promise<void> => {
  const thresholds = store.thresholds
  if (thresholds === null) {
    return
  }

  const bounds = { min: thresholds.minimumPercent, max: thresholds.maximumPercent }
  const diskPercent = percentOf(disk.value, thresholds)
  const cpuPercent = watchCpu.value ? percentOf(cpu.value, thresholds) : null
  const memoryPercent = watchMemory.value ? percentOf(memory.value, thresholds) : null
  // A field that is not watched is never wrong; a watched one is wrong when it read as nothing.
  const cpuRefused = watchCpu.value && cpuPercent === null
  const memoryRefused = watchMemory.value && memoryPercent === null
  const refusal = t('alertSettings.thresholds.outOfBounds', bounds)
  errors.value = {
    disk: diskPercent === null ? refusal : null,
    cpu: cpuRefused ? refusal : null,
    memory: memoryRefused ? refusal : null,
  }
  if (diskPercent === null || cpuRefused || memoryRefused) {
    return
  }

  await store.save({ diskPercent, cpuPercent, memoryPercent })
}

// The form follows whatever the panel last reported, including after a save.
watch(
  () => {
    return store.thresholds
  },
  fill,
  { immediate: true },
)

void store.load()
</script>

<template>
  <UiCard>
    <UiSectionHeading
      class="mb-4"
      :title="t('alertSettings.thresholds.title')"
      :subtitle="t('alertSettings.thresholds.description')"
    />

    <div class="mb-4 flex flex-col gap-3 empty:hidden">
      <UiAlert v-if="store.submitMessage !== null" variant="error">{{ store.submitMessage }}</UiAlert>
      <UiAlert v-if="store.saved" variant="success">{{ t('alertSettings.thresholds.saved') }}</UiAlert>
    </div>

    <UiLoadingRegion :state="store.loadState">
      <template #error>
        <UiAlert variant="error">{{ store.loadMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <UiForm :loading="store.loadState === 'loading'" @submit="submit">
        <div class="flex flex-col gap-4">
          <UiInput
            v-model="disk"
            :label="t('alertSettings.thresholds.diskLabel')"
            :error="errors.disk"
            inputmode="numeric"
            mono
            required
          />

          <div class="flex flex-col gap-2">
            <UiCheckbox v-model="watchCpu" :label="t('alertSettings.thresholds.watchCpu')" />
            <UiInput
              v-if="watchCpu"
              v-model="cpu"
              :label="t('alertSettings.thresholds.cpuLabel')"
              :error="errors.cpu"
              inputmode="numeric"
              mono
              required
            />
          </div>

          <div class="flex flex-col gap-2">
            <UiCheckbox v-model="watchMemory" :label="t('alertSettings.thresholds.watchMemory')" />
            <UiInput
              v-if="watchMemory"
              v-model="memory"
              :label="t('alertSettings.thresholds.memoryLabel')"
              :error="errors.memory"
              inputmode="numeric"
              mono
              required
            />
          </div>

          <!-- A spike shorter than the rule is not an alert; saying so here saves a support question. -->
          <UiFieldHint>{{ t('alertSettings.thresholds.sustainedHint') }}</UiFieldHint>

          <div>
            <UiButton type="submit" :disabled="store.saving">{{ t('alertSettings.thresholds.save') }}</UiButton>
          </div>
        </div>
      </UiForm>
    </UiLoadingRegion>
  </UiCard>
</template>
