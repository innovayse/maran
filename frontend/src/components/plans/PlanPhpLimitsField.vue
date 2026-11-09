<script setup lang="ts">
/**
 * The four PHP bounds of a plan (issue #138): the most a customer on the plan may set for the PHP
 * memory limit, the upload size, the script execution time and the number of input variables.
 * Each is a whole number of at least 1; the agent's own ceiling for it is enforced by the server,
 * whose already-localized refusal is what the page shows, so this field holds no ceiling.
 *
 * Dumb by the usual contract: the parent holds the four texts and the validation, this draws them.
 * Numbers are typed as text for the reason `PlanForm` gives: the browser's own spinner and
 * validation bubble stay out of the panel.
 */
import { useI18n } from 'vue-i18n'
import UiInput from '../ui/UiInput.vue'

/** Props accepted by {@link PlanPhpLimitsField}. */
defineProps<{
  /** The already-translated validation message for each figure, or `null` when it is acceptable. */
  errors: {
    /** The message for the memory bound. */
    memoryLimitMaxMb: string | null
    /** The message for the upload bound. */
    uploadMaxMb: string | null
    /** The message for the execution-time bound. */
    maxExecutionSeconds: string | null
    /** The message for the input-variables bound. */
    maxInputVars: string | null
  }
}>()

const { t } = useI18n()

/** The largest `memory_limit` as typed, in mebibytes. */
const memoryLimitMaxMb = defineModel<string>('memoryLimitMaxMb', { required: true })

/** The largest upload size as typed, in mebibytes. */
const uploadMaxMb = defineModel<string>('uploadMaxMb', { required: true })

/** The longest execution time as typed, in seconds. */
const maxExecutionSeconds = defineModel<string>('maxExecutionSeconds', { required: true })

/** The largest input-variable count as typed. */
const maxInputVars = defineModel<string>('maxInputVars', { required: true })
</script>

<template>
  <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <UiInput
      v-model="memoryLimitMaxMb"
      class="min-w-0"
      :label="t('plans.form.fields.phpMemoryLimitMaxMb')"
      :error="errors.memoryLimitMaxMb"
      inputmode="numeric"
      mono
      required
    />
    <UiInput
      v-model="uploadMaxMb"
      class="min-w-0"
      :label="t('plans.form.fields.phpUploadMaxMb')"
      :error="errors.uploadMaxMb"
      inputmode="numeric"
      mono
      required
    />
    <UiInput
      v-model="maxExecutionSeconds"
      class="min-w-0"
      :label="t('plans.form.fields.phpMaxExecutionSeconds')"
      :error="errors.maxExecutionSeconds"
      inputmode="numeric"
      mono
      required
    />
    <UiInput
      v-model="maxInputVars"
      class="min-w-0"
      :label="t('plans.form.fields.phpMaxInputVars')"
      :error="errors.maxInputVars"
      inputmode="numeric"
      mono
      required
    />
  </div>
</template>
