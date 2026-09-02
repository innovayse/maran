<script setup lang="ts">
/**
 * The create-a-database form: the account picker, the two names, and the client-side mirror of the
 * server's validator.
 *
 * Split out of `DatabasesPage.vue` rather than living in it, because the page was carrying three
 * unrelated jobs — creating, listing, and revealing a credential — and had grown past the length
 * rules/vue.md targets. This component holds the whole of the first job, including its own field
 * state, so the page keeps none of it.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer
 * (rules/vue.md) — it reports a validated request and the page decides what to do with it. It also
 * refuses to emit at all until its own rules pass, which is what makes the round trip it saves a
 * real saving rather than a message printed beside a request that went anyway.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { Account } from '../../types/account'
import type { CreateDatabaseRequest } from '../../types/database'

/**
 * The server's suffix alphabet, character for character: lowercase ASCII letters and digits, and
 * nothing else (`CreateDatabaseCommandValidator`). Narrower than a legal MySQL identifier on
 * purpose — both names are interpolated into DDL that takes no placeholders, so a backtick, a
 * quote or a newline must never reach it — and it excludes the underscore separator too, so
 * account `alice` cannot ask for `bob_secrets` and be handed a name that reads as `bob`'s.
 *
 * This is advice that saves a round trip. The server re-validates every one of these, and its
 * already-localized rejection is what the operator reads when the two disagree.
 */
const SUFFIX_PATTERN = /^[a-z0-9]+$/

/** The longest suffix the server accepts before the account prefix is applied. */
const MAX_SUFFIX_LENGTH = 30

/** Props accepted by {@link DatabaseCreateForm}. */
const props = defineProps<{
  /** The accounts a database may be created for, as the panel reported them. */
  accounts: readonly Account[]
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link DatabaseCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateDatabaseRequest): void
}>()

const { t } = useI18n()

/** The account that will own the new database. */
const accountId: Ref<string> = ref('')

/** The database name the customer chose, without the account prefix. */
const name: Ref<string> = ref('')

/** The dedicated user's name, without the account prefix. */
const dbUserName: Ref<string> = ref('')

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
  return accountId.value.length === 0 ? t('databases.form.errors.accountRequired') : null
})

/** Validation message for the database name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (name.value.length === 0) {
    return t('databases.form.errors.nameRequired')
  }
  return name.value.length > MAX_SUFFIX_LENGTH || !SUFFIX_PATTERN.test(name.value)
    ? t('databases.form.errors.nameInvalid')
    : null
})

/** Validation message for the user name, or `null`. */
const dbUserNameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (dbUserName.value.length === 0) {
    return t('databases.form.errors.dbUserNameRequired')
  }
  return dbUserName.value.length > MAX_SUFFIX_LENGTH || !SUFFIX_PATTERN.test(dbUserName.value)
    ? t('databases.form.errors.dbUserNameInvalid')
    : null
})

/** Whether every field currently passes the client's own mirror of the server's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return accountError.value === null && nameError.value === null && dbUserNameError.value === null
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value) {
    return
  }
  emit('submit', { accountId: accountId.value, name: name.value, dbUserName: dbUserName.value })
}

/**
 * Empties the two name fields and forgets that a submit was attempted, so the next database is
 * typed into a clean form rather than into one still showing the last one's values.
 *
 * The account is deliberately kept: creating several databases for one customer is the common
 * case, and re-picking the same account every time is friction with nothing behind it.
 *
 * Exposed for the page to call after the panel has accepted a create. The page cannot simply clear
 * the fields itself — they live here, and so does `submitted`, which has to be cleared with them
 * or an emptied form would immediately turn red.
 * @returns Nothing.
 */
const reset = (): void => {
  name.value = ''
  dbUserName.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <div class="rounded-xl border border-border-subtle bg-surface-1">
    <UiForm @submit="submit">
      <div class="grid gap-3.5 p-4.5 sm:grid-cols-3">
        <UiSelect
          v-model="accountId"
          :label="t('databases.form.fields.accountId')"
          :options="accountOptions"
          :error="accountError"
          required
        />
        <UiInput
          v-model="name"
          :label="t('databases.form.fields.name')"
          :placeholder="t('databases.form.placeholders.name')"
          :error="nameError"
          required
        />
        <UiInput
          v-model="dbUserName"
          :label="t('databases.form.fields.dbUserName')"
          :placeholder="t('databases.form.placeholders.dbUserName')"
          :error="dbUserNameError"
          required
        />
      </div>
      <div
        class="flex flex-wrap items-center justify-between gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3"
      >
        <p class="text-sm text-text-muted">{{ t('databases.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">{{ t('databases.form.submit') }}</UiButton>
      </div>
    </UiForm>
  </div>
</template>
