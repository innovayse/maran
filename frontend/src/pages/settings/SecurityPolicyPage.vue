<script setup lang="ts">
/**
 * The panel's security policy: how short a password may be, whether administrators
 * must hold a second factor, and how many wrong passwords lock an account for how
 * long. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives
 * in the layout this page is nested under.
 *
 * The numbers are the server's, read on mount and written back whole: the endpoint
 * is a PUT because there is exactly one policy on a panel and the form carries all
 * of it. Nothing here invents a default; while the panel has not answered, the form is drawn with
 * its real labels and a placeholder in place of each control (`UiForm loading` inside a
 * {@link UiLoadingRegion}) rather than with plausible-looking numbers.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSwitch from '../../components/ui/UiSwitch.vue'
import { useSecurityPolicyStore } from '../../stores/securityPolicy'
import type { SecurityPolicy } from '../../types/securityPolicy'

const { t } = useI18n()
const policyStore = useSecurityPolicyStore()

/** The shortest accepted password, as typed. Text, because the form is `novalidate`. */
const minimumPasswordLength: Ref<string> = ref('')

/** Whether administrators are steered into two-factor enrolment. */
const forceTwoFactorForAdmins: Ref<boolean> = ref(false)

/** Consecutive failures that lock an account, as typed. */
const maxFailedLoginAttempts: Ref<string> = ref('')

/** How long a locked account stays locked, in minutes, as typed. */
const lockoutMinutes: Ref<string> = ref('')

/**
 * Copies a policy the panel reported into the form's fields.
 * @param policy The policy to show, or `null` while none has been read.
 * @returns Nothing; the fields are updated synchronously.
 */
const fill = (policy: SecurityPolicy | null): void => {
  if (policy === null) {
    return
  }

  minimumPasswordLength.value = String(policy.minimumPasswordLength)
  forceTwoFactorForAdmins.value = policy.forceTwoFactorForAdmins
  maxFailedLoginAttempts.value = String(policy.maxFailedLoginAttempts)
  lockoutMinutes.value = String(policy.lockoutMinutes)
}

/**
 * Saves the policy as typed.
 *
 * The numbers are sent as numbers, and the bounds are the server's to enforce: it
 * refuses a minimum length under eight, a lock threshold under three and a lockout
 * over a day, each with its own localized message, and duplicating those bounds
 * here would put two authorities on one rule.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  await policyStore.save({
    minimumPasswordLength: Number(minimumPasswordLength.value),
    forceTwoFactorForAdmins: forceTwoFactorForAdmins.value,
    maxFailedLoginAttempts: Number(maxFailedLoginAttempts.value),
    lockoutMinutes: Number(lockoutMinutes.value),
  })
}

// The form is filled from whatever the panel last reported, including after a save.
watch(
  () => {
    return policyStore.policy
  },
  fill,
  { immediate: true },
)

void policyStore.load()
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading
      :title="t('app.securityPolicy.heading')"
      :subtitle="t('app.securityPolicy.subtitle')"
    >
      <!-- The panel's own TLS is part of how it is secured, so its screen is reached from here
           (issue #132) rather than from an entry of its own. -->
      <template #actions>
        <UiButton :to="{ name: 'panel-certificate' }" variant="secondary" class="whitespace-nowrap">
          {{ t('panelCertificate.link') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <!-- A refused save; a refused read is the region's own error below. -->
    <UiAlert v-if="policyStore.loadState === 'loaded' && policyStore.errorMessage !== null" variant="error">
      {{ policyStore.errorMessage }}
    </UiAlert>

    <UiAlert v-if="policyStore.saved" variant="success">
      {{ t('app.securityPolicy.saved') }}
    </UiAlert>

    <!-- One form around three panels: the endpoint stores the whole policy at once, so
         there is one submit and no per-panel save that could write half of it. -->
    <UiLoadingRegion :state="policyStore.loadState">
      <template #error>
        <UiAlert variant="error">{{ policyStore.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <UiForm :loading="policyStore.loadState === 'loading'" @submit="submit">
        <div class="flex flex-col gap-4">
          <UiCard>
            <UiSectionHeading
              class="mb-4"
              :title="t('app.securityPolicy.passwordsTitle')"
              :subtitle="t('app.securityPolicy.passwordsHint')"
            />
            <UiInput
              v-model="minimumPasswordLength"
              :label="t('app.securityPolicy.minimumPasswordLengthLabel')"
              required
            />
          </UiCard>

          <UiCard>
            <UiSectionHeading
              class="mb-4"
              :title="t('app.securityPolicy.administratorsTitle')"
              :subtitle="t('app.securityPolicy.administratorsHint')"
            />
            <div class="flex flex-col gap-4">
              <UiSwitch
                v-model="forceTwoFactorForAdmins"
                :label="t('app.securityPolicy.forceTwoFactorLabel')"
              />

              <!-- The warning is the reason this switch is not just another field: an
                   administrator who turns it on and then cannot complete enrolment has
                   locked themselves out of their own panel, and the only way back is a
                   command on the server. -->
              <UiAlert variant="warning">{{ t('app.securityPolicy.forceTwoFactorWarning') }}</UiAlert>
            </div>
          </UiCard>

          <UiCard>
            <UiSectionHeading
              class="mb-4"
              :title="t('app.securityPolicy.lockoutTitle')"
              :subtitle="t('app.securityPolicy.lockoutHint')"
            />
            <div class="grid gap-4 sm:grid-cols-2">
              <UiInput
                v-model="maxFailedLoginAttempts"
                :label="t('app.securityPolicy.maxFailedLoginAttemptsLabel')"
                required
              />
              <UiInput
                v-model="lockoutMinutes"
                :label="t('app.securityPolicy.lockoutMinutesLabel')"
                required
              />
            </div>
          </UiCard>

          <div class="flex justify-end">
            <UiButton type="submit" :disabled="policyStore.loading">
              {{ t('app.securityPolicy.save') }}
            </UiButton>
          </div>
        </div>
      </UiForm>
    </UiLoadingRegion>
  </section>
</template>
