# Repository Agent Guidelines

## Development

- Prioritize working Unity Editor features and reusable tests over additional documentation.
- Preserve existing user changes and do not overwrite unrelated assets.
- Do not copy proprietary code, assets, schemas, or business rules from other projects.
- Do not commit or push unless explicitly requested.
- Inspect git status before editing; distinguish feature work, reusable fixtures, disposable outputs, and pre-existing user changes.
- Preserve the existing Git index. When the user requests an OK review gate, do not stage the proposed changes until that explicit confirmation arrives; do not unstage or restage unrelated work.

## Asset Safety

- Do not modify original Template or Source Model assets during content creation.
- Preserve `.meta` files and GUIDs when moving reusable test fixtures.
- Do not overwrite existing generated Prefabs.
- Keep temporary Prefab instances isolated from the active Scene; the Creator owns both instances and the preview scene and must clean them in finally on success and failure.
- Before editing scenes, Prefabs, or serialized assets, detect the running Editor with unity status; use live Editor APIs when reachable, not raw YAML edits. Diagnose connection/Safe Mode failures before assuming no Editor exists.
- Before deleting an asset, confirm ownership and incoming dependencies. Never delete unknown or still-referenced user assets; move reusable fixtures through AssetDatabase.MoveAsset and preserve GUIDs.
- Keep the Generic Reference contract compatible: fixed field names/types remain stable; String and Object values use separate serialization fields. Do not resolve by name or the first matching component, and do not impose renderer-name uniqueness to avoid identity checks.
- Apply references only to temporary instance containers, with explicit Template/SourceModel origins. Inherited references remain unchanged unless the Draft requests an override; invalid or ambiguous identity must block success.

## Testing

For changes affecting Prefab creation or Reference Authoring, verify:

- Unity Editor compilation.
- Save → Reload reference identity and runtime getters.
- Variant base and Nested Model source links.
- Original asset and `.meta` integrity.
- Preview Scene cleanup and active Scene state.
- Failed operations never report success.

Use existing safe checks and integration tests where applicable. Do not substitute static compilation for a live Editor result, and clearly state unavailable checks.

- Use the project Editor version in ProjectSettings/ProjectVersion.txt. Recompile through the connected public-project Editor and inspect recompile_status / Console; do not close or restart unrelated user Editors.
- Run ContentVariantCreatorChecks.Run(), ContentReferenceAuthoringChecks.Run(), ContentReferenceIntegrationChecks.RunPreview(), and ContentReferenceIntegrationChecks.Run() as appropriate; the integration runner includes the existing checks.
- Persistence checks must force import/reload and compare key/type/count/value/identity, including Runtime getters, fixed overrides, nonempty inheritance, multiple collectors, duplicate names/types, list edits, Texture assets, and nested references.
- Test duplicate output rejection, stale Model selections, wrong types, duplicate keys, and apply failures. Diagnostics must retain Key, Origin, Property Path, Expected/Actual identity, and the underlying reason.
- Keep UI/manual acceptance distinct from automated checks. Re-run broad tests only when code, dependencies, failures, or unresolved concerns justify it.

## Test Outputs

- Reusable fixtures belong in `Assets/Game/Tests/Fixtures/ReferenceAuthoring/`; BaseTemplate, SmokeTemplate, SourceModel, their .meta files, and required folder .meta files are a single dependency-preserving unit. Do not generate a timestamped replacement fixture set for each run.
- Do not commit `Content_RA_Test_*` generated outputs.
- Report test results in the Codex conversation or Unity Console; do not generate per-run Markdown reports.
- Use `ContentReferenceIntegrationChecks.Run(keepOutputs: true)` when persistent debug artifacts are required.
- Clean up only outputs owned by the current test run, after confirming they have no external dependencies. Preserve outputs on failure for investigation.

## Documentation

- Keep README focused on setup, features, usage, and limitations.
- Update documentation only when user-facing behavior or long-lived contracts change.
- Do not create milestone, implementation, review, or test-report Markdown files unless explicitly requested or the document has a clear long-term purpose.
- README is the user entry point: current features, startup, Create Content steps, prerequisites, output paths, limitations, and Planned Features. AGENTS.md holds long-lived contribution / safety / verification rules.
- Personal Unity API study notes, ownership learning tables, and detailed Inspect/Resolve/Apply walkthroughs belong in the user-designated existing Obsidian notes, not in README or per-task repository reports. Preserve provenance and existing vault content.
- Do not delete a source document until necessary content is integrated and any requested external-note write succeeds. Validate local links and remove references to retired documents.
- Keep Excel, Content/Art Assets Browser descriptions and third-party credits/license records when updating README. Preserve reusable fixtures and avoid speculative framework documentation.