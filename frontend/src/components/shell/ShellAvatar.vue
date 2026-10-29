<script setup lang="ts">
/**
 * The signed-in person's round initials badge, drawn once for the app bar and the
 * sidebar's identity card so the two can never disagree about who is signed in.
 *
 * **It reads the auth store itself** rather than taking a name as a prop, because
 * "who is signed in" is one fact: two callers fetching it separately is how two
 * badges come to show different letters. The disc and the letter rule belong to
 * {@link UiAvatar}; this component only supplies the login name.
 *
 * Nobody signed in draws a neutral person glyph through the avatar's slot instead
 * of invented initials (rules/vue.md: the SPA never invents domain data).
 */
import { computed, type ComputedRef } from 'vue'
import { useAuthStore } from '../../stores/auth'
import UiAvatar from '../ui/UiAvatar.vue'
import UiIcon from '../ui/UiIcon.vue'

/** Props accepted by {@link ShellAvatar}. */
withDefaults(
  defineProps<{
    /** `md` (36px) beside the sidebar's name, `sm` (32px) in the app bar. */
    size?: 'sm' | 'md'
  }>(),
  { size: 'md' },
)

const authStore = useAuthStore()

/** The signed-in login name, or `null` when nobody is signed in. */
const username: ComputedRef<string | null> = computed(() => {
  return authStore.user === null ? null : authStore.user.username
})
</script>

<template>
  <UiAvatar :name="username ?? ''" :size="size">
    <UiIcon v-if="username === null" name="user" size="md" />
  </UiAvatar>
</template>
