---
name: systematic-debugging
description: Use when encountering any bug, test failure, or unexpected behavior, before proposing fixes
---

# Systematic Debugging

## Overview

Random fixes waste time and create new bugs. Quick patches mask underlying issues.

**Core principle:** ALWAYS find root cause before attempting fixes. Symptom fixes are failure.

## The Iron Law

```
NO FIXES WITHOUT ROOT CAUSE INVESTIGATION FIRST
```

## The Four Phases

### Phase 1: Root Cause Investigation

**BEFORE attempting ANY fix:**

1. **Read Error Messages Carefully** - stack traces, line numbers, error codes
2. **Reproduce Consistently** - can you trigger it reliably?
3. **Check Recent Changes** - git diff, new dependencies, config changes
4. **Gather Evidence** - log what enters/exits each component boundary
5. **Trace Data Flow** - where does the bad value originate?

### Phase 2: Pattern Analysis

1. Find working examples of similar code in the codebase
2. Compare against references completely
3. Identify every difference, however small
4. Understand all dependencies

### Phase 3: Hypothesis and Testing

1. Form a single, specific hypothesis: "I think X is the root cause because Y"
2. Make the SMALLEST possible change to test it
3. One variable at a time
4. Verify before continuing

### Phase 4: Implementation

1. Create a failing test case first
2. Implement ONE fix addressing the root cause
3. Verify fix - tests pass, no regressions
4. If fix doesn't work after 3 attempts → question the architecture

## Red Flags - STOP and Return to Phase 1

- "Quick fix for now, investigate later"
- "Just try changing X and see if it works"
- "I don't fully understand but this might work"
- Proposing solutions before tracing data flow
- 3+ failed fix attempts

## Quick Reference

| Phase | Key Activities | Success Criteria |
|-------|---------------|------------------|
| **1. Root Cause** | Read errors, reproduce, gather evidence | Understand WHAT and WHY |
| **2. Pattern** | Find working examples, compare | Identify differences |
| **3. Hypothesis** | Form theory, test minimally | Confirmed or new hypothesis |
| **4. Implementation** | Create test, fix, verify | Bug resolved, tests pass |
