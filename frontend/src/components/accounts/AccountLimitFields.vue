<script setup lang="ts">
/**
 * The per-account resource limits of the create form (issue #194): processes, inodes and open
 * files. An empty field follows the plan; a figure replaces it for this account only. The process
 * field prints the plan's own ceiling as its placeholder so the operator sees what an empty field
 * means. A reseller may only lower a limit below its plan, which the hint says; the server decides.
 */
import { useI18n } from 'vue-i18n'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiInput from '../ui/UiInput.vue'

defineProps<{
  /** Message under the process field, already translated; `null` when it is fine. */
  processError: string | null
  /** Message under the inode field, already translated; `null` when it is fine. */
  inodeError: string | null
  /** Message under the open-files field, already translated; `null` when it is fine. */
  openFilesError: string | null
  /** What an empty process field means (the plan's ceiling, in words); empty when unknown. */
  processDefault: string
  /** Whether the caller is a reseller, who may only lower a limit. */
  lowerOnly: boolean
}>()

/** The process limit as typed (`v-model:processes`). */
const processes = defineModel<string>('processes', { required: true })

/** The inode limit as typed (`v-model:inodes`). */
const inodes = defineModel<string>('inodes', { required: true })

/** The open-files limit as typed (`v-model:openFiles`). */
const openFiles = defineModel<string>('openFiles', { required: true })

const { t } = useI18n()
</script>

<template>
  <fieldset class="flex min-w-0 flex-col gap-3 md:col-span-2">
    <legend class="text-sm font-medium text-text-primary">{{ t('accounts.form.limits.title') }}</legend>
    <UiFieldHint>
      {{ lowerOnly ? t('accounts.form.limits.hintLowerOnly') : t('accounts.form.limits.hint') }}
    </UiFieldHint>
    <div class="grid grid-cols-1 gap-x-4 gap-y-4 md:grid-cols-3">
      <UiInput
        v-model="processes"
        :label="t('accounts.form.fields.processLimit')"
        :error="processError"
        :placeholder="processDefault"
        inputmode="numeric"
        mono
      />
      <UiInput
        v-model="inodes"
        :label="t('accounts.form.fields.inodeLimit')"
        :error="inodeError"
        :placeholder="t('accounts.form.limits.followPlan')"
        inputmode="numeric"
        mono
      />
      <UiInput
        v-model="openFiles"
        :label="t('accounts.form.fields.openFilesLimit')"
        :error="openFilesError"
        :placeholder="t('accounts.form.limits.followPlan')"
        inputmode="numeric"
        mono
      />
    </div>
    <UiFieldHint>{{ t('accounts.form.limits.zeroMeansNone') }}</UiFieldHint>
  </fieldset>
</template>
