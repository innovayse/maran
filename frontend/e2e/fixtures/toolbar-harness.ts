import { createApp, defineComponent, h, ref, type VNode } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiCheckbox from '@/components/ui/UiCheckbox.vue'
import UiFilterTrigger from '@/components/ui/UiFilterTrigger.vue'
import UiIcon from '@/components/ui/UiIcon.vue'
import UiSearchInput from '@/components/ui/UiSearchInput.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import UiToolbar from '@/components/ui/UiToolbar.vue'
import { createAppI18n } from '@/i18n'
import '@/assets/css/main.css'

const params = new URLSearchParams(window.location.search)
const planted = params.get('plant') === '1'

// Option labels and control names are fixture text, not product copy.
const OPTIONS = [
  { value: 'all', label: 'All' },
  { value: 'active', label: 'Active' },
]

const Harness = defineComponent({
  setup: (): (() => VNode) => {
    const search = ref('')
    const hidden = ref(false)
    const all = ref(false)
    const status = ref('all')

    return (): VNode => {
      const controls: VNode[] = [
        h(UiButton, { variant: 'secondary' }, () => {return [h(UiIcon, { name: 'chevronLeft', size: 'sm' }), 'Up']}),
        h(UiSearchInput, {
          modelValue: search.value,
          class: 'min-w-55 max-w-85 flex-1',
          label: 'Search this folder',
          placeholder: 'Search by name',
          clearLabel: 'Clear search',
          'onUpdate:modelValue': (value: string) => {
            search.value = value
          },
        }),
        h(UiSelect, {
          modelValue: status.value,
          label: 'Status',
          options: OPTIONS,
          'onUpdate:modelValue': (value: string) => {
            status.value = value
          },
        }),
        h(UiSwitch, {
          modelValue: hidden.value,
          label: 'Show hidden files',
          'onUpdate:modelValue': (value: boolean) => {
            hidden.value = value
          },
        }),
        h(UiCheckbox, {
          modelValue: all.value,
          label: 'Select all',
          'onUpdate:modelValue': (value: boolean) => {
            all.value = value
          },
        }),
        h(UiFilterTrigger, { label: 'Filters', activeCount: 0, activeLabel: '' }),
        h(UiButton, { variant: 'ghost', size: 'icon', 'aria-label': 'Refresh' }, () => {return [
          h(UiIcon, { name: 'rotateCw', size: 'md' }),
        ]}),
      ]
      if (planted) {
        // The planted break: a block taller than the control scale, as a control with a stacked
        // label or a hand-set height would be.
        controls.push(h('div', { style: 'height: 52px; width: 80px; background: #888' }, 'planted'))
      }
      return h(UiToolbar, null, () => {return controls})
    }
  },
})

createApp(Harness).use(createAppI18n()).mount('#app')
