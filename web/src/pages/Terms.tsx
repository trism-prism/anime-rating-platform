import { Link } from 'react-router-dom'
import Legal from '../components/Legal'
import pageMeta from '../pageMeta.json'

// bump TERMS_VERSION in api/Services/Terms.cs together with this date when the text changes
export const TERMS_UPDATED = '2026-11-07'

export default function Terms() {
  return (
    <Legal title={pageMeta['/terms'].title} updated={TERMS_UPDATED}>
      <p>
        AnimeRate is a hobby and portfolio project, run by one person. It is free, it comes without
        any promise of uptime, and these terms are short on purpose. By creating an account or using
        the site you agree to them.
      </p>

      <h2>Eligibility</h2>
      <p>
        You must be at least 13 years old. You are responsible for what happens under your account,
        so keep your password to yourself.
      </p>

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

      <h2>Your responsibilities</h2>
      <ul>
        <li>Follow the <Link to="/content-policy" className="link">content policy</Link>, and post nothing illegal.</li>
        <li>Do not try to break, overload or scrape the site, or to get into other people's accounts.</li>
        <li>Do not use the site to send spam, including through the support form.</li>
      </ul>
      <p>
        We may remove content, and restrict or close an account, that breaks these rules. We do not
        promise to review every report quickly.
      </p>

      <h2>Leaving</h2>
      <p>
        You can export your data and delete your account at any time in{' '}
        <Link to="/settings" className="link">Settings</Link>. Deleting removes your personal data and anonymizes your ratings, reviews, replies and reactions
        (they stay as "Deleted user"), as described in the <Link to="/privacy" className="link">privacy policy</Link>.
      </p>

      <h2>Anime and manga information</h2>
      <p>
        Titles, covers, synopses and characters come from AniList. They belong to their owners and
        to AniList, not to us.
      </p>

      <h2>Disclaimer</h2>
      <p>
        The site is provided as it is. We do not promise that it will always work, that data will
        never be lost, or that content written by users is correct. To the extent the law allows, we
        are not liable for losses from using the site.
      </p>

      <h2>Updates to these terms</h2>
      <p>
        If these terms change, the date above changes with them. Using the site after a change means
        you accept the new version. Questions: use the <Link to="/support" className="link">support form</Link>.
      </p>
    </Legal>
  )
}
