import { Link } from 'react-router-dom'
import Legal from '../components/Legal'

export default function Faq() {
  return (
    <Legal title="FAQ" updated="2026-11-07">
      <h2>Getting started</h2>
      <h3>Why verify my email?</h3>
      <p>
        You can rate without verifying, but writing reviews and replies and reporting need a
        verified email. Enter the 6-digit code we email you on the <Link to="/verify" className="link">Verify</Link> page.
      </p>
      <h3>I forgot my password.</h3>
      <p>
        Use <Link to="/forgot-password" className="link">Forgot password</Link> on the login page. A code is emailed to you.
        Accounts made with Google can add a password the same way.
      </p>

      <h2>Who sees what</h2>
      <p>
        You choose per rating, watch-list entry and custom list: <strong>Public</strong> (everyone,
        even signed out), <strong>Friends</strong> (your accepted friends) or <strong>Only me</strong>.
        Averages, counts and rankings are anonymous totals and include every rating, whatever its
        audience.
      </p>

      <h2>Your data</h2>
      <h3>Can I download my data?</h3>
      <p>
        Yes. <Link to="/settings" className="link">Settings</Link> has an export of your ratings,
        reviews, list entries and custom lists as JSON or CSV. It asks for your password.
      </p>
      <h3>Can I delete my account?</h3>
      <p>
        Yes, in <Link to="/settings" className="link">Settings</Link>. It asks for your password
        (or, if you only use Google, a code emailed to you) and removes your account, email, lists,
        watch list, friends and notifications. Your ratings, reviews, replies and reports stay on the
        site as "Deleted user", so averages and counts do not change. This cannot be undone. Details are in the{' '}
        <Link to="/privacy" className="link">privacy policy</Link>.
      </p>
      <h3>Can I import from AniList?</h3>
      <p>Yes, in Settings, by AniList username. Only public lists can be read.</p>

      <h2>Something else?</h2>
      <p>Write to us through the <Link to="/support" className="link">support form</Link>.</p>
    </Legal>
  )
}
