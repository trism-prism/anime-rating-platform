import { Link } from 'react-router-dom'
import Legal from '../components/Legal'

// bump TERMS_VERSION in api/Services/Terms.cs together with this date when the text changes
export const TERMS_UPDATED = '2026-11-06'

export default function Terms() {
  return (
    <Legal title="Terms of Service" updated={TERMS_UPDATED}>
      <p>
        AnimeRate is a hobby and portfolio project, run by one person. It is free, it comes without
        any promise of uptime, and these terms are short on purpose. By creating an account or using
        the site you agree to them.
      </p>

      <h2>Who can use it</h2>
      <ul>
        <li>You must be at least 13 years old. We ask you to confirm this when you sign up.</li>
        <li>You are responsible for what happens under your account. Keep your password to yourself.</li>
        <li>You can be signed in to up to five accounts at once, but each one must follow these terms.</li>
      </ul>

      <h2>Your content</h2>
      <p>
        Ratings, reviews, replies, reactions, lists and your profile are your content. You keep the
        rights to it. You allow us to store it and show it to the people you chose with the audience
        setting (Public, Friends or Only me), and to remove it if it breaks the{' '}
        <Link to="/content-policy" className="link">content policy</Link>.
      </p>
      <p>
        Scores feed the averages, counts and rankings shown on each title whatever audience you pick.
        Those totals are anonymous: they never show who gave a score.
      </p>
      <p>
        Only write what you are allowed to write. Do not post anything illegal, and do not post
        other people's private information.
      </p>

      <h2>Rules of the site</h2>
      <ul>
        <li>Follow the <Link to="/content-policy" className="link">content policy</Link>.</li>
        <li>Do not try to break, overload or scrape the site, or to get into other people's accounts.</li>
        <li>Do not use the site to send spam, including through the support form.</li>
      </ul>
      <p>
        We may remove content, and restrict or close an account, that breaks these rules. Reports
        from other users help us find it, but we do not promise to review everything quickly.
      </p>

      <h2>Leaving</h2>
      <p>
        You can export your data and delete your account at any time in{' '}
        <Link to="/settings" className="link">Settings</Link>. Deleting removes your data as
        described in the <Link to="/privacy" className="link">privacy policy</Link>.
      </p>

      <h2>Anime and manga information</h2>
      <p>
        Titles, covers, synopses and characters come from AniList. They belong to their owners and
        to AniList, not to us.
      </p>

      <h2>No warranty</h2>
      <p>
        The site is provided as it is. We do not promise that it will always work, that data will
        never be lost, or that content written by users is correct. To the extent the law allows, we
        are not liable for losses from using the site.
      </p>

      <h2>Changes</h2>
      <p>
        If these terms change, the date and version above change with them. Using the site after a
        change means you accept the new version.
      </p>

      <h2>Contact</h2>
      <p>Questions about these terms: use the <Link to="/support" className="link">support form</Link>.</p>
    </Legal>
  )
}
