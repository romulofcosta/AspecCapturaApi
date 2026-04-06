---
name: fix-review
description: "Verify fix commits address audit findings without new bugs"
source: "https://github.com/trailofbits/skills/tree/main/plugins/fix-review"
risk: safe
---

# Fix Review

Verify that fix commits properly address audit findings without introducing new bugs or security vulnerabilities.

## When to Use

- Reviewing commits that address security audit findings
- Verifying that fixes don't introduce new vulnerabilities
- Ensuring code changes properly resolve identified issues
- Validating that remediation efforts are complete and correct

## Review Process

1. Compare the fix against the original audit finding
2. Verify the fix addresses the root cause, not just symptoms
3. Check for potential side effects or new issues
4. Validate that tests cover the fixed scenario
5. Ensure no similar vulnerabilities exist elsewhere

## Best Practices

- Review fixes in context of the full codebase
- Verify test coverage for the fixed issue
- Check for similar patterns that might need fixing
- Ensure fixes follow security best practices
- Document the resolution approach
