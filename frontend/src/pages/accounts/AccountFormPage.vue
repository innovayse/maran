<script setup lang="ts">
/**
 * Create-account screen: a form for the primary domain, the username (the
 * system account name, suggested from the domain and editable), plan, the
 * owner's contact address and the owner's language, beside a panel saying what
 * creation makes on the server (issue #194). Client-side validation mirrors the
 * server's
 * `CreateAccountCommandValidator` constraints exactly (same character
 * classes and length limits — read from
 * `backend/src/Maran.Modules/Accounts/Commands/CreateAccount/CreateAccountCommandValidator.cs`)
 * so a user sees the same problem before submitting that the server would
 * otherwise reject. On submit, a server-side failure (e.g. a name already
 * taken — a race the client check cannot catch) is rendered verbatim
 * (rules/vue.md: "the backend owns their text"). Renders a `<section>`, not
 * a `<main>` — the single `<main>` landmark lives in the layout this page
 * is nested under. **An administrator also chooses the account's owner**: the panel's own, or one of its
 * resellers (a server-searched picker); a reseller never sees the field, because everything it
 * creates is its own and the server stamps that from its token. The plan must belong to the same
 * owner, which the server enforces and answers in its own words. The plan picker's option list must never be clipped by its card
 * (see the template comment): an option that exists in the DOM but cannot be
 * hit is a plan an operator cannot choose.
 */
import { computed, onBeforeUnmount, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiFieldHint from '../../components/ui/UiFieldHint.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import AccountCreationSummary from '../../components/accounts/AccountCreationSummary.vue'
import AccountLimitFields from '../../components/accounts/AccountLimitFields.vue'
import AccountOwnerLoginFields from '../../components/accounts/AccountOwnerLoginFields.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import { useCallerRole } from '../../composables/useCallerRole'
import { useNewAccountOptions } from '../../composables/useNewAccountOptions'
import { usePlanChoice } from '../../composables/usePlanChoice'
import { useResellerChoice } from '../../composables/useResellerChoice'
import { useAccountsStore } from '../../stores/accounts'
import { useLocaleStore } from '../../stores/locale'
import { useOwnerLanguagesStore } from '../../stores/ownerLanguages'
import type { Plan } from '../../types/account'
import { accountNameFromDomain } from '../../utils/accountNameFromDomain'
import { flashNoticeState } from '../../utils/flashNotice'

/**
 * Matches the server's `Name` rule exactly: lowercase, starts with a letter,
 * 3-30 characters of lowercase letters/digits/underscore.
 */
const NAME_PATTERN = /^[a-z][a-z0-9_]{2,29}$/

/**
 * Matches the server's `PrimaryDomain` rule exactly: dot-separated labels of
 * up to 63 alphanumeric/hyphen characters, not starting or ending with a
 * hyphen, at least two labels.
 */
const DOMAIN_PATTERN = /^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))+$/

/**
 * A conservative address shape, not a full RFC 5322 grammar: something,
 * an "@", something with a dot. The server's `EmailAddressRule` is the
 * authority (mirroring it exactly is not attempted here, same as
 * `ForgotPasswordPage.vue`); this only catches the obvious "forgot the @"
 * slip before a round trip.
 */
const OWNER_EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Storage bound mirroring the server's `Account.OwnerEmail` column width. */
const MAX_OWNER_EMAIL_LENGTH = 256

/** The leading label the server refuses on a primary domain (`AccountDomainWwwPrefix`), compared case-insensitively. */
const WWW_PREFIX = /^www\./i

const { t } = useI18n()
const router = useRouter()
const store = useAccountsStore()
const localeStore = useLocaleStore()
const languagesStore = useOwnerLanguagesStore()
const { isAdmin, isReseller } = useCallerRole()
const { picker: resellerPicker, search: searchResellers } = useResellerChoice()

/** Current value of the username field (the account's system name). */
const name: Ref<string> = ref('')
/**
 * Whether the operator has typed into the username field. Until they have, the field follows the
 * domain; once they have, their own choice is never overwritten.
 */
