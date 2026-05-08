---
id: ref-testing
scope: [codex, copilot]
category: reference
requires: [pack-tests]
---
# Testing checklist

- Was changed behavior covered by new or updated tests?
- Do the tests cover the main path plus relevant failure or edge cases?
- Are test names explicit about the scenario and expected behavior?
- Are external dependencies and time handled explicitly rather than through shared state or timing assumptions?
- Were the repository checks needed for this change executed?
