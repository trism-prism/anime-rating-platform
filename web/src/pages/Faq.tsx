import { Link } from 'react-router-dom'
import Legal from '../components/Legal'

export default function Faq() {
  return (
    <Legal title="FAQ" updated="2026-11-06">
      <h2>Accounts</h2>
      <h3>How do I sign up?</h3>
      <p>
        With an email and a password of at least 12 characters (passwords found in known data
        breaches are refused), or with Google. You confirm you are 13 or older and accept the{' '}
        <Link to="/terms" className="link">Terms of Service</Link>.
      </p>
      <h3>Why verify my email?</h3>
      <p>
        You can rate without verifying, but writing reviews and replies and reporting need a
        verified email. We email a 6-digit code; open <Link to="/verify" className="link">Verify</Link> to enter it.
      </p>
      <h3>I forgot my password.</h3>
      <p>
        Use <Link to="/forgot-password" className="link">Forgot password</Link> on the login page. A code is emailed to you.
        Accounts made with Google can add a password the same way.
      </p>
      <h3>Can I be signed in to more than one account?</h3>
      <p>Yes, up to five. Use Switch account in the avatar menu.</p>

      <h2>Ratings and reviews</h2>
      <h3>How does rating work?</h3>
      <p>
        You give four scores from 1 to 10: story, art, characters and enjoyment. The overall score is
        their average. You can change or delete a rating whenever you like. For manga with a known
        number of volumes you can also rate single volumes.
      </p>
      <h3>How do I write a review?</h3>
      <p>
        Add text (up to 2000 characters) when you rate. Mark it as a spoiler if it gives away the
        story. Other people can reply (up to 1000 characters) and react with helpful, not helpful or funny.
      </p>

      <h2>Audiences</h2>
      <h3>Who can see my ratings and reviews?</h3>
      <p>
        You choose per rating, watch-list entry and custom list: <strong>Public</strong> (everyone,
        even signed out), <strong>Friends</strong> (your accepted friends) or <strong>Only me</strong>.
        Averages, counts and rankings are anonymous totals and include every rating, whatever its
        audience.
      </p>

      <h2>Friends and blocking</h2>
      <h3>How do friends work?</h3>
      <p>
        Send a request from someone's profile. They accept or decline it. A request nobody answers
        disappears after 30 days. Friends see your Friends-only items and show up in each other's
        activity feed.
      </p>
      <h3>How do I stop someone?</h3>
      <p>
        Block them from their profile. Blocking ends any friendship and hides your reviews and
        replies from each other.
      </p>

      <h2>Reports</h2>
      <h3>How do I report something?</h3>
      <p>
        Use Report on a review, a reply or a profile. An admin reviews it and you get a notification
        with the outcome. More in the <Link to="/content-policy" className="link">content policy</Link>.
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
        (or, if you only use Google, a code emailed to you) and removes your account and everything
        you added. This cannot be undone. Details are in the{' '}
        <Link to="/privacy" className="link">privacy policy</Link>.
      </p>
      <h3>Can I import from AniList?</h3>
      <p>Yes, in Settings, by AniList username. Only public lists can be read.</p>

      <h2>Still stuck?</h2>
      <p>Write to us through the <Link to="/support" className="link">support form</Link>.</p>
    </Legal>
  )
}
