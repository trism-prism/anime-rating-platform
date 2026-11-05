import type { Audience } from '../types'

const OPTIONS: { value: Audience; label: string }[] = [
  { value: 'Public', label: 'Public' },
  { value: 'Friends', label: 'Friends' },
  { value: 'OnlyMe', label: 'Only me' },
]

// who can see a rating or a list entry; `label` names it for screen readers
export default function AudienceSelect({ value, onChange, disabled, label }: {
  value: Audience
  onChange: (a: Audience) => void
  disabled?: boolean
  label: string
}) {
  return (
    <select aria-label={label} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value as Audience)}>
      {OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
    </select>
  )
}
