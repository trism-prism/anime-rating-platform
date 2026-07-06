import { useId, useState } from 'react'

// Password field with a Show/Hide button.
export default function PasswordInput({ label, value, onChange, minLength, autoComplete }: {
  label: string
  value: string
  onChange: (value: string) => void
  minLength?: number
  autoComplete?: string
}) {
  const id = useId()
  const [shown, setShown] = useState(false)
  return (
    <div className="password-field">
      <label htmlFor={id}>{label}</label>
      <div className="password-row">
        <input id={id} type={shown ? 'text' : 'password'} required minLength={minLength} autoComplete={autoComplete}
          value={value} onChange={(e) => onChange(e.target.value)} />
        <button type="button" aria-pressed={shown} onClick={() => setShown(!shown)}>{shown ? 'Hide' : 'Show'}</button>
      </div>
    </div>
  )
}