const nameEdited: Ref<boolean> = ref(false)
/** Current value of the primary domain field. */
const primaryDomain: Ref<string> = ref('')
/** Current value of the plan field (the chosen plan's id; the label says "Plan", #80). */
const planId: Ref<string> = ref('')
/** Current value of the owner's contact address field. */
const ownerEmail: Ref<string> = ref('')
/**
 * The owner's language. It starts as the operator's own interface language, the likeliest
 * choice, and is sent as chosen: the invitation is written in it.
 */
const ownerLanguage: Ref<string> = ref(localeStore.current)
/** The owning reseller's id; empty for an account the administrator owns directly. Only an administrator sets it. */
const resellerId: Ref<string> = ref('')
/** Whether a submit has been attempted, so validation messages only show after the first try. */
const submitted: Ref<boolean> = ref(false)

// A plan is sold only by its owner, so the plan picker follows the owner chosen so far: the chosen
// reseller's plans, or the administrator's own while none is chosen (and always for a reseller,
// whose `resellerId` stays empty and whose plans the server scopes from its token).
const { picker: planPicker, search: searchPlans } = usePlanChoice((): string | null => {
  return resellerId.value === '' ? null : resellerId.value
})

/** The plan chosen so far, read from the picker's latest page; `undefined` while none is chosen or listed. */
const chosenPlan: ComputedRef<Plan | undefined> = computed(() => {
  const read = store.planChoicePage
  if (read.kind !== 'loaded') {
    return undefined
  }
  return read.page.items.find((plan) => {
    return plan.id === planId.value
  })
})

// The owner-login choice and the per-account limits, with their checks and request fragment.
const options = useNewAccountOptions(
  submitted,
  (): Plan | undefined => {return chosenPlan.value},
  (): boolean => {return isReseller.value},
)

/** What an empty process field means: the chosen plan's own ceiling, in words; empty when not known. */
const processDefault: ComputedRef<string> = computed(() => {
  const ceiling = chosenPlan.value?.maxProcesses
  if (ceiling === undefined) {
    return t('accounts.form.limits.followPlan')
  }
  return ceiling.kind === 'limited'
    ? t('accounts.form.limits.planLimited', { maximum: ceiling.maximum })
    : t('accounts.form.limits.planUnlimited')
})

/** The owner picker: "the panel's own" first, then the page of resellers matching the search. */
const ownerPicker: ComputedRef<AccountPickerState> = computed(() => {
  const state = resellerPicker.value
  return {
    ...state,
    options: [{ value: '', label: t('accounts.form.noReseller') }, ...state.options],
    total: state.total + 1,
  }
})

/** Client-side validation message for the name field, mirroring the server's rule. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (name.value.length === 0) {
    return t('accounts.form.errors.nameRequired')
  }
  if (name.value.length > 32 || !NAME_PATTERN.test(name.value)) {
    return t('accounts.form.errors.nameInvalid')
  }
  return null
})

/** The owner-language choices exactly as the panel lists them, each named in itself. */
const languageOptions: ComputedRef<SelectOption[]> = computed(() => {
  return languagesStore.languages.map((choice) => {
    return { value: choice.language, label: choice.languageDisplayName }
  })
})

/** Client-side validation message for the primary domain field, mirroring the server's rule. */
const primaryDomainError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (primaryDomain.value.length === 0) {
    return t('accounts.form.errors.primaryDomainRequired')
  }
  if (primaryDomain.value.length > 253 || !DOMAIN_PATTERN.test(primaryDomain.value)) {
    return t('accounts.form.errors.primaryDomainInvalid')
  }
  if (WWW_PREFIX.test(primaryDomain.value)) {
    return t('accounts.form.errors.primaryDomainWww')
  }
  return null
})

/** Client-side validation message for the plan field. */
const planIdError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  // Nothing chosen is the only way this field can be wrong: the options come from the
  // backend, so the value is always one the server issued and needs no shape check.
  return planId.value.length === 0 ? t('accounts.form.errors.planRequired') : null
})

