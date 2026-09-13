<script setup lang="ts">
/**
 * The take-a-backup-now form: the account picker and the button, with the one client-side rule the
 * server has — an account must be named.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer
 * (rules/vue.md) — it reports a validated request and the page decides what to do with it.
 *
 * There is no destination picker and no kind picker, and neither is an omission to be filled in
 * later without a backend change: `CreateBackupRequest` carries one field, because where the bytes
 * go is the operator's configuration rather than a per-request choice, and everything asked for
 * over HTTP is a manual backup.
 *
 * The button says what the wait will be like. The panel's create endpoint does not answer until the
 * archive is written, so on a large account this form sits submitting for minutes; a control that
 * only greyed out would read as a broken page.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { Account } from '../../types/account'
import type { CreateBackupRequest } from '../../types/backup'

/** Props accepted by {@link BackupCreateForm}. */
const props = defineProps<{
  /** The accounts a backup may be taken of, as the panel reported them. */
  accounts: readonly Account[]
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link BackupCreateForm}. */
const emit = defineEmits<{
  /** Fired only when an account has been chosen, carrying the request to send. */
  (e: 'submit', request: CreateBackupRequest): void
}>()

const { t } = useI18n()

/** The account the backup will be taken of. */
const accountId: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The accounts the picker offers, as the panel reported them. */
const accountOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.accounts.map((account) => {
    return { value: account.id, label: `${account.name} · ${account.primaryDomain}` }
  })
})

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return accountId.value.length === 0 ? t('backups.form.errors.accountRequired') : null
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when an account has been chosen.
 */
const submit = (): void => {
  submitted.value = true
  if (accountError.value !== null) {
    return
  }
  emit('submit', { accountId: accountId.value })
}
</script>

<template>
  <div class="rounded-xl border border-border-subtle bg-surface-1">
    <UiForm @submit="submit">
      <div class="grid gap-3.5 p-4.5 sm:grid-cols-2">
        <UiSelect
          v-model="accountId"
          :label="t('backups.form.fields.accountId')"
          :placeholder="t('backups.form.placeholders.accountId')"
          :options="accountOptions"
          :error="accountError"
          required
        />
      </div>
      <div
        class="flex flex-wrap items-center justify-between gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3"
      >
        <p class="text-sm text-text-muted">{{ t('backups.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">
          {{ submitting ? t('backups.form.submitting') : t('backups.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </div>
</template>
