<script setup lang="ts">
/**
 * Create-account screen: a form for name, primary domain, plan and the
 * account's owner contact address, with client-side validation mirroring the
 * server's
 * `CreateAccountCommandValidator` constraints exactly (same character
 * classes and length limits — read from
 * `backend/src/Maran.Modules/Accounts/Commands/CreateAccount/CreateAccountCommandValidator.cs`)
 * so a user sees the same problem before submitting that the server would
 * otherwise reject. On submit, a server-side failure (e.g. a name already
 * taken — a race the client check cannot catch) is rendered verbatim
 * (rules/vue.md: "the backend owns their text"). Renders a `<section>`, not
 * a `<main>` — the single `<main>` landmark lives in the layout this page
 * is nested under. The plan picker's option list must never be clipped by its card
 * (see the template comment): an option that exists in the DOM but cannot be
 * hit is a plan an operator cannot choose.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import { useAccountsStore } from '../../stores/accounts'

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

const { t } = useI18n()
const router = useRouter()
const store = useAccountsStore()

/** Current value of the name field. */
const name: Ref<string> = ref('')
/** Current value of the primary domain field. */
const primaryDomain: Ref<string> = ref('')
/** Current value of the plan field (the chosen plan's id; the label says "Plan", #80). */
const planId: Ref<string> = ref('')
/** Current value of the owner's contact address field. */
const ownerEmail: Ref<string> = ref('')
/** Whether a submit has been attempted, so validation messages only show after the first try. */
const submitted: Ref<boolean> = ref(false)

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

/**
 * The plans offered by the picker. Labels come from the backend already localized —
 * plans are server-side reference data, and a limit shown beside the name is what
 * makes the choice meaningful rather than a list of words.
 */
const planOptions: ComputedRef<SelectOption[]> = computed(() => {
  return store.plans.map((plan) => {
    return {
      value: plan.id,
      label: t('accounts.form.planOption', {
        name: plan.displayName,
        disk: plan.diskQuotaMb,
        sites: plan.maxSites,
      }),
    }
  })
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
    ownerEmailError.value === null
  )
})

/**
 * Loads the plans the picker offers, once, when the form opens.
 * @returns Resolves once the request has settled.
 */
const loadPlans = async (): Promise<void> => {
  await store.loadPlans()
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
  })
  if (created !== null) {
    await router.push({ name: 'account-detail', params: { id: created.id } })
  }
}

/**
 * Navigates back to the accounts list without submitting.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'accounts' })
}
onMounted(loadPlans)
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('accounts.list.heading')"
      :title="t('accounts.form.heading')"
      :subtitle="t('accounts.form.subtitle')"
    />

    <!-- The one thing that needs attention goes first: a refusal from the server, rendered
         verbatim. It is the page's only `status` region (a spec reads it by that role). -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">
      {{ store.createErrorMessage }}
    </UiAlert>

    <!-- The card is NOT clipped (`overflow-hidden`) and nothing inside it is: the plan select's
         option list is positioned inside this box, and clipping it once left the third plan
         rendered in the DOM yet unreachable — a hit test at its centre returned the card, so an
         assertion that the option exists passed while an operator could not choose it. The
         actions row therefore sits inside the card's padding behind a hairline instead of
         bleeding to its edges, which would have needed the card to round (and so clip) them. -->
    <UiCard>
      <UiForm @submit="submit">
        <div class="grid gap-x-4 gap-y-4 md:grid-cols-2">
          <UiInput
            v-model="name"
            :label="t('common.name')"
            :error="nameError"
            required
            :placeholder="t('accounts.form.placeholders.name')"
          />
          <UiInput
            v-model="primaryDomain"
            :label="t('accounts.form.fields.primaryDomain')"
            :error="primaryDomainError"
            required
            :placeholder="t('accounts.form.placeholders.primaryDomain')"
          />
          <UiSelect
            v-model="planId"
            :label="t('accounts.form.fields.plan')"
            :options="planOptions"
            :error="planIdError"
            required
            :placeholder="t('accounts.form.placeholders.plan')"
          />
          <UiInput
            v-model="ownerEmail"
            :label="t('accounts.form.fields.ownerEmail')"
            :error="ownerEmailError"
            required
            autocomplete="email"
            :placeholder="t('accounts.form.placeholders.ownerEmail')"
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
  </section>
</template>
