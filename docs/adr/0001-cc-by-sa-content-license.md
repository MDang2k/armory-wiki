# 1. Prose is licensed CC BY-SA 4.0

Status: Accepted (2026-11-20)

## Context
Hand-writing every firearm history doesn't scale. Wikipedia has good prose for most notable firearms, but it is CC BY-SA 4.0, and reusing it requires our text to carry the same license. The original plan banned Wikipedia text and assumed all-rights-reserved content.

## Decision
All prose on the site (histories, biographies, descriptions) is published under CC BY-SA 4.0. Wikipedia article text may be imported and adapted, as Draft, with a per-firearm attribution linking the exact source revision. Structured data mostly comes from CC0 Wikidata. Text from any other copyrighted source (Forgotten Weapons, books, Modern Firearms) is never copied, only cited or linked.

## Consequences
- Anyone may reuse our prose under the same terms.
- Firearm pages store `WikipediaTitle` + `WikipediaRevisionId` and render an attribution footer; the site footer states the license.
- Changing to a more restrictive license later would require removing all Wikipedia-derived text.
