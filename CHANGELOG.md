# Changelog

## 0.3.0

- Endfield P0+P1 material punch upgrade (all new controls default to 0, leaving 0.2.3 materials unchanged):
  - Baked AO fallback from `_ShadowBorderMask.r` when `_PBRMap` is unbound (outside face and eye), plus Jimenez 2016 Multi-Bounce Colored AO (`_EFMultiBounceAO`) and 6-tap character-only depth Contact AO (`_EFContactAO`, `_EFContactAORadius`, `_EFContactAOBias`).
  - Screen-space normal Curvature micro-shading (`_EFCurvature`, `_EFCurvatureScale`) outside face/eye/hair.
  - Cloth/metal environment reflection contrast (`_EFEnvContrast`) with view-reflection (`viewR`) high-contrast chrome dome, Schlick metal grazing tint (`_EFMetalGrazingColor`), dark-safe non-metal microfiber Fabric Sheen (`_EFFabricSheen`), and dielectric Clearcoat (`_EFClearcoat`, `_EFClearcoatSmoothness`) isolated from metals (`1 - metallic`) with sub-pixel bevel AA and 82% micro-normal retention.
  - Lowered Endfield GSAA minimum floor from `0.5` to `0.15` so pebbled leather and knit normal maps retain micro-specular crispness.
  - Fixed `_GlitterColor` Gamma-HDR metadata parity across all four shaders (`[FairyToonHDR]`, matching lilToon `[lilHDR]` in Linear color space).
  - `FairyToonTextureTools.PackPBRFromLilToonMaterial` helper for packing lilToon metal/MatCap masks, smoothness, and AO maps into an Endfield `_PBRMap`.
  - Lossless opt-in Endfield 0.3.0 upgrade pipeline in `FairyToonConverter` (`FairyToonConvertOptions.UpgradeToEndfield`, `FairyToonEndfieldUpgrader`, context menu `Assets/FairyToon/Convert lilToon Material to Endfield (copy)`, and converter window controls) with deterministic surface & BrightSilver/SmokedGunmetal classification, automatic `_PBRMap` packing, authored Outline/RimShade/Rim/Glitter preservation, and `FairyToonConvertProfile` precedence.

## 0.2.3

- Camera-aligned character shadows sample only the caster's own atlas tile. The main-light atlas can still cast between characters, and Unity scene shadows are independent.
- `FairyToonCharacter` uses the shader user value of its mesh renderers for the atlas owner while enabled and restores the previous value on disable.

## 0.2.2

- Main Color 2nd/3rd Reset UV matches the ten writes in lilToon 2.4.1. Texture tiling, offset and Scroll/Rotate survive the reset. Animation preview callbacks and grouped Undo still apply.

## 0.2.1

- Transparent hair converted to Cutout moves out of the transparent queue. Reset UV on Main 2nd/3rd respects animation preview and groups asset edits into one Undo. The first TwoPass Fur shell starts with depth writes and alpha to coverage enabled.

- The inspector now follows lilToon's section boxes, HDR swatch, and shared en/ja/zh-Hans labels where the shader has the property.
- Keyword sync is per material for the write that just happened. Paste and Reset use one undo group and ignore a texture scale when that texture property is absent. From Shader keeps render queue -1. Dissolve edits keep the other vector components. Decal uses Mirror Mode and Copy Mode.
- Inspector layout follows lilToon 2.4.1 where the shader has the property: version and help foldouts, rendering mode under search, feature toggles inside the section, nested shadow and rendering boxes, MatCap order, and blend-mode labels. Sections the shader does not implement stay absent. FairyToon-only sections stay.
- Inspector only. Advanced / Preset / Shader Setting replace Simple / Advanced. Search matches the drawn label and tooltip, not only the property name. Main Color 1st is a label and gear; Color Adjust shows the mask, then hue / saturation / value / gamma, with a reset. Alpha Mask puts its gear on the Mode row, including on Refraction, and shows only a warning on Opaque. Scalar reads follow ShaderLab: legacy `Int` is a float, `Integer` is a real int. Paste and Reset do nothing when the values already match. Keyword sync runs when a property write hits the asset, including animation mode with no callback, and is skipped only when `applyPropertyCallback` returns true. Texture scale still copies to the first target and can be undone. Rendering uses Unity's queue, instancing, and double-sided GI controls. Language can follow the editor. No shader or material-default change.

## 0.2.0 (2026-11-17)

- Endfield: **Exposure** on `FairyToonCharacter` (default 1) scales the normalised main light without changing the sun / shade state, plus **Follow Light Intensity** and **Reference Luminance** so a dim light gives a dim key. Global `_FT_EFExposure`.
- Endfield: **ambient cap** on `FairyToonCharacter` (off by default): Ambient Max Luminance, Ambient Saturation and Light Ceiling, so a strongly coloured scene probe cannot wash a character out. Global `_FT_EFAmbientCap`.
- Defaults and materials without `FairyToonCharacter` render as in 0.1.0. No material, keyword or sampler change.

## 0.1.0

- As developed in LiminalNW (`Packages/com.nw.fairytoon`, LiminalNW 106c8bd). That history stays in LiminalNW; this repository starts from it.
