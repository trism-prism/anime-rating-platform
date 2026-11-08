import type { Audience } from '../types'
import Select from './Select'

const OPTIONS: { value: Audience; label: string }[] = [
  { value: 'Public', label: 'Public' },
  { value: 'Friends', label: 'Friends' },
  { value: 'OnlyMe', label: 'Only me' },
]

// visibility of a rating or a list entry; `label` names it for screen readers
export default function AudienceSelect({ value, onChange, disabled, label }: {
  value: Audience
  onChange: (a: Audience) => void
  disabled?: boolean
  label: string
}) {
  return (
    <Select label={label} value={value} options={OPTIONS} disabled={disabled} onChange={(v) => onChange(v as Audience)} />
  )
}
