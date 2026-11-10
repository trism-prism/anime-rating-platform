import { SCOPE_OPTIONS, useScope, type Scope } from '../scope'
import { useAuth } from '../auth'
import Select from './Select'

// "Ratings: All / Friends only / Only me". With no props it reads and sets the site-wide scope and hides itself
// when signed out; the profile passes its own value, onChange and options (another person's profile has no Only me).
export default function ScopeSelect({ value, onChange, options = SCOPE_OPTIONS }: {
  value?: Scope
  onChange?: (s: Scope) => void
  options?: typeof SCOPE_OPTIONS
}) {
  const [scope, setScope] = useScope()
  const { user } = useAuth()
  if (!user) return null
  return (
    <label className="row scope-select">
      <span className="muted">Ratings</span>
      <Select label="Ratings" value={value ?? scope} options={options} onChange={(v) => (onChange ?? setScope)(v as Scope)} />
    </label>
  )
}
