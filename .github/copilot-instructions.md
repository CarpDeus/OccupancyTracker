# Unit test generation

When asked to create or update unit tests:

- Read the production code and nearby tests first. Test the requested behavior and relevant regressions, not implementation details.
- Add tests to `Monolithic/OccupancyTracker.Tests`. Organize them by the type of object under test (for example, `Services` or `Repositories`), then by object name (for example, `AdminProfileOperations`).
- Use NUnit as the testing framework. Mark every test with NUnit's `[Test]` attribute. Do not use test setup methods: each test must be self-contained, including creating its required objects and setting up all Moq return values within that test. Code duplication between test methods is acceptable and preferred over shared setup.
- Use Moq for necessary dependencies, but do not mock configuration. Create a `Dictionary<string, string>` with the required values, then build configuration with `ConfigurationBuilder().AddInMemoryCollection(configForTest).Build()`.
- Structure each test using Arrange-Act-Assert sections, with clear `// Arrange`, `// Act`, and `// Assert` markers. Define expected values, including error messages, in the Arrange section; use `const` for values whose types support compile-time constants. Give test methods descriptive names in the form `MethodName_Scenario_ExpectedBehavior()`.
- Use FluentAssertions for validation. Each test should verify only one result, and should cover relevant valid, invalid, boundary, and failure scenarios. Consider edge cases such as large files, malformed input, and permission errors. Verify relevant mock interactions as well as the primary result. For async exceptions, use FluentAssertions' `.Should().ThrowAsync()` assertions.
- Keep tests deterministic and self-contained: do not call real external services or rely on secrets, ambient machine state, or wall-clock timing. For database behavior, prefer SQLite in-memory so relational behavior is exercised; for API behavior, follow the existing `WebApplicationFactory` test pattern and replace external dependencies with test doubles.
- Ensure files and directories created by tests are cleaned up even if a test fails. Use unique paths per test to avoid conflicts during parallel execution.
- Run the focused test(s) while iterating, then run `dotnet test Monolithic/OccupancyTracker.sln` before handing off when feasible. Report any tests that could not be run.