/** Client-side validation message for the owner email field, mirroring the server's rule. */
const ownerEmailError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (ownerEmail.value.length === 0) {
    return t('accounts.form.errors.ownerEmailRequired')
  }
  if (ownerEmail.value.length > MAX_OWNER_EMAIL_LENGTH || !OWNER_EMAIL_PATTERN.test(ownerEmail.value)) {
    return t('accounts.form.errors.ownerEmailInvalid')
  }
  return null
})

/** Whether every field currently passes client-side validation. */
const isValid: ComputedRef<boolean> = computed(() => {
  return (
    nameError.value === null &&
    primaryDomainError.value === null &&
    planIdError.value === null &&
    ownerEmailError.value === null &&
    options.valid.value
  )
})

/**
 * Takes a new owner and drops the plan chosen for the previous one, which the new owner cannot sell.
 * @param value The owner chosen in the picker; empty for the panel's own.
 */
const onOwnerChange = (value: string): void => {
  if (value !== resellerId.value) {
    planId.value = ''
  }
  resellerId.value = value
}

/**
 * Takes the domain the operator typed and, while they have not chosen a username of their own,
 * suggests one from it.
 * @param value The primary domain as typed.
 */
const onDomainInput = (value: string): void => {
  primaryDomain.value = value
  if (!nameEdited.value) {
    name.value = accountNameFromDomain(value)
  }
}

/**
 * Takes the username the operator typed; from now on the domain no longer rewrites it.
 * @param value The username as typed.
 */
const onNameInput = (value: string): void => {
  name.value = value
  nameEdited.value = true
}

