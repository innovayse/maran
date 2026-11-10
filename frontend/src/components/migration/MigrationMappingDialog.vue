<script setup lang="ts">
/**
 * Maps one account of a draft job to a plan and an owner: the three things a source cannot say (the plan the
 * account is created against, the owner's e-mail and the primary domain), plus the owner's language and, for a
 * Maran source, the single-use pull grant. Props in, events out: the page owns the store and the plan list.
 *
 * Validation here is advice; the backend refuses a value that is not valid and the mapping is judged again by the
 * dry run and by the start.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiPasswordInput from '../ui/UiPasswordInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { MigrationJobAccount, MigrationMappingRequest } from '../../types/migrationJob'

/** Props accepted by {@link MigrationMappingDialog}. */
const props = defineProps<{
  /** The account being mapped, or `null` while the dialog is closed. */
  account: MigrationJobAccount | null
  /** The plans the administrator may choose from. */
  plans: SelectOption[]
  /** Whether the job's source is a Maran panel, which needs a pull grant for the account. */
  needsGrant: boolean
  /** Whether the save is in flight. */
  saving: boolean
  /** The panel's message for the last refused save, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link MigrationMappingDialog}. */
const emit = defineEmits<{
  /** The administrator saved the mapping. */
  (e: 'save', request: MigrationMappingRequest): void
  /** The administrator closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The chosen plan. */
const planId: Ref<string> = ref('')

/** The owner's e-mail. */
const ownerEmail: Ref<string> = ref('')

/** The owner's language. */
const ownerLanguage: Ref<string> = ref('en')

/** The primary domain. */
const primaryDomain: Ref<string> = ref('')

/** The pull grant, for a Maran source. */
const pullGrant: Ref<string> = ref('')

/** Whether a save was attempted, so that errors show only after. */
const submitted: Ref<boolean> = ref(false)

/** The languages the panel is translated into. */
const languages: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: 'en', label: t('migrationJobs.mapping.languages.en') },
    { value: 'ru', label: t('migrationJobs.mapping.languages.ru') },
    { value: 'hy', label: t('migrationJobs.mapping.languages.hy') },
  ]
})

/** The e-mail's error, shown after a save attempt. */
const emailError: ComputedRef<string | null> = computed(() => {
  return submitted.value && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(ownerEmail.value.trim()) ? t('migrationJobs.mapping.errors.email') : null
})

/** The plan's error, shown after a save attempt. */
const planError: ComputedRef<string | null> = computed(() => {
  return submitted.value && planId.value === '' ? t('migrationJobs.mapping.errors.plan') : null
})

/** The domain's error, shown after a save attempt. */
const domainError: ComputedRef<string | null> = computed(() => {
  return submitted.value && primaryDomain.value.trim() === '' ? t('migrationJobs.mapping.errors.domain') : null
})

// Each opening starts from what the row holds.
watch(
  (): MigrationJobAccount | null => {
    return props.account
  },
  (account): void => {
    submitted.value = false
    planId.value = account?.planId ?? ''
    ownerEmail.value = account?.ownerEmail ?? ''
    ownerLanguage.value = account?.ownerLanguage ?? 'en'
    primaryDomain.value = account?.primaryDomain ?? ''
    pullGrant.value = ''
  },
  { immediate: true },
)

/**
 * Saves the mapping when it is complete.
 * @returns Nothing; the event is emitted synchronously.
 */
const save = (): void => {
  submitted.value = true
  if (props.account === null || emailError.value !== null || planError.value !== null || domainError.value !== null) {
    return
  }
  const request: MigrationMappingRequest = {
    accountIds: [props.account.id],
    planId: planId.value,
    ownerEmail: ownerEmail.value.trim(),
    ownerLanguage: ownerLanguage.value,
    primaryDomain: primaryDomain.value.trim(),
  }
  if (props.needsGrant && pullGrant.value !== '') {
    request.pullGrant = pullGrant.value
  }
  emit('save', request)
}
</script>

<template>
  <UiModal
    :open="account !== null"
    :title="t('migrationJobs.mapping.title', { name: account?.sourceAccountName ?? '' })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiForm @submit="save">
      <div class="flex min-w-0 flex-col gap-3">
        <UiAlert v-if="errorMessage !== null" variant="error">{{ errorMessage }}</UiAlert>
        <UiSelect v-model="planId" :label="t('migrationJobs.mapping.plan')" :options="plans" :error="planError ?? undefined" required />
        <UiInput v-model="ownerEmail" type="email" :label="t('migrationJobs.mapping.ownerEmail')" :error="emailError ?? undefined" required />
        <UiSelect v-model="ownerLanguage" :label="t('migrationJobs.mapping.ownerLanguage')" :options="languages" />
        <UiInput v-model="primaryDomain" :label="t('migrationJobs.mapping.primaryDomain')" :error="domainError ?? undefined" required />
        <UiPasswordInput v-if="needsGrant" v-model="pullGrant" :label="t('migrationJobs.mapping.pullGrant')" />
      </div>
    </UiForm>

    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton :disabled="saving" @click="save">{{ t('common.save') }}</UiButton>
    </template>
  </UiModal>
</template>
