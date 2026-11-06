import { useId, useState } from 'react'

// Password field with an eye button: open eye while the text is hidden, crossed eye while it is shown.
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
        <button type="button" className="icon-button" aria-label={shown ? 'Hide password' : 'Show password'}
          aria-pressed={shown} onClick={() => setShown(!shown)}>
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5"
            strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M1 8s2.5-5 7-5 7 5 7 5-2.5 5-7 5-7-5-7-5z" />
            <circle cx="8" cy="8" r="2" />
            {shown && <path d="M2.5 13.5l11-11" />}
          </svg>
        </button>
      </div>
    </div>
  )
}
