<script setup lang="ts">
/**
 * An import in flight: the step the job is at, and the way to its background task. The page reads the
 * import again every few seconds while it runs, so the step moves by itself.
 *
 * **The link opens the import's own task.** The job records one task of kind `AccountImport` whose
 * subject is the account's name, so the link opens the tasks list narrowed to that kind and that name.
 *
 * The step is a module id (or `restore`), named in the administrator's language from the panel's own
 * chrome; a step this bundle has no word for is shown as the panel sent it, so a module added later
 * does not draw a dotted locale key.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiLink from '../ui/UiLink.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { accountImportTaskRoute } from '../../utils/accountImportTaskRoute'

/** Props accepted by {@link AccountImportProgress}. */
const props = defineProps<{
  /** The step the job is at, or `null` before the first one reports. */
  stage: string | null
  /** The account's name, which is the subject of the import's task. */
  accountName: string
}>()

const i18n = useI18n()

/** The step in the administrator's language, or the machine name when there is no word for it. */
const stageText: ComputedRef<string | null> = computed(() => {
  if (props.stage === null) {
    return null
  }
  const key = `accountMove.import.stages.${props.stage}`
  return i18n.te(key) ? i18n.t(key) : props.stage
})
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3" data-testid="import-progress">
    <UiSectionHeading
      :title="i18n.t('accountMove.import.progress.title')"
      :subtitle="i18n.t('accountMove.import.progress.intro')"
    />
    <UiSpinner :label="stageText === null ? i18n.t('accountMove.import.progress.starting') : i18n.t('accountMove.import.progress.stage', { stage: stageText })" />
    <UiLink :to="accountImportTaskRoute(accountName)" trailing-icon="chevronRight">{{ i18n.t('accountMove.import.progress.tasks') }}</UiLink>
  </div>
</template>
