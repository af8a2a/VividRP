# UnityGLTF source provenance

This directory vendors the glTF importer source from:

- Repository: https://github.com/af8a2a/UnityGLTF
- Commit: `50b2a2e82d465b5134e45970f24669ac8c2590a8`
- Upstream package version: `2.19.5`
- License: MIT; see the unmodified `LICENSE` file beside this document.

VividRP carries an import-focused integration. The UnityGLTF URP render-pipeline
helpers, ShaderGraph sources/assets, interactivity integrations, experimental
plugins, Timeline/Input System recorder glue, export menu, and package-version
build hook are intentionally omitted.
The remaining core types are kept under their upstream namespaces to minimize
the delta from the fork.

VividRP-specific importer changes are limited to:

- registering the embedded importer directly for `.gltf` and `.glb`;
- resolving its support assets from the VividRP package path;
- using UnityGLTF's legacy non-ShaderGraph fallback shaders for imported
  placeholder materials;
- using an in-memory default settings object so importing a package asset does
  not create an `Assets/Resources/UnityGLTFSettings.asset` side effect;
- replacing Unity 6.7-inaccessible editor-internal texture and humanoid helpers
  with public-API fallbacks; and
- omitting the upstream custom tabbed importer inspector and its texture-fix UI.

CornellBox scenes replace those placeholder materials with VividRP
`StandardLit` materials.