/**
 * Validates the form and, when valid, submits it to the store. On success it opens the new
 * account's own page rather than the list (#78): a customer cannot sign in until their owner has
 * been handed an invitation, and that page is where the invitation card — with a one-time link for
 * a panel that cannot send mail — is waiting. A server-side failure is left in the store's
 * `createErrorMessage` for the template to render verbatim.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (!isValid.value) {
    return
  }
  const created = await store.create({
    name: name.value,
    primaryDomain: primaryDomain.value,
    planId: planId.value,
    ownerEmail: ownerEmail.value,
    ownerLanguage: ownerLanguage.value,
    ...options.toRequest(),
    // Only an administrator names an owner; a reseller's accounts are stamped by the server.
    ...(isAdmin.value && resellerId.value !== '' ? { resellerId: resellerId.value } : {}),
  })
  if (created !== null) {
    // Created: the password has done its job and the page is leaving. A refusal keeps it, so a
    // corrected name can be resubmitted without choosing a new one.
    options.clearPassword()
    await router.push({
      name: 'account-detail',
      params: { id: created.id },
      state: flashNoticeState(t('accounts.form.created', { account: created.name })),
    })
  }
}

/**
 * Navigates back to the accounts list without submitting.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'accounts' })
}

onBeforeUnmount(() => {
  options.clearPassword()
})

onMounted(async () => {
  await languagesStore.load()
  // The operator's own interface language is the likeliest choice, but only the panel says which
  // languages an owner may be written to: when it is not one of them, start from the panel's first.
  const [first] = languagesStore.languages
  const offered = languagesStore.languages.some((choice) => {
    return choice.language === ownerLanguage.value
  })
  if (first !== undefined && !offered) {
    ownerLanguage.value = first.language
  }
})
</script>

<template>
  <section class="flex w-full max-w-5xl flex-col gap-4">
    <UiPageHeading
      :title="t('accounts.form.heading')"
      :subtitle="t('accounts.form.subtitle')"
    />

    <!-- The one thing that needs attention goes first: a refusal from the server, rendered
         verbatim. It is the page's only `status` region (a spec reads it by that role). -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">
      {{ store.createErrorMessage }}
    </UiAlert>

    <div class="grid items-start gap-4 lg:grid-cols-3">
      <!-- The card is NOT clipped (`overflow-hidden`) and nothing inside it is: the select option
           lists are positioned inside this box, and clipping it once left the third plan rendered
           in the DOM yet unreachable — a hit test at its centre returned the card, so an assertion
           that the option exists passed while an operator could not choose it. The actions row
           therefore sits inside the card's padding behind a hairline instead of bleeding to its
           edges, which would have needed the card to round (and so clip) them. -->
      <UiCard class="lg:col-span-2">
        <UiForm @submit="submit">
          <div class="grid grid-cols-1 gap-x-4 gap-y-4 md:grid-cols-2">
            <div class="flex flex-col gap-1">
              <UiInput
                :model-value="primaryDomain"
                :label="t('accounts.form.fields.primaryDomain')"
                :error="primaryDomainError"
                required
                mono
                :placeholder="t('accounts.form.placeholders.primaryDomain')"
                @update:model-value="onDomainInput"
              />
              <UiFieldHint>{{ t('accounts.form.hints.primaryDomain') }}</UiFieldHint>
            </div>
            <div class="flex flex-col gap-1">
              <UiInput
                :model-value="name"
                :label="t('accounts.form.fields.username')"
                :error="nameError"
                required
                mono
                :placeholder="t('accounts.form.placeholders.name')"
                @update:model-value="onNameInput"
              />
              <UiFieldHint>{{ t('accounts.form.hints.username') }}</UiFieldHint>
            </div>
            <!-- Searched on the server one page at a time, so every plan is reachable however many
                 the panel sells; the picker reads its first page when it is opened. -->
            <UiCombobox
              v-model="planId"
              v-bind="planPicker"
              :label="t('accounts.form.fields.plan')"
              :error="planIdError"
              required
              :placeholder="t('accounts.form.placeholders.plan')"
              @search="searchPlans"
            />
            <div v-if="isAdmin" class="flex flex-col gap-1">
              <UiCombobox
                :model-value="resellerId"
                v-bind="ownerPicker"
                :label="t('accounts.form.fields.reseller')"
                :placeholder="t('accounts.form.noReseller')"
                @update:model-value="onOwnerChange"
                @search="searchResellers"
              />
              <UiFieldHint>{{ t('accounts.form.hints.reseller') }}</UiFieldHint>
            </div>
            <UiInput
              v-model="ownerEmail"
              :label="t('accounts.form.fields.ownerEmail')"
              :error="ownerEmailError"
              required
              autocomplete="email"
              :placeholder="t('accounts.form.placeholders.ownerEmail')"
            />
            <div class="flex flex-col gap-1">
              <UiSelect
                v-model="ownerLanguage"
                :label="t('accounts.form.fields.ownerLanguage')"
                :options="languageOptions"
                :load-error="languagesStore.errorMessage"
                required
              />
              <UiFieldHint>{{ t('accounts.form.hints.ownerLanguage') }}</UiFieldHint>
            </div>
            <AccountOwnerLoginFields
              v-model:mode="options.loginMode.value"
              v-model:password="options.initialPassword.value"
              :password-error="options.passwordError.value"
            />
            <AccountLimitFields
              v-model:processes="options.processLimit.value"
              v-model:inodes="options.inodeLimit.value"
              v-model:open-files="options.openFilesLimit.value"
              :process-error="options.processLimitError.value"
              :inode-error="options.inodeLimitError.value"
              :open-files-error="options.openFilesLimitError.value"
              :process-default="processDefault"
              :lower-only="isReseller"
            />
          </div>
          <div class="mt-5 flex flex-wrap justify-end gap-2 border-t border-border-subtle pt-4">
            <UiButton variant="secondary" type="button" @click="cancel">
              {{ t('common.cancel') }}
            </UiButton>
            <UiButton type="submit" :disabled="store.creating">{{ t('accounts.form.submit') }}</UiButton>
          </div>
        </UiForm>
      </UiCard>
      <AccountCreationSummary :login-mode="options.loginMode.value" />
    </div>
  </section>
</template>
