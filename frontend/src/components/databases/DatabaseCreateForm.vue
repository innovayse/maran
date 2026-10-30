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
 *
 * Drawn as one `UiCard` panel: a titled section, the three fields, and the submit pill under them
 * at the end. The root is a single element, so the page spaces it with a class and the card
 * draws no margin of its own.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
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
  /**
   * The account picker's state — the page of accounts a database may be created for matching the last search — read
   * by the page from its store; the form only renders it and asks for searches.
   */
  accountPicker: AccountPickerState
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
  /**
   * The account every request is for when the caller has no choice (a customer's own), or `null`
   * for an administrator. When set, the picker is not drawn and the account rule is skipped.
   */
  fixedAccountId: string | null
}>()

/** Events emitted by {@link DatabaseCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateDatabaseRequest): void
  /** Fired when the account picker needs the accounts matching what was typed into it. */
  (e: 'searchAccounts', term: string): void
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

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || props.fixedAccountId !== null) {
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
  emit('submit', { accountId: props.fixedAccountId ?? accountId.value, name: name.value, dbUserName: dbUserName.value })
}

/**
 * Returns every field, the account included, to its starting value and forgets that a submit was
 * attempted: a form still showing the values it just sent invites the same submit twice (#218).
 *
 * Exposed for the page to call after the panel has accepted a create. The page cannot simply clear
 * the fields itself — they live here, and so does `submitted`, which has to be cleared with them
 * or an emptied form would immediately turn red.
 * @returns Nothing.
 */
const reset = (): void => {
  accountId.value = ''
  name.value = ''
  dbUserName.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <UiCard>
    <UiForm @submit="submit">
      <UiSectionHeading
        class="mb-4"
        :title="t('databases.form.heading')"
        :subtitle="t('databases.form.hint')"
      />
      <!-- Two columns from a tablet, three only once a content panel is wide enough to give the
           account picker its "name · domain" labels room: at 1024px the sidebar has already taken
           240px, and three squeezed tracks cut the picker's text. -->
      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <div v-if="fixedAccountId === null" class="min-w-0 sm:col-span-2 xl:col-span-1">
          <UiCombobox
            v-model="accountId"
            v-bind="accountPicker"
            :label="t('databases.form.fields.accountId')"
            :placeholder="t('databases.form.placeholders.accountId')"
            :error="accountError"
            required
            @search="emit('searchAccounts', $event)"
          />
        </div>
        <div class="min-w-0">
          <UiInput
            v-model="name"
            :label="t('databases.form.fields.name')"
            :placeholder="t('databases.form.placeholders.name')"
            :error="nameError"
            required
          />
        </div>
        <div class="min-w-0">
          <UiInput
            v-model="dbUserName"
            :label="t('databases.form.fields.dbUserName')"
            :placeholder="t('databases.form.placeholders.dbUserName')"
            :error="dbUserNameError"
            required
          />
        </div>
      </div>
      <div class="mt-5 flex justify-end">
        <UiButton type="submit" :disabled="submitting">{{ t('databases.form.submit') }}</UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
