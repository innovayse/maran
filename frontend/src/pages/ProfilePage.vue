<script setup lang="ts">
/**
 * The signed-in person's own profile: `/profile`, for every role.
 *
 * It describes the USER — login name, address, role — and the state of their sign-in security:
 * whether a second factor is enrolled and how many devices are signed in, each with a link to the
 * screen that manages it. It is not the customer's hosting account: that is `/my-account`, a
 * different thing an administrator does not have. A customer is offered a link to it here; an
 * administrator never is, because its endpoint answers "the account YOU own" and 404s for them.
 *
 * Reads `GET /api/v1/profile` through `stores/profile.ts` — never an API composable directly
 * (rules/vue.md). The role is translated here (`app.auth.role.*`), as the account menu does,
 * because the contract carries a machine role and no label beside it.
 *
 * While the profile is on its way the cards are already drawn with their real terms and a value
 * line in place of each value ({@link UiLoadingRegion}, `UiDescriptionList loading`), so the loaded
 * profile lands in the space that was held for it.
 *
 * The password card (`ChangePasswordCard`) changes the caller's own password and ends every
 * other sign-in (issue #232).
 *
 * Renders a `<section>`, not a `<main>`: the layout owns the landmark.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import ChangePasswordCard from '../components/auth/ChangePasswordCard.vue'
import UiAlert from '../components/ui/UiAlert.vue'
import UiBadge from '../components/ui/UiBadge.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiCard from '../components/ui/UiCard.vue'
import UiDescriptionItem from '../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../components/ui/UiDescriptionList.vue'
import UiLoadingRegion from '../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../components/ui/UiSectionHeading.vue'
import { useCallerRole } from '../composables/useCallerRole'
import { useProfileStore } from '../stores/profile'

const { t } = useI18n()
const store = useProfileStore()
const { isCustomer } = useCallerRole()

onMounted(async () => {
  await store.load()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('app.profile.heading')" :subtitle="t('app.profile.subtitle')" />

    <UiLoadingRegion :state="store.loadState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <div class="flex flex-col gap-4">
        <UiCard>
          <UiSectionHeading class="mb-4" :title="t('app.profile.identityHeading')" />
          <UiDescriptionList :loading="store.loadState === 'loading'">
            <UiDescriptionItem :term="t('app.profile.usernameLabel')" mono>
              {{ store.profile?.username }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('app.profile.emailLabel')" mono>
              {{ store.profile?.email }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('app.profile.roleLabel')">
              {{ store.profile === null ? '' : t(`app.auth.role.${store.profile.role}`) }}
            </UiDescriptionItem>
          </UiDescriptionList>
        </UiCard>

        <UiCard>
          <UiSectionHeading class="mb-4" :title="t('app.profile.securityHeading')" />
          <UiDescriptionList :loading="store.loadState === 'loading'">
            <UiDescriptionItem :term="t('app.profile.twoFactorLabel')">
              <span v-if="store.profile !== null" class="flex flex-wrap items-center gap-3">
                <UiBadge :variant="store.profile.isTwoFactorEnabled ? 'success' : 'warning'">
                  {{ store.profile.isTwoFactorEnabled ? t('app.profile.twoFactorOn') : t('app.profile.twoFactorOff') }}
                </UiBadge>
                <UiButton variant="secondary" :to="{ name: 'two-factor' }">
                  {{ t('app.profile.manageTwoFactor') }}
                </UiButton>
              </span>
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('app.profile.sessionsLabel')">
              <span v-if="store.profile !== null" class="flex flex-wrap items-center gap-3">
                <span class="tabular-nums">{{
                  t('app.profile.sessionCount', { count: store.profile.activeSessionCount }, store.profile.activeSessionCount)
                }}</span>
                <UiButton variant="secondary" :to="{ name: 'sessions' }">
                  {{ t('app.profile.manageSessions') }}
                </UiButton>
              </span>
            </UiDescriptionItem>
          </UiDescriptionList>
        </UiCard>

        <!-- Every role changes its own password here; the card holds no profile data, so it is
             drawn at once and does not wait on the read above. -->
        <ChangePasswordCard />

        <!-- Only a customer owns a hosting account. An administrator is never sent to
             `/my-account`: its endpoint answers "the account you own" and has none to give. -->
        <UiCard v-if="isCustomer">
          <UiSectionHeading
            class="mb-4"
            :title="t('app.profile.hostingHeading')"
            :subtitle="t('app.profile.hostingDescription')"
          />
          <UiButton variant="secondary" :to="{ name: 'my-account' }">
            {{ t('app.profile.openHostingAccount') }}
          </UiButton>
        </UiCard>
      </div>
    </UiLoadingRegion>
  </section>
</template>
