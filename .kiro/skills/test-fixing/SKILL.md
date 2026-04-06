---
name: test-fixing
description: Run tests and systematically fix all failing tests using smart error grouping. Use when user asks to fix failing tests, mentions test failures, runs test suite and failures occur, or requests to make tests pass.
---

# Test Fixing

Systematically identify and fix all failing tests using smart grouping strategies.

## When to Use

- Asked to fix tests or make tests pass
- Test suite is broken or CI/CD failing
- After a refactor that broke tests

## Systematic Approach

### 1. Initial Test Run
Run the test suite and analyze output for total failures, error types, and affected modules.

### 2. Smart Error Grouping

Group failures by:
- **Error type**: compile errors, null refs, assertion failures, etc.
- **Module/file**: same file causing multiple failures
- **Root cause**: missing dependencies, API changes, logic bugs

Prioritize: highest impact first, infrastructure before functionality.

### 3. Fix Order Strategy

1. **Infrastructure first**: compile errors, missing dependencies, config issues
2. **API changes**: signature changes, renamed symbols, module reorganization
3. **Logic issues**: assertion failures, business logic bugs, edge cases

### 4. For Each Group

1. Identify root cause (read code, check git diff)
2. Implement minimal focused fix
3. Run subset of tests to verify
4. Move to next group only after current passes

### 5. Final Verification

Run complete test suite and verify no regressions.

## Best Practices

- Fix one group at a time
- Keep changes minimal and focused
- Don't move on until current group passes
