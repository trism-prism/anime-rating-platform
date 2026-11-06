import { Link } from 'react-router-dom'
import Legal from '../components/Legal'

export default function Support() {
  return (
    <Legal title="Support" updated="2026-11-06">
      <p>
        Questions, bugs, a decision you disagree with, or a data request? Read the{' '}
        <Link to="/faq" className="link">FAQ</Link> first, then write to us here. You do not need an account.
      </p>
    </Legal>
  )
}
