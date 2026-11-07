<script setup lang="ts">
/**
 * How long the audit journal keeps its entries: a number of days, or "keep forever". Reads the
 * setting from the panel and saves it back; the bounds of the day count arrive WITH the setting
 * (`minimumDays`, `maximumDays`) and are only shown here as the field's hint, never enforced: the
 * panel refuses a value outside them and its sentence is rendered as it came (rules/vue.md).
 *
 * The one thing the card does decide is whether a Save can be sent at all: with "keep forever" off,
 * the field must hold a whole number, because anything else has no day count to send and a
 * non-number turned into a number would reach the panel as "keep forever" — the opposite of what
 * was typed. A Save is likewise not sent before the panel has stated the setting: until then the
 * switch shows its default, and sending that would overwrite a stored limit with "keep forever".
 * That is advice for the form; the bounds stay the panel's.
 *
 * After a save the store reads the setting again, so the form shows what the panel stored.
 */
import { computed, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import { useAuditIntegrityStore } from '../../stores/auditIntegrity'
import { useLocaleStore } from '../../stores/locale'
import type { AuditRetention } from '../../types/audit'
import { formatDate } from '../../utils/formatDate'

/** A whole, non-negative number written in digits only. */
const WHOLE_NUMBER = /^\d+$/

const { t } = useI18n()
const store = useAuditIntegrityStore()
const localeStore = useLocaleStore()

/** Whether entries are kept indefinitely; the day field is inert while this is on. */
const keepForever: Ref<boolean> = ref(true)

/** The day count as typed. */
const days: Ref<string> = ref('')

/** The field's hint: the range the panel accepts, as figures only. Empty until the setting is read. */
const range: ComputedRef<string> = computed(() => {
  const retention = store.retention
  return retention === null ? '' : `${retention.minimumDays}–${retention.maximumDays}`
})

/** The day count to send: `null` to keep forever, the number typed otherwise, or `undefined` when the field cannot be read. */
const retentionDays: ComputedRef<number | null | undefined> = computed(() => {
  if (keepForever.value) {
    return null
  }
  const typed = days.value.trim()
  return WHOLE_NUMBER.test(typed) ? Number(typed) : undefined
})

/**
 * Whether a Save may be sent: only once the panel has stated the setting (before that the form's
 * "keep forever" is a default, not what is stored, and sending it would overwrite a real limit),
 * only when the field can be read, and not while a save is already on its way.
 */
const canSave: ComputedRef<boolean> = computed(() => {
  return store.retention !== null && retentionDays.value !== undefined && !store.savingRetention
})

/** When the daily run last ran, in the reader's language, or the empty-value mark before the first run. */
const lastRun: ComputedRef<string> = computed(() => {
  const at = store.retention?.lastRunAt ?? null
  return at === null ? t('common.emptyValue') : formatDate(at, localeStore.current)
})

/**
 * Saves the setting the form shows.
 * @returns Resolves once the panel answered.
 */
const save = async (): Promise<void> => {
  // Enter in the field submits the form without going through the button's disabled state.
  if (!canSave.value || retentionDays.value === undefined) {
    return
  }
  await store.saveRetentionDays(retentionDays.value)
}

onMounted(async () => {
  await store.loadRetention()
})

// The form follows what the panel holds: on the first read and after every save.
watch(
  (): AuditRetention | null => {
    return store.retention
  },
  (retention: AuditRetention | null): void => {
    if (retention !== null) {
      keepForever.value = retention.retentionDays === null
      days.value = retention.retentionDays === null ? '' : String(retention.retentionDays)
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiCard class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('app.audit.retention.heading')" :subtitle="t('app.audit.retention.description')" />

    <UiLoadingRegion :state="store.retentionLoadState">
      <template #error>
        <UiAlert variant="error">{{ store.retentionLoadMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiForm :loading="store.retention === null" class="flex flex-col gap-4" @submit="save">
        <UiSwitch v-model="keepForever" :label="t('app.audit.retention.keepForever')" />
        <UiInput
          v-model="days"
          :label="t('app.audit.retention.days')"
          :placeholder="range"
          :disabled="keepForever"
          inputmode="numeric"
          mono
        />

        <UiDescriptionList :loading="store.retention === null">
          <UiDescriptionItem :term="t('app.audit.retention.lastRun')">{{ lastRun }}</UiDescriptionItem>
        </UiDescriptionList>

        <UiAlert v-if="store.retentionSave.state === 'failed'" variant="error">
          {{ store.retentionSaveMessage ?? t('common.list.unreachable') }}
        </UiAlert>
        <UiAlert v-else-if="store.retentionSave.state === 'done'" variant="success">
          {{ t('app.audit.retention.saved') }}
        </UiAlert>

        <div>
          <UiButton type="submit" :disabled="!canSave">
            {{ store.savingRetention ? t('common.saving') : t('app.audit.retention.save') }}
          </UiButton>
        </div>
      </UiForm>
    </UiLoadingRegion>
  </UiCard>
</template>
