# Unit test generation

When asked to create or update unit tests:

- Read the production code and nearby tests first. Test the requested behavior and relevant regressions, not implementation details.
- Add tests to `Monolithic/OccupancyTracker.Authorization.Tests` unless the repository has since established a more appropriate test project. Keep the existing xUnit conventions and nullable-enabled C# style; use descriptive behavior-based test names.
- Prefer focused tests with clear arrange, act, and assert phases. Cover the normal outcome and meaningful boundary, invalid-input, and failure cases for the behavior being changed. Include authorization and data-isolation cases when relevant.
- Keep tests deterministic and self-contained: do not call real external services or rely on secrets, ambient machine state, or wall-clock timing. Use the existing test fixtures and fakes where suitable. For database behavior, prefer SQLite in-memory so relational behavior is exercised; for API behavior, follow the existing `WebApplicationFactory` test pattern and replace external dependencies with test doubles.
- Do not add test dependencies or create another test project unless the existing setup cannot reasonably cover the requested behavior.
- Run the focused test(s) while iterating, then run `dotnet test Monolithic/OccupancyTracker.sln` before handing off when feasible. Report any tests that could not be run.
