<script setup lang="ts">
/**
 * The app bar's account selector: which hosting account the signed-in customer is acting for
 * (issue #313). One person can own several accounts, and every tenant screen follows the one the
 * session names.
 *
 * **Drawn only for a person with a choice.** With one account (or none) there is nothing to pick
 * between, so the component renders nothing and the bar is exactly what it was. Administrators act
 * for no account and are never offered it. That is presentation: the server refuses a switch to an
 * account the person holds no membership of with a 404, and issues the account claim of every
 * token itself, so a wrong list here could only mislead the page, never widen a request.
 *
 * **After a switch the panel reloads, on purpose.** Tenant data lives in some twenty Pinia stores
 * (sites, databases, plans, usage, tasks, badges), none of them keyed by account, and several
 * (`ensureLoaded`-style) read once and then answer from memory. Clearing them one by one would be a
 * list that goes stale the day a store is added, and a store missed shows the previous account's
 * rows on the new account's screen, which is the worst thing this control could do. A full page
 * load is the one mechanism that cannot miss a store. It costs one refresh round trip: the server
 * stored the choice on the session, so the refresh cookie restores a token that names the new
 * account, and that is why the token in the switch answer is not adopted here. The reload lands on
 * the dashboard, because the page the person was on (a site, a database) belongs to the account
 * they just left and would be a 404 in the new one.
 *
 * The trigger is the kit dropdown's own button, in the shape of the language picker beside it: an
 * icon plus the active account's name, the name dropped at narrow widths while the accessible name
 * stays whole. The list is a set of alternatives, so every item states whether it is the chosen one.
 */
import { computed, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiPinnedToast from '../ui/UiPinnedToast.vue'
import { useCallerRole } from '../../composables/useCallerRole'
import { useAccountMembershipsStore } from '../../stores/accountMemberships'
import { useAuthStore } from '../../stores/auth'
import type { AccountMembership } from '../../types/accountMembership'

const { t } = useI18n()
const router = useRouter()
const store = useAccountMembershipsStore()
const auth = useAuthStore()
const { isCustomer } = useCallerRole()

/** The active account's name, or an empty string while it is not known. */
const activeName: ComputedRef<string> = computed(() => {
  return store.active?.name ?? ''
})

/**
 * Reads the accounts for the signed-in customer, and forgets them when nobody is signed in or the
 * person is not a customer. Keyed on the user's id, so a different person signing in on the same
 * tab never sees the previous one's list.
 * @param userId The signed-in person's id, or undefined when nobody is.
 * @returns Resolves once the read settled.
 */
const sync = async (userId: string | undefined): Promise<void> => {
  if (userId === undefined || !isCustomer.value) {
    store.clear()
    return
  }
  await store.load()
}

/**
 * Switches to the chosen account and reloads the panel at its landing screen (see the component's
 * doc comment for why a reload and not a refetch).
 * @param account The account the person chose.
 * @returns Resolves once the switch was refused, or immediately before the page unloads.
 */
const choose = async (account: AccountMembership): Promise<void> => {
  if (account.isActive) {
    return
  }
  if (await store.switchTo(account.accountId)) {
    window.location.assign(router.resolve({ name: 'system-status' }).href)
  }
}

watch(
  (): string | undefined => {
    return auth.user?.id
  },
  sync,
  { immediate: true },
)
</script>

<template>
  <div v-if="store.hasChoice" class="shrink-0">
    <UiDropdown
      data-testid="account-selector"
      align="end"
      trigger-variant="ghost"
      trigger-size="bar"
      :label="activeName"
      :aria-label="t('accountSelector.label', { name: activeName })"
      :chevron="false"
      :disabled="store.switching"
    >
      <template #leading>
        <UiIcon name="users" size="lg" />
      </template>
      <template #trigger>
        <span class="max-w-40 truncate max-lg:hidden @max-[62rem]:hidden">{{ activeName }}</span>
      </template>

      <UiDropdownItem
        v-for="account in store.items"
        :key="account.accountId"
        :checked="account.isActive"
        @select="choose(account)"
      >
        {{ account.name }}
      </UiDropdownItem>
    </UiDropdown>

    <UiPinnedToast
      :message="store.errorMessage"
      variant="danger"
      :close-label="t('common.close')"
      @dismiss="store.dismissError"
    />
  </div>
</template>
