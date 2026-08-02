import { Link } from 'react-router-dom'
import Legal from '../components/Legal'

export default function Privacy() {
  return (
    <Legal title="Privacy policy" updated="2026-11-07">
      <p>
        AnimeRate is a hobby and portfolio project run by one person. It keeps only what it needs to
        work. We do not sell your data, we show no ads, and we use no tracking or analytics.
      </p>

      <h2>Information we collect</h2>
      <ul>
        <li><strong>Account:</strong> your email address, display name, the date you joined, and a hash of your password (never the password itself). If you sign in with Google we store the Google account id and the email Google gives us, and no password.</li>
        <li><strong>Sign-up:</strong> that you confirmed you are 13 or older and accepted the Terms, with the time and the version of the Terms.</li>
        <li><strong>Sign-ins:</strong> for each signed-in device, the browser description, the IP address it signed in from, when it was last used, and a hash of its sign-in token.</li>
        <li><strong>What you add:</strong> ratings, reviews, replies, reactions, your watch list with progress and dates, custom lists, and the audience you chose for each.</li>
        <li><strong>Social:</strong> friend requests and friendships, the users you blocked, your notifications and your notification preferences.</li>
        <li><strong>Support form:</strong> your name (optional), email and message.</li>
        <li><strong>Reports:</strong> when you report something we store who reported it, the reason, your optional note, and a copy of the reported text. Admins read them.</li>
      </ul>

      <h2>Visibility</h2>
      <p>
        Your email is private. Your display name, profile, ratings, reviews and lists are shown
        according to the audience you pick: Public, Friends or Only me. Averages, counts and
        rankings add up every rating whatever its audience and never show who gave a score. Admins
        can see anything in order to moderate it.
      </p>

      <h2>Uses of your information</h2>
      <ul>
        <li>Running the site: signing you in, showing your content to the people you chose, and sending notifications.</li>
        <li>Email: a 6-digit code to verify your email, a code if you reset your password or delete a Google-only account, a link if you change your email, and a notice when your account is deleted.</li>
        <li>Support: messages sent through the <Link to="/support" className="link">support form</Link> are emailed to the admins, who reply to the address you gave. The site does not store them. They stay in the admins' mailboxes until deleted, and you can ask for them to be deleted.</li>
        <li>Security: passwords are checked against known breaches, and failed sign-ins lock an account for a few minutes.</li>
      </ul>

      <h2>Cookies</h2>
      <p>
        We use only strictly necessary cookies, so there is no cookie banner. After you sign in, an
        HttpOnly cookie (named rt_ followed by your account id) keeps you signed in until it
        expires (30 days after last use, 90 at most). While you sign in with Google, a cookie lasting ten minutes protects the sign-in
        from forgery. Your browser's local storage keeps the names and emails of the accounts signed
        in on this device so the account switcher works.
      </p>

      <h2>Service providers</h2>
      <ul>
        <li><strong>Railway</strong> hosts the server and the database.</li>
        <li><strong>Vercel</strong> hosts the website and passes requests to the server, so it sees them, with IP addresses, in its logs.</li>
        <li><strong>Google</strong> if you use Sign in with Google (we receive your email and Google account id), and its Gmail service, which may send our emails.</li>
        <li><strong>Resend</strong> may send our emails instead of Gmail. Either one sees the address and the text of each email.</li>
        <li><strong>AniList</strong> supplies the anime and manga data. If you import your list, the AniList username you type is sent to AniList to read your public list. Scores are not imported.</li>
        <li><strong>Have I Been Pwned</strong> checks passwords against known breaches. Only the first five characters of a hash of the password are sent, which is not enough to identify the password.</li>
      </ul>
      <p>These services may run in other countries. We do not share your data for advertising.</p>

      <h2>Retention</h2>
      <ul>
        <li>Your account and personal data: until you delete your account. Your ratings, reviews, replies, reactions and the reports you filed are kept after that without your name, as "Deleted user".</li>
        <li>Sessions: until they expire (30 days after last use, 90 at most) or you sign out.</li>
        <li>Reports: settled reports are deleted after one year. Open reports stay until an admin settles them.</li>
        <li>Notifications: deleted after 90 days.</li>
        <li>Emailed codes are not stored; they expire within minutes.</li>
        <li>Server and host logs: kept by Railway and Vercel under their own rules.</li>
      </ul>

      <h2>Your rights and controls</h2>
      <ul>
        <li><strong>Download your data</strong> in <Link to="/settings" className="link">Settings</Link>: ratings, reviews, list entries and custom lists as JSON or CSV.</li>
        <li><strong>Delete your account</strong> in Settings. It removes your email, password hash, Google link, sessions, lists, watch list, friends, blocks, notifications and settings, and your name; of the date you joined only the month is kept. Your ratings, reviews, replies, reactions and the reports you filed stay without your name or email, shown as "Deleted user", so averages and counts do not change. Reviews set to Friends or Only me stay hidden from everyone but admins.</li>
        <li><strong>Correct</strong> your display name, password, email, audiences and notification preferences at any time.</li>
        <li><strong>Ask</strong> through the <Link to="/support" className="link">support form</Link> for anything else you want done with your data.</li>
      </ul>

      <h2>Children</h2>
      <p>The site is for people aged 13 and over. If you think a younger child has an account, tell us through the <Link to="/support" className="link">support form</Link> and we will delete it.</p>
    </Legal>
  )
}
