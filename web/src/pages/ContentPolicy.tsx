import { Link } from 'react-router-dom'
import Legal from '../components/Legal'

export default function ContentPolicy() {
  return (
    <Legal title="Content policy" updated="2026-11-06">
      <p>
        Reviews and replies are for talking about anime and manga. Disagreeing is fine; hurting
        people is not.
      </p>

      <h2>Not allowed</h2>
      <ul>
        <li>Harassment, threats or bullying of a person or group.</li>
        <li>Hate: attacking people for who they are (race, religion, gender, sexuality, disability and similar).</li>
        <li>Spam, advertising, scams and repeated copies of the same text.</li>
        <li>Sexual content involving minors, and anything else illegal.</li>
        <li>Other people's private information, and pretending to be someone else.</li>
      </ul>

      <h2>Spoilers</h2>
      <p>
        Tick the spoiler box when you write a review or reply that gives away the story. The text is
        then hidden behind a button until the reader opens it.
      </p>

      <h2>Automatic filter</h2>
      <p>
        A short list of slurs is masked with asterisks when you save a review or reply. The masked
        text is what gets stored. The filter is simple and will miss things, so it does not replace
        the rules above.
      </p>

      <h2>Reporting</h2>
      <p>
        Signed-in users with a verified email can report a review, a reply or a profile and pick a
        reason: spam, harassment, hate, spoiler or other, with an optional note. You cannot report
        your own content, and you only have one open report per item. The reported text is saved with
        the report, so it can still be judged if the author edits it.
      </p>
      <p>
        An admin reads each report and either dismisses it or removes the content: a removed review
        loses its text (the scores stay), a removed reply is deleted. You get a notification with
        the outcome. Reports are not shown to the person reported.
      </p>

      <h2>Blocking</h2>
      <p>
        You can block any user. Blocking ends any friendship, stops friend requests between you, and
        hides your reviews and replies from each other.
      </p>

      <h2>Consequences</h2>
      <p>
        Content that breaks these rules is removed. Repeat or serious cases can lead to an account
        being restricted or closed. See the <Link to="/terms" className="link">Terms of Service</Link>.
        If you think we got a decision wrong, use the <Link to="/support" className="link">support form</Link>.
      </p>
    </Legal>
  )
}
