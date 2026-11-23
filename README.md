# FairyToon

Maintained in this repository since 2026-11-17 (earlier history in LiminalNW); see `Documentation~/MAINTAINING.md` and `CHANGELOG.md`.

Toon-first URP shaders using lilToon-named properties: FairyToon plus separate Refraction, Fur and Gem variants. The ordinary Reflection layer is off by default.

## Use

- Rendering Mode selects Opaque, Cutout (optional alpha to coverage for hair), Transparent, Refraction, Fur or Gem. `_TransparentMode` remains body coverage 0 / 1 / 2; special modes select separate shaders.
- Main Color 2nd / 3rd, Reflection, MatCap / MatCap 2nd, Rim Light, Rim Shade and Emission / Emission 2nd use the same lilToon properties in both shading models. Rim Shade multiplies the albedo at grazing angles before lighting; its alpha and red-channel mask control the strength.
- Feature toggles are lilToon's `_Use*` floats; `FairyToonMaterialSetup.Apply` (run by the inspector) turns them into keywords and enables the outline pass. Call it after setting properties from script.
- Face shadow map: Shadow > Mask Type = Face SDF, `_ShadowStrengthMask` in lilToon's format (R / G thresholds for light from either side, B blend back to normal shading, A strength). Add `FairyToonFaceShadow` to the avatar root so the map follows the head bone.
- Outline width source `_OutlineVertexR2Width` = 3 reads a smoothed normal from UV7 (tangent space, W = coherence). Bake it with **Assets > FairyToon > Bake Smooth Normals (UV7)** on a model: it welds vertices by position across every mesh of the model (so split meshes such as clothes share a normal), averages angle-weighted face normals and runs again on every reimport. Low coherence (hard corners) tapers the line (`_OutlineCornerTaper`).
- Outline Width Mode (`_OutlineMode`): Object (lilToon, cm) or Screen: `_OutlinePixelWidth` pixels at 1080p, even on screen, floored at `_OutlineMinPixels` whole pixels so thin lines do not crawl, faded out between `_OutlineFadeStart` and `_OutlineFadeEnd` metres.
- Inspector: lilToon 2.4.1's top bar (Advanced / Preset / Shader Setting) and foldout order. Advanced shows every property. Preset is a placeholder. Shader Setting is FairyToon-only information (language lives in the top bar, including Auto, which follows the Unity Editor; there is no global variant strip). Each section has a gear menu: Copy, Paste, Paste (with texture), Reset, and Open manual where lilToon has a manual anchor. English, Japanese, and Simplified Chinese. See `THIRD_PARTY_NOTICES.md`.

## Wetness

- Enable **Wetness** on the main FairyToon material and add `FairyToonWetness` to the character root. `FairyToonWaterVolume` polls the box's XZ footprint and top surface without requiring a Rigidbody, or integrations can call `SetWaterSurface(y)` / `ClearWaterSurface()`. Drying defaults to 90 seconds; `SoakHeight` and `Wetness` can seed a splash. The package has no KWS2 or game-code dependency.
- Porosity and the UV-stable soak front retain step-1 mild global darkening. Raw-UV rivulets add stronger local darkening, reduced transparency, wandering splits/rejoins and film-only edge bumps. They move down the derivative-snapped gravity axis while fresh, settle over eight seconds, and flow under positive rain wetness.
- Both modes add an anisotropic GGX water coat (F0 0.02), scene/character shadows and a sharp probe reflection. Endfield retains its own specular. In lilToon this replaces the old isotropic wet lobe: **`_WetSpecular` remains an extra multiplier on `_WetFilm`, its reflection and drops**. `_WetSmoothness` controls both modes; trails raise film smoothness to at least 0.95.
- Sparse static Voronoi droplets have dome normals and sharp direct-light glints. Streaks/drops are hidden below the current water surface. Outline retains the global darkening; Forward and SeeThroughHair share all details.
- **Wet details** controls, in material-buffer order:

| Property | Default | Use |
| --- | ---: | --- |
| `_WetStreakAmount` | 0.55 | Fraction of streak columns populated. |
| `_WetStreakScale` | 0.006 | UV length per cell (not frequency); target approximately 6 mm after mesh-density calibration. |
| `_WetStreakDarken` | 0.45 | Trail darkening and transparent-alpha strengthening; alpha-zero holes remain empty. |
| `_WetStreakBump` | 0.15 | Film normal tilt along trail edges. |
| `_WetStreakFlow` | 0.03 | Approximate m/s at calibrated 6 mm cells; per-column variation, slowing to rest by 8 s. |
| `_WetStreakAxis` | 0 | 0 Auto derivative snap; 1 +V down, 2 -V, 3 +U, 4 -U. Pin for unstable seams. |
| `_WetFilm` | 0.7 | Added coat/reflection/drop intensity. |
| `_WetFilmStretch` | 0.85 | Along-gravity anisotropy (0 isotropic, 1 long narrow bands). |
| `_WetDropAmount` | 0.04 | Probability of a drop in a cell. |
| `_WetDropScale` | 0.025 | UV length per drop cell; target approximately 25 mm after calibration. |

- Scales should be `sqrt(UV triangle area / world triangle area) * desired cell size in metres`; the lab measures/logs these per renderer/submesh, using per-slot copies for captures. Shared assets use an area-weighted estimate; distorted UVs and seam-axis stability still need visual tuning.
- Detail cost above the existing soak noise: one vector hash for streaks, one 3×3 Voronoi search (nine vector hashes) for enabled drops, and one film probe lookup. Analytic trail gradients avoid extra pattern evaluations. Zero wet mask skips details; disabled features skip their own work. No new textures. Probe filtering is isotropic at the film's cross-gravity roughness; the direct coat is anisotropic. Existing specular clamp at 20 is retained.
- Rain uses the existing smoothed rain-wetness global, so residual rain continues flowing until dry; transitions between rain and departure clocks may change the trail phase.
- Shared globals are `_FT_Wet0 = (world soak line, wetness, water surface or -1e5, seconds since leaving)` and `_FT_Wet1 = (rain wetness, edge width, 0, 0)`. They support **one wet character at a time**. Multiple independently wet characters need a future per-renderer path.
- `_UseWet` defaults off and derives `_FT_WET` in `FairyToonMaterialSetup.Apply`. Only Forward, SeeThroughHair and Outline compile the fragment feature; all properties remain unconditional in the shared material buffer. Separate Refraction, Fur and Gem shaders do not expose this wetness feature.
- Offline checks: `node Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs`; `CompileShaders.ps1 -Shaders FairyToon -Wetness Both` covers dry/wet colour passes, constant-buffer layout and absence of wet vertex globals. Unity import, 6000.6.0f1 Editor compilation and visual captures still require a permitted Unity session.

## Colour layers and emission

- Main Color 2nd / 3rd: lilToon names and defaults, HDR tint, texture scale / offset, angle in radians, scroll XY / rotation speed W, UV0..UV3 or MatCap, red-channel blend mask, Normal / Add / Screen / Multiply, per-layer lighting and culling. A layer's lit portion blends into the albedo before Rim Shade and lighting; its unlit portion blends after diffuse lighting, before Reflection / MatCap / Rim / Emission.
- Decals: tiling, bounds clipping, left / right by tangent handedness (not object position), copy, flip mirror / copy, MSDF. Layer alpha None / Replace / Multiply / Add / Subtract works on Cutout / Transparent, including body depth, normals, shadow, character shadow, motion and see-through coverage. The main alpha mask applies after layer alpha. Outline and the MotionVectors outline hull use only base main-texture / tint alpha plus the main alpha mask; main-layer alpha and per-face culling never affect either hull. Decal edges use saturated normal-mapped N.V in Forward, and geometric N.V in coverage passes.
- Emission 1st / 2nd: tint / map / mask (RGBA), independent map and mask scale / offset / scroll / rotation, UV0..UV3 or Rim, blend mode / strength, main-colour strength, sine / hard blink, animated gradient texture, and fluorescence. Gradient lookup repeats along U at V=0.5. Transparent emission matches lilToon's premultiplied on-screen result while retaining FairyToon's SrcAlpha blend state; Add contributes emission times surface alpha exactly once (including Emission 1st legacy Add). Normal / Screen / Multiply account for the premultiplied destination too, without division at zero alpha. Emission main-strength uses the layered albedo before Rim Shade.
- One `_FT_MAIN_LAYERS` keyword covers both main layers in Forward and body coverage passes; Outline has no main-layer variants. `_FT_EMISSION` covers both emissions in Forward and SeeThroughHair. Runtime toggles keep 3rd-only and emission-2nd-only materials independent. Main2nd and Emission 1st use their own texture samplers; Main3rd, Emission 2nd and masks retain `sampler_MainTex`, and gradients reuse `sampler_ft_linear_repeat`. This keeps the optical variants within the DX11 sampler budget.
- UV1..UV3 use two packed interpolators only in variants that can read extra UVs. Normal Map 2nd / Glitter have UV0 and extra-UV alternatives within their existing feature groups; UV0-only materials with main layers / emission off carry no added context in Forward / SeeThroughHair or DepthNormals. Main layers / emission retain their shared UV1..UV3 interface. Coverage geometry is separate: DepthNormals reuses its normal and handedness, and only clipped main layers require world position. Other body coverage passes need normal / handedness and position for decals / MatCap UVs. Outline and the motion hull carry none. Emission Rim uses absolute shading N.V; main MatCap UVs remain geometric.
- Both emission masks always use raw UV0 followed by their own scale / offset / scroll / rotation, including zero-speed masks. This matches stock lilToon, which defines `LIL_FEATURE_ANIMATE_EMISSION_MASK_UV`.
- Main Color 2nd/3rd Reset UV clears UV Mode, six Decal flags and Angle. It restores Decal Animation to (1, 1, 1, 30) and Decal Sub-Parameters to (1, 1, 0.01, 1). Tiling, offset and Scroll/Rotate keep their values. The two vectors are stored for material compatibility. Atlas animation playback is unsupported.
- Skipped: layer distance fade / AudioLink; emission parallax / AudioLink; lilToon's gradient-authoring metadata (`_eg*` / `_e2g*`). Assign the baked gradient texture directly. The converter continues reporting active unsupported sub-options as dropped values.
- Existing converted materials need `FairyToonMaterialSetup.Apply` (or an inspector edit) to refresh the shared keywords. Name-based conversion copies all supported values and texture transforms; no game or lilToon assembly dependency is added.

Older FairyToon materials can change appearance: emission masks now multiply RGBA (mask alpha previously had no effect), Emission Main Strength uses the layered albedo before Rim Shade (previously after it), and static emission masks use raw UV0 plus their own transform (previously transformed main UV). The single-alpha Add result is preserved apart from these intentional input changes.

## Two-sided cloth and reflection parity

- `_FlipNormal` follows lilToon (default 0) in Toon Forward / SeeThroughHair: only the mapped normal flips, with geometric `origN` retained. Endfield always faces mapped and geometric lighting normals on backs, independently of `_FlipNormal`; Face uses the negative geometric normal. Front faces are unchanged. DepthNormals always faces the final mapped normal towards the camera in both models, including octahedral output, for SSAO / depth rim. Gem retains its one interior reversal/tilt in either shading model; Fur shells retain geometric normals.
- `_BackfaceForceShadow` scales Endfield lit, view-light and deeper-tone ramp weights, including Eye overrides. It does not multiply final RGB: independent additive lights, specular and emission can remain visible, as in the existing toon path. `_BackfaceColor` (HDR, default transparent black; Base Setting > Advanced) now works in both models after emission and before fog: alpha blends toward its color times the model's light color, without changing surface coverage. Transparent materials use the post-premultiply conversion described below, including its alpha-floor, zero-alpha and HDR limits. Normal flipping, force shadow and backface color are independent controls.
- Toon shading supports the third shadow colour, its blue AO / blur channels, border, normal strength and receive strength. Its default alpha is 0; Endfield continues using its own diffuse shading.
- Custom cubemap overrides multiply the decoded sample by `_ReflectionCubeColor.rgb` and `lerp(1, lightColor, _ReflectionCubeEnableLighting)` (lighting defaults to 1). Probe reflections use world position and screen UV for URP local-probe blending and clustered lookup. MatCap RGB masks and reflection maps apply their own scale / offset after the main UV transform; their imported sRGB sampling is unchanged.
- `_Metallic` and `_Reflectance` retain lilToon's `[Gamma]` attributes in the main, Refraction, Fur and Gem shaders. Materials authored directly in FairyToon use the same sRGB slider values as lilToon: in a Linear project, Reflectance 0.04 uploads approximately 0.0031, and Metallic 0.5 uploads approximately 0.214; in a Gamma project these values remain 0.04 and 0.5. These sliders are not linear physical values. Converted lilToon values stay unchanged; no automatic migration or rescaling is applied to directly authored FairyToon materials.
- Texture scale / offset controls are exposed wherever the shader consumes that texture's `_ST`, including both MatCap masks, Smoothness / Metallic / Reflection Color maps, the second-normal scale mask, anisotropy scale mask and Backlight color map. This exposes already imported transforms for editing without changing saved scale / offset or texture sampling colour space.
- Toon direct specular uses URP main-light `shadowAttenuation` in ordinary, cascade and screen-shadow modes, including toon-shaped highlights. Endfield retains its artistic `shadowmix`. Shared Reflection roughness has a 0.002 floor so Smoothness 1 keeps its toon exponent finite.
- Frontface normal orientation is unchanged. Materials that enabled the omitted options can change in either shading model: flipped backfaces, custom cube tint / lighting, and nonidentity mask transforms. Only Toon uses the added third shadow and corrected direct-specular attenuation. Already converted materials retain the saved lilToon values; conversion still copies by name.

Historical pre-merge backface validation: **44/44 Node checks and 61/61 offline DX11 VS/PS pairs passed**, including both DepthNormals encodings. No Unity process was started.

The current [independent-review corrections](Tests~/MergeConsistencyReport.md#independent-review-corrections-2026-11-05) supersede earlier backface/probe/queue claims in the historical audits. The reported hood capture tint has not been isolated by a fresh render.

The [Silent Code Sleeve audit](Tests~/SleeveParityAudit.md) records source / material / importer evidence, expected numerical differences, offline checks and the remaining rendering uncertainties. These corrections have not been verified by a new Unity A/B capture.

## Normal Map 2nd, Anisotropy, Backlight and Glitter

All 55 properties in these four installed lilToon blocks retain their exact names, types, ranges and defaults. Conversion copies matching values and texture transforms by name. The inspector follows the installed advanced lilToon order: Shadow, Rim Shade, Emission 1st / 2nd, Normal Map 1st / 2nd, Anisotropy, Backlight, Reflection, MatCap, Rim Light, Glitter. Advanced exposes every option.

- Normal Map 2nd: `_UseBump2ndMap`, independent of the first map; raw UV0..UV3 selection followed by the map's scale / offset, signed scale and red-channel scale mask with its own transform. Decoded normal XY add and Z multiply, then the result is normalized, matching lilToon. Forward, SeeThroughHair and DepthNormals share the combined normal; main texture transforms never shift the second normal texture. Its scale mask follows transformed main UV0 and then its own scale / offset, matching lilToon. DepthNormals samples that mask with inline linear-repeat filtering, since opaque normal-only passes may not bind the main-texture sampler. Primary-only decoding retains FairyToon's existing URP calculation; enabling the second normal uses lilToon's scale-before-Z-reconstruction for both layers.
- Anisotropy: canonical `_AnisotropyTangentMap` (a tangent direction normal map), signed scale / red-channel mask, two tangent / bitangent widths, offsets, shift noise / red-channel noise mask and strengths. Texture transforms are preserved. Independent `_Anisotropy2Reflection`, `_Anisotropy2MatCap` and `_Anisotropy2MatCap2nd` routes work in both shading models. `_AnisotropyBumpMap` is absent from the installed lilToon source and local materials; no invented alias is imported. Degenerate direction projections fall back to the mesh tangent, missing mesh tangents use an orthogonal fallback, and zero-width lobes remain finite.
- Backlight: HDR color / color texture, main-color strength, normal strength, border / blur, directivity, view-direction strength, receive-shadow and backface mask. Receive-shadow uses scene attenuation only and excludes the separate character-shadow map: raw, as in lilToon, in toon mode; scaled by `_ShadowReceive` in Endfield. Endfield supplies its direct main-light color (the same luminance-normalized color as its direct lighting, times its day strength), without ambient or top fill. Toon retains its corrected lighting color. Backlight is separate from Endfield's built-in terminator lift.
- Glitter: UV0 / UV1 particle placement; HDR color texture with independent UV0..UV3 / transform; main-color and normal strengths; randomized size / angle; optional shape texture with its own transform and atlas dimensions (`_GlitterAtras`, preserving lilToon's spelling); both packed parameter vectors, sensitivity / post contrast, lighting / shadow / backface masks and transparency. `_GlitterParams1` packs tiling XY, squared particle size Z and contrast W; `_GlitterParams2` packs blink speed, angle limit, rim-light direction and color randomness. `_GlitterVRParallaxStrength` retains the blend from camera-forward to per-fragment view direction; this PC desktop path has no stereo eye offset and XR has not been tested. Transparent glitter uses one surface-alpha factor; zero-alpha surfaces suppress glitter. Unmasked inverse-alpha gain is capped at 4096 (alpha floor 1/4096), computed in float, and transparent RGB is clamped to native-half range before conversion, including HDR colors. Below that floor unmasked glitter fades safely instead of retaining exact premultiplied parity.

The four feature toggles compile local keywords `_FT_NORMAL2ND`, `_FT_ANISOTROPY`, `_FT_BACKLIGHT` and `_FT_GLITTER`. Normal Map 2nd and Glitter each use one mutually exclusive group: off, base keyword for UV0, or `_FT_NORMAL2ND_UV` / `_FT_GLITTER_UV` for extra UVs. Both stages see these groups in Forward / SeeThroughHair; only Normal Map 2nd also has DepthNormals variants. Anisotropy / Backlight are fragment-only in the color passes. No independent keyword dimension or Outline variants are added by the UV refinement. UV selectors are material uniforms, so a compile-time alternative is necessary to remove VS outputs on FXC / Vulkan; a runtime branch cannot do it. All constants remain unconditional in `UnityPerMaterial`, with seven material samplers (including Main2nd and Emission 1st texture samplers).


Local source-material audit (8 OffAir color variants): Shoes enables Normal Map 2nd with scale 0.05, UV0 and `Tex_186_NormalTwo.png` tiled 8 x 8, masked by `Tex_ACC_AlphaMask9.png`; ACC Leather enables Anisotropy with `Tex_ClothA_Normal 1.png`, scale mask `Tex_ACC_AlphaMask5.png`, scale 1, both lobes' tangent / bitangent widths 1, first-lobe strength 1, second-lobe strength 0, all offsets / noise scales 0, noise texture unassigned, and all three routes off. Mayo Dress / Shoes / Stocking / Underwear / UW_Pink / UW_Whtie enable Backlight: color (0.69803923, 0.7251794, 0.8509804, 1), except Shoes green = 0.75019544; border 0.35, blur 0.05, directivity 5, view / normal strength 1, receive-shadow / backface-mask 1, main strength 0, and no color texture. The imported normal maps, masks, signed values and tiling survive conversion; this audit reads source materials and does not claim a visual match.
Feature combinations before -> after this surface addition (only main layers, emission, the four new features, and MotionVectors' outline keyword; existing normal / shadow / reflection / rendering and URP keywords excluded):

| Pass | VS combinations | PS combinations |
|---|---|---|
| Forward / SeeThroughHair (each) | 4 -> 36 | 4 -> 144 |
| DepthNormals | 2 -> 6 | 2 -> 6 |
| ShadowCaster, CharacterShadow, DepthOnly, SeeThroughMask, SeeThroughFakeShadow, SeeThroughEarlyZ (each) | 2 -> 2 | 2 -> 2 |
| MotionVectors | 4 -> 4 | 4 -> 4 |
| Outline | 1 -> 1 | 1 -> 1 |

Some graphics APIs ignore shader stage suffixes, so stage compilation savings depend on the backend; mesh-interface gating remains explicit. The UV refinement changes the previous binary Normal Map 2nd / Glitter groups to three alternatives (Forward: 16 -> 36 VS and 64 -> 144 PS combinations; DepthNormals: 4 -> 6). Extra-UV variants retain a shared UV1..UV3 interface so selectors can change among nonzero channels without more variants. Reapply `FairyToonMaterialSetup.Apply` to existing converted materials; runtime changes crossing UV0 / nonzero must refresh these keywords too.

Additional interpolators beyond each pass's existing fields (UV pair = float4 TEXCOORD8 + float2 TEXCOORD9):

| Pass / condition | Extra UV mesh inputs | Added interpolators |
|---|---|---|
| Forward / SeeThroughHair: Normal Map 2nd / Glitter UV0 only, no main layers / emission | none | none |
| Forward / SeeThroughHair: either extra-UV alternative, or main layers / emission | UV1..UV3 | UV pair (2) |
| DepthNormals: UV0 normal, no clipped main layers | none | none |
| DepthNormals: extra-UV normal, no clipped main layers | UV1..UV3 | UV pair (2) |
| DepthNormals: clipped main layers | UV1..UV3 | UV pair + world position TEXCOORD11 (3) |
| ShadowCaster / CharacterShadow / DepthOnly / see-through auxiliary: clipped main layers | UV1..UV3 | UV pair + normal/handedness TEXCOORD10 + world position TEXCOORD11 (4) |
| MotionVectors: clipped main layers, outline off | UV1..UV3 | same four |
| Outline / motion hull, or other coverage without clipped main layers | none | none |

Transparent BackfaceColor, Rim Light, both MatCaps and both Reflection contributions match lilToon's premultiplied blending while keeping SrcAlpha / OneMinusSrcAlpha. Let C be straight destination, a surface alpha, P = a*C, S layer color and b its mask/strength (channel-wise):

| Blend mode | Delta used for premultiplied conversion |
|---|---|
| Normal | S - P |
| Add | S |
| Screen | max(S - P*S, 0) |
| Multiply | P*(S - 1) |

Apply Transparency on returns C + b*delta, so the framebuffer applies surface alpha once. Off returns C + b*delta/max(a,1/4096), with zero-alpha compensation suppressed. Float intermediates and a +/-65504 output clamp protect native half; exact unmasked parity is limited below the alpha floor or at HDR saturation. At a=1 every mode retains its original result; opaque / cutout variants call the original blend. Multiply already had the correct result and retains it. BackfaceColor uses Normal blending with Apply Transparency off, matching its position after lilToon premultiplication. Emission keeps its division-free helper. Sequential layers use the updated destination, including Reflection specular then environment.

Offline checks (Windows, Node.js installed): `node Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs` and `powershell -NoProfile -ExecutionPolicy Bypass -File Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1`. The compiler script uses Windows D3DCompiler and cached URP sources without opening Unity; it checks constant-buffer layout, sampler limits and mesh interfaces. Options: `-SurfaceFeatures All|None|Normal2nd|Anisotropy|Backlight|Glitter`, `-LayerFeatures Both|Main|Emission|None`, `-Coverage Opaque|Cutout|Transparent`, `-Normal2ndUV UV0|UV1To3`, `-GlitterUV UV0|UV1To3`, `-PassNames`, `-NoReflection`, `-NoNormalMap`, `-NoOutline` and `-OctNormals` (DepthNormals quad-octahedral encoding) (`-NoLayers` still disables both older layer keywords). Node checks include source/default parity, keyword pass / stage scope, imported options, normal composition, transparent emission / glitter alpha, all four surface blend modes with both Apply Transparency states, HDR half limits, Backlight shadows / color, anisotropic highlight ownership and material UV keyword selection. These checks do not replace Unity import / SRP Batcher / screenshot checks or a Unity 6000.6.0f1 Editor run; those have not been run for this change.

Previous surface-feature verification (before the Sleeve parity corrections): **25 Node checks and 94 offline DX11 VS/PS pairs passed**. Current Sleeve checks and their results are recorded in [SleeveParityAudit.md](Tests~/SleeveParityAudit.md). Constant-buffer layouts match wherever reflected; genuinely unused opaque buffers strip. Actual VS signatures enforce the interface table above, including zero UV0-only outputs, DepthNormals' UV-only context and its reused coverage normal. Maximum sampler use is 11 PS / 1 VS (DX11 limit 16). No Unity process was started.

| Tested configuration | VS/PS pairs |
|---|---:|
| All-feature Cutout: 11 Toon passes + Forward / SeeThroughHair for five Endfield parts | 21 |
| Same all-feature matrix, Transparent | 21 |
| Opaque Toon: no new features, main / emission layers, primary normal or outline | 11 |
| Opaque Toon: second-normal-only, no main / emission layers or primary normal; UV0 / extra UV separately | 11 + 11 |
| Opaque Toon: all new features, no main / emission layers or primary normal; normal UV0 / Glitter extra, then normal extra / Glitter UV0; Forward / DepthNormals / SeeThroughHair | 3 + 3 |
| Opaque Toon: Glitter-only, no main / emission layers or primary normal; UV0 / extra UV; same three passes | 3 + 3 |
| Opaque Endfield Cloth: anisotropy on, Reflection off, no main / emission layers or primary normal; Forward / SeeThroughHair | 2 |
| Transparent Endfield Cloth Forward: probe atlas + probe volumes, both extra-UV alternatives | 1 |
| Cutout Toon MotionVectors body: main layers on, outline off | 1 |
| Cutout Toon: emission only, no main layers, new features or primary normal; Forward / DepthNormals / SeeThroughHair | 3 |

The two second-normal-only configurations each use 2 DepthNormals textures / 1 sampler; UV0 adds no context, extra UV adds only the UV pair. Mixed UV configurations confirm Glitter does not inflate DepthNormals. Numerical checks execute the actual HLSL arithmetic against independent premultiplied oracles, including consecutive layers, both transparency states and finite HDR output. Static review found no blocking or important code issue.

Historical main-only DX11 resources reflected before the merge from the all-feature Cutout matrix (old main layers, both emissions, primary normal, Reflection, MatCap, Rim and URP lights enabled; texture counts include URP resources):

| Pass / model | PS textures | PS samplers | VS samplers |
|---|---:|---:|---:|
| Forward / SeeThroughHair: Toon | 42 | 10 | 0 |
| Forward / SeeThroughHair: Endfield Cloth | 44 | 10 | 0 |
| Forward / SeeThroughHair: Endfield Skin | 45 | 10 | 0 |
| Forward / SeeThroughHair: Endfield Face | 48 | 10 | 0 |
| Forward / SeeThroughHair: Endfield Hair | 46 | 10 | 0 |
| Forward / SeeThroughHair: Endfield Eye | 42 | 10 | 0 |
| Outline | 8 | 5 | 1 |
| ShadowCaster / CharacterShadow / DepthOnly | 6 | 1 | 0 |
| DepthNormals | 9 | 3 | 0 |
| MotionVectors (outline hull) | 2 | 1 | 1 |
| SeeThroughMask | 7 | 2 | 0 |
| SeeThroughFakeShadow / SeeThroughEarlyZ | 6 | 1 | 0 |
| Transparent Endfield Forward + probe atlas / volumes | 52 | 11 | 0 |

Outline and the MotionVectors hull use one vertex texture (the outline width mask); the remaining vertex programs use no textures. All tested stages stay below DX11's 16-sampler limit. That historical matrix used five material sampler declarations; current Mermaid support uses seven. The totals above include URP and character-shadow samplers. Unity import, visual comparisons, runtime SRP Batcher inspection, Vulkan / XR and a Unity 6000.6.0f1 Editor run remain untested. Current standalone Editor C# compilation and offline shader results are in [MergeConsistencyReport.md](Tests~/MergeConsistencyReport.md).

## Endfield shading model

Base Setting > Shading Model = Endfield (`_ShadingModel` 1, keyword `_FT_ENDFIELD`) switches a material to a stylised PBR model after the published breakdowns of Arknights: Endfield's character shading. It replaces the toon Shadow section; Main Color (including 2nd / 3rd), both Normal Maps, Anisotropy, Backlight, Glitter, Reflection, MatCap, Rim Light, Rim Shade, Emission (both layers), Outline and the rendering settings still apply. lilToon mode is unchanged.

- Inputs (FairyToon names): `_PBRMap` (R metallic, G reflectivity with F0 = 0.04 × G, B AO, A smoothness; without a map `_EFMetallic` / `_EFSmoothness` are the values), `_DiffuseRamp` (256×1: RGB shadow hue, A lit weight over half-Lambert; without one, the `_ShadowBorder` / `_ShadowBlur` step), `_SpecRamp` (F0 colour; U = highlight shape or Fresnel, V = roughness on non-metals).
- Diffuse: backlight lift around the terminator, lit / dark / deeper-dark tones, a camera "view light" that darkens edges and creases, chroma-weighted ramp hue with the luminance put back, grey direct light on the shadow side.
- Specular: GGX without 1/π with the half vector pulled towards the camera, F0 × specular ramp; environment reflection with a split-sum fit and energy compensation for rough metal.
- Rim: Fresnel rim plus a rim on the light's side. Depth Edge Rim (`_EFRimDepth`, 0..1, off by default) keeps both to the true outline: the shader steps outward along the normal's direction on screen (Depth Edge Width, pixels at 1080p, two taps for a soft inner edge) and reads the camera depth there; only where that point is farther away than the surface by about Depth Edge Gap (metres) is the pixel on the outline. Inner creases between hair strands, braid lobes or folds, where both sides sit at about the same depth, get no rim. It needs the camera depth texture before opaques: the FairyToon renderer feature's Depth Rim (on by default) requests it, which makes URP render a depth prepass if SSAO or another feature has not already; a camera without it keeps the N.V rim. With the feature's Character Mask (on by default) the gap is measured against `_FT_CharacterDepth`, a depth texture of the FairyToon characters alone drawn before opaques (each shader's `CharacterMask` pass, opaque queues, the feature's layer mask, the same alpha clip as DepthOnly, no outline hull). Everything that is not a character then counts as far away, so the rim keeps the whole silhouette, shoe soles on the floor and a shoulder at a wall included, and a prop in front of a character does not cut a rim into it. The rim reads it with a texel load, so it costs no sampler. Material setup turns the pass off where it turns DepthOnly off (transparent surfaces). Endfield's frame capture has a scene / character occlusion buffer of this kind before its outline draw. After the character-depth rim in Ronald Burns' recreation and the ZMD demo's depth-offset rim (ideas only).
- `FairyToonCharacter` on the avatar root sets the sun / shade state (`DayStrength`) and a top fill light with separate sun / shade strengths, as shader globals (one character at a time for now). The top light is `saturate(N.y + Top Offset) + Top Fill`: the constant fill (default 0.35, not occluded by AO) reaches every side, so in the shade, where the top light replaces the sun, faces and sideways parts stay readable instead of falling to the ambient light alone. It also holds the per-character lighting controls of the community Endfield recreation (all off by default):
  - Light Override (0..1) swaps the main light's direction for a light fixed to the camera (Override Yaw / Pitch: degrees to the camera's right and above the horizon), so a face keeps a chosen light whatever the sun does. The face SDF, ramps, rims, hair band and outline follow it; scene shadows still come from the sun.
  - Terminator Offset (−1..1) is added to N·L on every part shaded by its normal (negative: more shade). The face follows its SDF.
  - Ambient Blend mixes a character ambient over the scene's probe: Ambient Sky from above, Ambient Ground from below, by N.y.
  - Ambient cap (0.2.0, all off by default): Ambient Max Luminance scales the ambient down to that luminance, hue kept; Ambient Saturation scales its chroma around its luminance (1 unchanged, 0 grey); Light Ceiling puts a soft shoulder on the luminance of ambient + top + direct (identity up to half of it, exponential roll-off towards it). Endfield adds the ambient to a sun normalised to 1 without a clamp, so without a cap a strongly coloured probe (a saturated blue sky) lifts the whole character towards white. The cap applies wherever the shader reuses the ambient (specular light, hair band, face highlight, outline). Shader global `_FT_EFAmbientCap` = (max luminance, saturation, ceiling, set).
  - Exposure (0.2.0, default 1): scales the normalised main light, so every use of the direct light (diffuse, specular, hair band, light-side rim, backlight, outline) darkens together, while the sun / shade weights still come from `DayStrength` alone. Use it for night or dim interiors: `DayStrength` below 1 moves the character into the view-lit shade look instead, which flattens back-lit shots. Follow Light Intensity (0..1, default 0) blends the factor towards the main light's own luminance over Reference Luminance (capped at 1), so a dim moon gives a dim key light with no script. Shader global `_FT_EFExposure` = (exposure, follow, reference, set). Small lights and additional lights are unchanged; ambient and top fill keep their own strengths.
  - Small lights: `FairyToonCharacterLight` on any object near the character (usually a child of the avatar) registers with the `FairyToonCharacter` above it, which passes the first four to its Endfield materials. Roles: Fill (a soft wrapped light), Tint (colours the shade side), Highlight (a toon specular dot), Rim (lights the silhouette on its side), Fog (pulls the colour towards its colour); each fades out at its Range. They are not Unity lights: no light slot, light layer or shadow, and they light nothing else. The outline does not take them.
- The main light counts as a colour only (normalised to luminance 1, as in Endfield): its strength is `DayStrength` times Exposure, so the character keeps one exposure under any sun intensity unless Follow Light Intensity is raised. No light clamp; `_LightMaxLimit` does not apply in this mode. Additional lights keep their real intensity. On the Face part they shade by a smooth sphere around the head instead of the mesh normals (the SDF's mesh blend, B, keeps the mesh normals, e.g. for the neck), so a lamp gives one clean gradient across the face; on the mesh, a lamp above and behind lit the upward-tilted nose bridge as a bright patch on a dark face.
- Light colours are gamma (Unity's colour fields): a lamp at (1, 0.55, 0.25) is (1, 0.26, 0.05) in linear light, and the hue-kept tonemap keeps that ratio, so strongly tinted lamps read neon. Paler lamp colours give the firelight look of the in-game frames.
- Parts (`_EFPart`, keywords `_FT_EF_SKIN` / `_FT_EF_FACE` / `_FT_EF_HAIR` / `_FT_EF_EYE`): Cloth (default), Skin, Face, Hair and Eye. `FairyToonFaceShadow` on the avatar root feeds the head frame (face axes, head-bone position) to Face, Hair and Eye slots.
  - Skin: an edge colour towards grazing angles (`_EFSSSColor` multiplies the colour at the silhouette by `_EFSSSStrength`; the community recreation darkens skin there towards a warm deep tone near (0.40, 0.32, 0.29)), shadow colour from `_SkinLUT` (Unity's 2D LUT strip, 32 slices of 32 × 32, sRGB albedo in, sRGB shadow colour out; without a LUT the Shadow Tone), direct specular, no environment reflection.
  - Face: NoL from the face shadow map in lilToon's format (`_ShadowStrengthMask` R / G per light side, B blend back to the mesh NoL for the neck) projected into the head's horizontal plane, with Endfield's soft window and a backlight fix; `_EFFaceSDFScale` sets how fast the shadow follows the light (0.5 for Endfield-style maps, 1 for maps made for lilToon); head vectors from `FairyToonFaceShadow` (keep `_ShadowMaskType` 2). SSS only where the face turns away from the camera; Fresnel rim inside `_RimColorTex` R on the camera's right side; no GGX specular or environment reflection. Instead a painted highlight (`_EFFaceHighlight` R, for example a small triangle on the nose tip and a dot on the lip, as in Endfield's specular-only frames) adds `_EFFaceHighlightColor` with the specular light; its UV is pushed by the view in the head frame (`_EFFaceHighlightShift`), so it slides like a wet reflection. For face UVs laid out as seen from the front, a positive shift moves it towards the camera. `_EFEyeMask` R marks eyes painted on the face texture: they shade as the Eye part.
  - Hair: the diffuse of Cloth plus a Kajiya-Kay band computed on a sphere around the head (centre `_EFHairCentre` above the head bone), not on the mesh normals, so the band is one horizontal ring tied to the head whatever the UV layout. The camera-right part of the sphere normal is removed so the band runs straight across the screen; the half vector follows the camera (`_EFHairLightFollow` mixes in the light) and `_EFHairBandOffset` sets its height. Two lobes: a sharp primary with a flat top edge (`_EFHairTopCut`) coloured by `_SpecRamp` (U = the band's strength, 0 at its edge to 1 at its centre, so a ramp that steps up from black cuts a clean toon edge; V = side), and a broad secondary in the hair colour or its own colour (`_EFHairColor2`, A = 1 uses the colour; warm gold under blonde hair); the exponent widens with the pixel footprint so thin bands do not sparkle. `_HairMask`: R highlight filter, G strand noise (jitters the band), B AO. Optional `_SkinLUT` on hair: warm hair takes its shadow colour from the skin LUT (tan undersides instead of grey). A back halo when the light is behind. Optional Root Occlusion (`_EFHairRootAO`, 0 = off): the hair diffuse is multiplied by a warm `_EFHairRootColor` near a point `_EFHairRootLift` above the hair centre (the crown), fading between `_EFHairRootInner` and `_EFHairRootOuter` metres, so roots darken and tips stay bright without any mesh data; the band is left at full strength. Optional strand breakup: `_EFHairStrandBreakup` pushes the albedo's painted strands against a blurred copy of it (`_EFHairStrandBlur` mip levels up), and `_EFHairStrandLines` adds a fine stripe noise (`_EFHairStrandFreq` stripes per UV unit) along the strands for hair painted as soft locks; the strand direction is the head's down vector mapped into UV and snapped to U or V, so it suits atlases whose strips run along an axis (a lock that curls upward gets its stripes across it). Both are tied to the UVs and move with the hair. No GGX or environment reflection.
  - Eye: no scene shadow, SDF, SSS or rim; the colour only dims (`_EFEyeLight`) as the light moves behind the face. Use MatCap for the cornea highlight.
  - Eye overlay (Face and Eye parts, `_EFEyeOverlay`): R is a painted shadow under the upper lid that multiplies the eye white by `_EFEyeShadowColor` (Endfield draws it as an overlay-shadow sheet, 目影, so the eye sits in its socket); G marks the lower iris, multiplied by the HDR `_EFEyeScatterColor` as light scattered through the iris (the community recreation's `_EyeScatteringColor`, warm gold for amber eyes). Black overlay: no change.
  - **3D Iris** (`_EFIris`, works with both Toon / lilToon and Endfield shading models; optional on any Endfield part): an analytic paraboloid bowl sinks the iris behind the eye surface; a paraboloid cornea dome bends its lookup and refracts a matcap (RGB light, A blue screened glow). Colour layers, emission and the painted eye overlay follow the shifted UV0. By default **Pupil Layer** (`_EFPupilLayer = 1`) draws the separate pupil mesh with its own UV and moves its vertices at **Pupil Depth** (`_EFPupilDepth = 0.5`): 0 stays on the surface, 1 follows the sinking pattern. Size, star, heart and off shape keys work because skinning and blend shapes precede the vertex shift. Main iris and expression islands never receive a folded pupil in this mode. Layer 0 restores the previous default-pupil fold and pupil-island remap exactly. In both models, UV0 is remapped before the Main Color layers; after the layered base, the pupil fold and cornea matcap screen are composed into the base albedo before shading, MatCap, rim and other lighting effects. Toon mode exposes the same controls in **3D Iris** immediately after the Main Color layers. The iris is lit by lilToon shading in Toon mode instead of Endfield's eye dimming, so **Reflection / Glow Strength may need lower values**. In Endfield mode it continues to share the eye's dimming and day / character lighting; Reflection Strength (`_EFIrisSparkle`, 0-4) scales only RGB light, while Glow Strength (`_EFIrisGlowStrength`, 0-2, default 1) scales only alpha glow. Both at 0 leave the albedo unchanged; both at 1 match the node tree. The Endfield lit outline follows the same layer/fold selection without the matcap; Toon retains its outline colour controls and uses the shared iris UV for lighting masks.
    - `_EFIrisFrame` is baked **offline from the mesh UV islands**, in UV0 with V up. The bake script belongs to the project, not this package. Import as **linear floating-point data** (sRGB off; preserve signed RG/A and codes above 1; no lossy compression; no mip maps). The frame is always sampled at LOD 0 so mip filtering cannot blend island codes; frame and cornea matcap use bilinear clamp samplers independently of the main texture.
    - Frame **RG** = position p0 in iris x-radius units (Y uses the same units); **B** = UV per iris unit; **A** = 0 outside, **+k** main iris, **+(k+10)** expression iris (no pupil fold), **-k** pupil-mesh island. Disabled for |A| < 0.5; k = |A| > 5 ? |A| - 10 : |A| is the iris Y/X ratio. The radial lookup clamp is in ellipse units. Known limitation: distant-view pixel quads that straddle an island border more than 2 frame texels away can give iris rim pixels a coarse albedo mip.
    - `_EFIrisScale` (0.3–1.5, default 0.72) scales only the sinking art lookup before Pattern Edge, including the pupil-mesh UV and pupil fold only in legacy fold mode; bowl and dome geometry stay in disc units with rim = 1. Lower values enlarge the visible pupil. The Blender study has iris pattern extending well past the disc; Mayo instead has a painted dark ring and rim lights at its art edge.
    - `_EFIrisGaze` (default 1) blends the depth axis from the mesh normal (0) to head forward (1), preventing face-on art drift on curved or outward-tilted eye shells while keeping UV-aligned art axes; animated heads need `FairyToonFaceShadow` on the renderer, otherwise forward falls back to object +Z. The cornea matcap always uses the head frame (viewer right, head up), independently of this blend, so both eyes keep the Blender study's non-mirrored orientation regardless of UV handedness.
    - **Fixed Rim** (`_EFIrisRim`, 0–1.5, default **1.5 = off**, the whole iris sinks): at or beyond this display radius in ellipse units, main and expression irises keep their exact surface UV with no parallax or pupil fold. Legacy pupil-mesh lookups ignore it; the moving layer softly limits its centre to `min(Fixed Rim, 1) - pupil extent`; the cornea matcap still covers the limbus. Set it just inside the painted dark ring: the ring and rim lights belong to the eye surface like a real limbus, while only the pattern inside sinks. With Edge Fade 0, the hard boundary deliberately lets the ring occlude the pattern; its boundary pixel can take a coarser albedo mip at a distance, like the island border noted above.
    - **Pattern Edge** (`_EFIrisEdge`, 0.5–1, default 0.96) bounds the lookup inside the painted pattern; **Edge Softness** (`_EFIrisEdgeSoftness`, 0–1, default 0.4) squeezes the outer pattern smoothly toward that radius to avoid repeated-colour streaks. Softness 0 restores the hard edge-colour hold; below `(1 - softness) × Pattern Edge` the inner pattern is untouched, with slope 1 at the knee.
    - **Edge Fade** (`_EFIrisEdgeFade`, default **0 = off**) fades the full lookup offset, including face-on magnification, back to the identity lookup at `end = min(Fixed Rim, 1)`. Its width is `max(fade × end, 1.25 × ellipseShift)`, so larger shifts widen the transition to keep the painted rim still.
    - **Vertical Strength** (`_EFIrisVertical`, default **1**) scales the Y component of the displacement before the shift limit and edge fade. Lower values soften vertical iris travel while retaining horizontal parallax.
    - **Max Shift** (`_EFIrisMaxShift`, default **2 = off**) softly caps displacement length in ellipse radii before edge fading. Smaller values limit large view-angle shifts without a hard clip.
    - With Edge Fade enabled, for a source point `p` inside `end` with ellipse radius `rho`, the fade bounds `|d|_ellipse <= (end - rho) / 1.25`, so the shifted lookup cannot cross that boundary. For a **locally constant incoming displacement**, its radial projection derivative is at least `1 - 1/1.25 = 0.2`, so the fade itself does not fold that map. This is not a no-fold guarantee for the full spatially varying optics.
    - The inspector puts Depth, Pupil Layer, Pupil Depth, Refraction, Art Scale, Fixed Rim, Edge Fade, Vertical Strength, Max Shift, Pattern Edge, Edge Softness and Gaze Frame first, followed by Cornea and Cornea matcap. Every control has a tooltip; measured frame, island and pupil-fold values sit under **Avatar data**, collapsed by default.
    - The Mayo lab starts Fixed Rim at **0.82**, Pattern Edge at **0.76** (clear of a painted cyan dot near 0.80), Edge Softness at **0.4**, and Art Scale at **0.6** (face-on centre magnification 1.8 × 0.6 = 1.08, close to the stock pupil size), with Edge Fade **0.4**, Vertical Strength **0.6** and Max Shift **0.5**. In the offline Mayo art study these reduced folded/mirrored pixels by 60–75% at yaw ±30°, +50° and pitch +20°; the pupil stayed inside through 50° turns. Reflection Strength and Glow Strength start at **0.5 each** for a gentler look. Refreshing Endfield materials keeps every iris float, integer, vector, colour and texture with its scale/offset on an existing copy with `_EFIris = 1`; new or disabled copies get code defaults. Generated frame and matcap assets fill only missing texture references on enabled copies. `LiminalNW.Editor.LabPhase2E.ResetIrisBatch` resets only the iris values on `Assets/Lab/Materials/Endfield/MAYO_Face.mat`. Iris capture sweeps accept `-labIrisSet "depth,bulge,refraction,sparkle[,scale[,gaze[,glow[,rim[,edge[,softness[,fade[,vertical[,maxShift]]]]]]]]];..."`; omitted scale, gaze, glow, rim, edge, softness, fade, vertical and maxShift retain their tuned values.
    - Defaults describe Mayo. **Iris World Radius** (`_EFIrisWorldRadius = 0.01233` metres per iris X radius) and **Iris Dish** (`_EFIrisDish = 0.24` iris radii, quadratic rise toward the rim) are measured avatar data. Rescaled avatars need a new world radius. Dish compensation uses the camera ray at the laterally moved point, preserving perspective screen position; the layer matcap uses the moved surface coordinates so its highlight meets the iris. Iris centres/radius and all **pupil-fold values are per avatar**: centres, radius, Y/X ratio, scale and offset must match its islands. Layer mode also uses offset, scale and Y/X ratio to solve and limit pupil travel; pupil centres, texture radius and Pupil Fold strength are fold-only. The right eye mirrors the offset X. A pupil painted into the iris art separates from the mesh pupil at head turns; author the iris art without a duplicate pupil. Use a mesh with UV tangents; check mirrored UVs and backfaces when authoring a frame. Bulge is stored positive (the dome uses negative depth); IOR 8 is deliberately extreme, matching the Blender study. The glow colour default (0.3645, 0.8106, 1.2403) is the sRGB form of the study's linear (0.1093, 0.622, 1.606): Unity linearizes Color properties, so set it from script as `new Color(0.1093f, 0.622f, 1.606f).gamma`.
    - Forward, Outline (before extrusion), DepthOnly, DepthNormals, MotionVectors, CharacterMask, SeeThroughMask, SeeThroughFakeShadow, SeeThroughEarlyZ and SeeThroughHair share the same camera vertex shift. Fur shells and Gem's camera prepass inherit it too. **Depth priming / ZTest Equal requires the identical unextruded position in depth and colour**; both call the same function with skinned position, normal, tangent and UV0. Motion vectors add the same current displacement to current and previous world positions; ShadowCaster and CharacterShadow retain light-view positions. The iris keyword therefore reaches vertex as well as fragment stages in every affected pass.
    - Capture with `-labIris` adds face-on/yaw30 `iris_pupil_<key>_on` / `iris_pupil_<key>_yaw30_on` for Kira2_big, Star_big, Heart_big, OFF, Big2, Small2 and Up at 100, plus `iris_pupil_kira_fold` for the older fold. Keys are looked up by name and original weights/material values are restored. Optional `-labIrisToon` captures `iris_toon_off`, `iris_toon_on`, `iris_toon_yaw30_on`, `iris_toon_yaw-30_on` and `iris_toon_pupil_kira2_big_on` using an in-memory Toon face with the Endfield face's iris tuning. Combine it with `-labIris` to add those shots after the Endfield sweep. Renderer slots are restored and the temporary material is destroyed without asset writes. `LabPhase2E.ApplyIrisToToonFaceBatch()` copies iris tuning to the saved Toon face; an already enabled face keeps its own values and only receives missing textures, including through a Phase2 rebuild. Optional `-labPupilDepth "0.3;0.5;0.7"` adds zero-based `iris_pdepth<n>`, `iris_pdepth<n>_yaw30`, `iris_pdepth<n>_yaw-30` shots. The capture logs a BakeMesh/world-transform least-squares radius measurement and warns above 10% mismatch without writing it to the material.
    - Technique after a Bilibili Blender study by **大大大歪歪呀** ([BV17DbR6ZEmM](https://www.bilibili.com/video/BV17DbR6ZEmM/)); reimplemented, **no assets copied**. The port screens the iris-plus-matcap composite like the study. The original x3 and squared Lambert light mask are omitted; FairyToon's two strength sliders independently control the matcap channels.
    - Edge-fade and displacement-limit idea after the eye-parallax node setup by **kensyouen_Y**, rebuilt by **ERiC80822** ([BV1Yy886SERH](https://www.bilibili.com/video/BV1Yy886SERH/)); reimplemented, **no assets or nodes copied**.
- The see-through fake shadow (fringe shadow on the face) fades with `DayStrength`.
- Character self-shadow (see below) darkens both the sun term and the view light. The face's SDF area ignores it; the SDF's mesh blend (B: neck, ears) takes it, which gives the chin shadow on the neck. Eyes ignore it.
- Outline in this mode: a darker version of the local shadow colour (diffuse ramp hue, skin LUT on skin and face) times `_EFOutlineColor`, its chroma scaled by `_EFOutlineSaturation` around the luminance (Endfield's skin line is a saturated red-orange, not a dark grey), then lit mainly by the ambient and top light (after the boost, so a cool shade light greys the line as it greys the surface), plus an edge tint at grazing angles (`_EFOutlineEdge`, A = strength). Two more steps from the frame capture's final outline grade, both off by default and applied before the light: Flatten (`_EFOutlineFlatten`, a contrast squeeze towards mid grey; the capture's 0.85 is 0.15) and Tint Pull (`_EFOutlinePull`, a constant colour, A = amount). On Mayo, Flatten 0.15 greyed the measured red-orange skin line, so the lab leaves both off.
- Anisotropic highlight ownership: only the shared lilToon Reflection specular uses `_Anisotropy2Reflection`. With Reflection enabled, Apply Specular on and the anisotropy route active, Cloth / metal suppress their Endfield direct GGX highlight; Reflection supplies the sole anisotropic highlight with lilToon smoothness, reflectance and lobe controls. The route alone does not alter Endfield GGX. Reflection disabled, Apply Specular off or the route disabled retains Endfield's original isotropic highlight. This keeps converted booth materials on lilToon's single Reflection specular model. Endfield retains its environment BRDF, probe atlas selection and custom Skin / Face / Hair / Eye specular. Its custom cubemap uses the shared lilToon sampler/decoder.
- Texture tools for ramps and packed PBR maps: **Tools > FairyToon > Texture Tools**.

## Character self-shadow

A tight depth map per character, so the fringe, chin and hair shadow the character crisply at any camera distance.

- Put `FairyToonCharacter` on the avatar root (Cast Character Shadow on) and keep the **FairyToon** renderer feature; its Character Shadow settings hold the rest. Up to four characters share one 32-bit depth atlas (2048 / 4096 / 8192; 8192 is 256 MB).
- Light: **Camera Aligned** (default, as in Endfield): a projector above the camera, yaw from the character's centre to the camera, pitch clamped to 30-35°, so shadows fall down and away from the viewer whatever the sun does; turning the camera without moving it changes nothing. **Main Light**: the sun, as a high-resolution self-shadow.
- Camera Aligned samples each character’s own atlas tile. Main Light allows characters to cast shadows on each other.
- Camera Fit fits each map to the part of the character in view, with snapped sizes and texel-snapped positions against shimmer.
- Filtering: a 7×7 tent (16 bilinear compares, 64 depth tests), then a narrow smoothstep around the 0.5 contour, so the edge is a crisp toon line without stair steps even when a texel covers several pixels; Softness widens it back into a penumbra. The shadow fades in with `saturate(N·L × 4)` towards the projector, so the toon ramp alone shades the terminator.
- Per material: `_CharShadowReceive` (0..1, default 1). Materials without FairyToon's `CharacterShadow` pass cast through a hidden opaque caster; transparent materials do not cast.
- Debug View shows only the visibility, for checking acne and edges.

## Tonemap

A filmic tonemap for the whole image, run by the **FairyToon** renderer feature after URP's post (so after bloom, vignette, chromatic aberration and colour grading, the order public frame-capture research gives for Endfield).

- Add **Post-processing > FairyToon Tonemap** to a volume and set Mode to Filmic. Set URP's Tonemapping to None and the pipeline asset's Grading Mode to High Dynamic Range; otherwise URP has already clamped the image, and the pass skips with one warning.
- Curve: `x^a / (x^(a·d)·b + c)` with b and c solved so scene mid grey 0.18 lands on Mid Grey and White Point on 1. Contrast (a) shapes the toe, Shoulder (d) the roll-off.
- Hue Preservation 1 maps the brightest channel and keeps the channel ratios, so warm skin and tinted hair keep their hue and saturation; 0 maps each channel. Highlight Desaturation rolls saturated colours towards white near the top of the curve.
- Optional Sharpen after the curve (0 = off, the default): a light unsharp mask on the four neighbouring pixels, clamped to their range so an edge gets no halo.
- Optional look after that, looked up in sRGB and blended by Lut Contribution: a Look Preset (Warm, Cool, Soft, Vivid: mild photo-filter grades generated in code from white balance, lift, saturation and a gentle S-curve; None by default) or a look LUT (URP's strip layout, size² × size, import with sRGB off), which wins over the preset. Then a triangular dither (Dither, in 8-bit steps) into an 8-bit sRGB target.
- Skipped for overlay cameras in a stack, cameras without post-processing or HDR, and HDR displays.

## See-through hair

The fringe turns see-through over the eyes and brows, from the front only (after SeeThroughHair by goorm).

- Roles are per material (`_SeeThroughRole`): **Face** marks face pixels and, in an extra colourless pass, the painted eye / brow mask (UV0, one channel, no scale / offset); **Hair** is drawn normally outside the mask and again, alpha-blended, inside it. **None** leaves the stencil to the user; removing a role resets the stencil it set.
- Stencil bits: 4 = see-through region, 8 = face not covered by opaque hair (used by the fake shadow). Keep both for FairyToon.
- Order: the mask pass (LightMode `UniversalForwardOnly`) runs right after the face's own Forward pass, so it is depth-tested against the face only; Opaque / Cutout hair is queued at 2460 or later so every face is drawn before it.
- The **FairyToon** renderer feature draws the rest: the optional multiply fake shadow (hair pushed away from the main light, face bit only, once per pixel) after opaques; after transparents (so after eye highlights), every hair Early Z pass, then the lit, blended hair. Early Z keeps the blend to the front hair layer. View Angle Fade takes the opacity back to Side Alpha (1 = opaque) as the camera leaves the head's front; horizontal and vertical ranges are separate.

Set-up:

1. Add the **FairyToon** feature to the URP renderer (the material inspector and the component offer a button).
2. Add **FairyToonSeeThroughHair** to the avatar root; assign the renderer that holds the face (on Mayo it is a slot of the Body renderer) and the hair renderers. Its inspector lists their FairyToon slots with a role toggle each, and a Paint button per face slot. It feeds the head position and axes to the fade through per-slot property blocks (FairyToonFaceShadow's axes when present).
3. Paint the eye / brow mask: **Tools > FairyToon > Mask Painter**, or the Paint button. Defaults for the hair: Opacity 0.7, Early Z on, View Angle Fade on, Side Alpha 1.

The **Mask Painter** paints any channel of any 2D texture slot on a renderer's material, in the Scene view: the brush is a world-space sphere painted in UV space on the GPU, so it crosses UV seams. LMB paints towards Value, Shift+LMB towards 0, Ctrl+LMB fills the UV island under the cursor (the face-triangle quick start), Ctrl+Shift+LMB clears it; [ / ] radius; Ctrl+Z / Ctrl+Y undo / redo. The brush only hits the chosen slot, so it paints the face through the hair; an overlay shows the channel, and Isolate hides everything else. Save overwrites a PNG in place (GUID and import settings kept; other formats are saved as a new PNG) after padding the UV islands by 4 px; nothing is written before Save. It can also create a new mask texture (linear, black or white).

Limits: Forward / Forward+ only (in Deferred `UniversalForwardOnly` is the main pass). Faces must be Opaque / Cutout. Keep depth priming off; the depth / normals prepass still holds the hair over the eyes, so SSAO there comes from the hair. Without the renderer feature the fringe is cut over the mask but never drawn there. The property blocks take the hair renderers out of the SRP Batcher.

## Differences from lilToon

- Additional lights (Forward+ cluster loop, light layers, shadows) get the same toon step as the main light instead of a flat add.
- SSAO (URP renderer feature) is read with `_SSAOStrength` (shadow / ambient side, default 1), `_SSAOLitStrength` (lit side, default 0.2) and `_SSAOMask`.
- Optional shadow ramp (`_UseShadowRamp`, `_ShadowRampTex`, U = half-lambert) replaces the two shadow colours.
- The MotionVectors pass covers the outline hull, so outlines do not ghost under DLAA / TAA.
- Not ported (yet): AudioLink, distance fade, main 1st UV animation and decal atlas animation. Whole-material and Main Color 2nd / 3rd dissolve support mask, UV and object-position modes, with noise and edge color. Refraction, Fur and Gem use the separate shaders described below.

## lilToon converter

- **Assets > FairyToon > Convert lilToon Material (copy / in place)** on materials, folders, prefabs or models. Copies go to `<source folder>/FairyToon/<name>.mat` and are refreshed on re-run; in place records Undo. **Tools > FairyToon > Convert lilToon Materials…** adds an output folder, the options and the last report.
- By default, transparent variants with "hair" in the material name convert to Cutout with alpha to coverage and `_Cutoff` 0.5. Queues above 2500 move to 2450. Existing opaque-range ordering such as 2460 is preserved.
- **Convert Profile** (Create > FairyToon > Convert Profile): per-material overrides for what the lilToon material cannot carry, such as a face SDF painted later. Entries reference the source material; every profile in the project is applied, the window's profile last.
- Each batch writes `Logs/FairyToonConvert.md`: unsupported features in use, non-default values dropped (only for features that are on), applied heuristics and profile overrides.

Property mapping (from the shader's actual Properties block; identical names copy 1:1, including texture scale / offset when present):

| Inspector section | lilToon -> FairyToon | Handling |
|---|---|---|
| Base / Rendering | Variant -> `_TransparentMode` | Derived (see below) |
| Base / Rendering | `_Cull` -> `_Cull` | 1:1 |
| Base / Rendering | `_ZWrite` -> `_ZWrite` | Copied, then rendering setup derives / constrains it |
| Base / Rendering | `_ZTest` -> `_ZTest` | 1:1 |
| Base / Rendering | `_SrcBlend` -> `_SrcBlend` | Copied, then rendering setup derives / constrains it |
| Base / Rendering | `_DstBlend` -> `_DstBlend` | Copied, then rendering setup derives / constrains it |
| Base / Rendering | `_SrcBlendAlpha` -> `_SrcBlendAlpha` | Copied, then rendering setup derives / constrains it |
| Base / Rendering | `_DstBlendAlpha` -> `_DstBlendAlpha` | Copied, then rendering setup derives / constrains it |
| Base / Rendering | `_AlphaToMask` -> `_AlphaToMask` | Copied, then rendering setup derives / constrains it |
| Base / Rendering | `_OffsetFactor` -> `_OffsetFactor` | 1:1 |
| Base / Rendering | `_OffsetUnits` -> `_OffsetUnits` | 1:1 |
| Base / Rendering | `_AAStrength` -> `_AAStrength` | 1:1 (shared with lilToon); default 1 |
| Main Color | `_Color` -> `_Color` | 1:1 |
| Main Color | `_MainTex` -> `_MainTex` | 1:1 |
| Main Color 2nd | `_UseMain2ndTex` -> `_UseMain2ndTex` | 1:1 |
| Main Color 2nd | `_Color2nd` -> `_Color2nd` | 1:1 |
| Main Color 2nd | `_Main2ndTex` -> `_Main2ndTex` | 1:1 |
| Main Color 2nd | `_Main2ndTexAngle` -> `_Main2ndTexAngle` | 1:1 |
| Main Color 2nd | `_Main2ndTex_ScrollRotate` -> `_Main2ndTex_ScrollRotate` | 1:1 |
| Main Color 2nd | `_Main2ndTex_UVMode` -> `_Main2ndTex_UVMode` | 1:1 |
| Main Color 2nd | `_Main2ndTex_Cull` -> `_Main2ndTex_Cull` | 1:1 |
| Main Color 2nd | `_Main2ndTexIsDecal` -> `_Main2ndTexIsDecal` | 1:1 |
| Main Color 2nd | `_Main2ndTexIsLeftOnly` -> `_Main2ndTexIsLeftOnly` | 1:1 |
| Main Color 2nd | `_Main2ndTexIsRightOnly` -> `_Main2ndTexIsRightOnly` | 1:1 |
| Main Color 2nd | `_Main2ndTexShouldCopy` -> `_Main2ndTexShouldCopy` | 1:1 |
| Main Color 2nd | `_Main2ndTexShouldFlipMirror` -> `_Main2ndTexShouldFlipMirror` | 1:1 |
| Main Color 2nd | `_Main2ndTexShouldFlipCopy` -> `_Main2ndTexShouldFlipCopy` | 1:1 |
| Main Color 2nd | `_Main2ndTexDecalAnimation` -> `_Main2ndTexDecalAnimation` | Stored for Reset UV and conversion |
| Main Color 2nd | `_Main2ndTexDecalSubParam` -> `_Main2ndTexDecalSubParam` | Stored for Reset UV and conversion |
| Main Color 2nd | `_Main2ndTexIsMSDF` -> `_Main2ndTexIsMSDF` | 1:1 |
| Main Color 2nd | `_Main2ndBlendMask` -> `_Main2ndBlendMask` | 1:1 |
| Main Color 2nd | `_Main2ndTexBlendMode` -> `_Main2ndTexBlendMode` | 1:1 |
| Main Color 2nd | `_Main2ndTexAlphaMode` -> `_Main2ndTexAlphaMode` | 1:1 |
| Main Color 2nd | `_Main2ndEnableLighting` -> `_Main2ndEnableLighting` | 1:1 |
| Main Color 3rd | `_UseMain3rdTex` -> `_UseMain3rdTex` | 1:1 |
| Main Color 3rd | `_Color3rd` -> `_Color3rd` | 1:1 |
| Main Color 3rd | `_Main3rdTex` -> `_Main3rdTex` | 1:1 |
| Main Color 3rd | `_Main3rdTexAngle` -> `_Main3rdTexAngle` | 1:1 |
| Main Color 3rd | `_Main3rdTex_ScrollRotate` -> `_Main3rdTex_ScrollRotate` | 1:1 |
| Main Color 3rd | `_Main3rdTex_UVMode` -> `_Main3rdTex_UVMode` | 1:1 |
| Main Color 3rd | `_Main3rdTex_Cull` -> `_Main3rdTex_Cull` | 1:1 |
| Main Color 3rd | `_Main3rdTexIsDecal` -> `_Main3rdTexIsDecal` | 1:1 |
| Main Color 3rd | `_Main3rdTexIsLeftOnly` -> `_Main3rdTexIsLeftOnly` | 1:1 |
| Main Color 3rd | `_Main3rdTexIsRightOnly` -> `_Main3rdTexIsRightOnly` | 1:1 |
| Main Color 3rd | `_Main3rdTexShouldCopy` -> `_Main3rdTexShouldCopy` | 1:1 |
| Main Color 3rd | `_Main3rdTexShouldFlipMirror` -> `_Main3rdTexShouldFlipMirror` | 1:1 |
| Main Color 3rd | `_Main3rdTexShouldFlipCopy` -> `_Main3rdTexShouldFlipCopy` | 1:1 |
| Main Color 3rd | `_Main3rdTexDecalAnimation` -> `_Main3rdTexDecalAnimation` | Stored for Reset UV and conversion |
| Main Color 3rd | `_Main3rdTexDecalSubParam` -> `_Main3rdTexDecalSubParam` | Stored for Reset UV and conversion |
| Main Color 3rd | `_Main3rdTexIsMSDF` -> `_Main3rdTexIsMSDF` | 1:1 |
| Main Color 3rd | `_Main3rdBlendMask` -> `_Main3rdBlendMask` | 1:1 |
| Main Color 3rd | `_Main3rdTexBlendMode` -> `_Main3rdTexBlendMode` | 1:1 |
| Main Color 3rd | `_Main3rdTexAlphaMode` -> `_Main3rdTexAlphaMode` | 1:1 |
| Main Color 3rd | `_Main3rdEnableLighting` -> `_Main3rdEnableLighting` | 1:1 |
| Emission | `_EmissionMap_ScrollRotate` -> `_EmissionMap_ScrollRotate` | 1:1 |
| Emission | `_EmissionMap_UVMode` -> `_EmissionMap_UVMode` | 1:1 |
| Emission | `_EmissionBlendMask_ScrollRotate` -> `_EmissionBlendMask_ScrollRotate` | 1:1 |
| Emission | `_EmissionBlendMode` -> `_EmissionBlendMode` | 1:1 |
| Emission | `_EmissionBlink` -> `_EmissionBlink` | 1:1 |
| Emission | `_EmissionUseGrad` -> `_EmissionUseGrad` | 1:1 |
| Emission | `_EmissionGradTex` -> `_EmissionGradTex` | 1:1 |
| Emission | `_EmissionGradSpeed` -> `_EmissionGradSpeed` | 1:1 |
| Emission | `_EmissionFluorescence` -> `_EmissionFluorescence` | 1:1 |
| Emission 2nd | `_UseEmission2nd` -> `_UseEmission2nd` | 1:1 |
| Emission 2nd | `_Emission2ndColor` -> `_Emission2ndColor` | 1:1 |
| Emission 2nd | `_Emission2ndMap` -> `_Emission2ndMap` | 1:1 |
| Emission 2nd | `_Emission2ndMap_ScrollRotate` -> `_Emission2ndMap_ScrollRotate` | 1:1 |
| Emission 2nd | `_Emission2ndMap_UVMode` -> `_Emission2ndMap_UVMode` | 1:1 |
| Emission 2nd | `_Emission2ndBlend` -> `_Emission2ndBlend` | 1:1 |
| Emission 2nd | `_Emission2ndBlendMask` -> `_Emission2ndBlendMask` | 1:1 |
| Emission 2nd | `_Emission2ndBlendMask_ScrollRotate` -> `_Emission2ndBlendMask_ScrollRotate` | 1:1 |
| Emission 2nd | `_Emission2ndBlendMode` -> `_Emission2ndBlendMode` | 1:1 |
| Emission 2nd | `_Emission2ndMainStrength` -> `_Emission2ndMainStrength` | 1:1 |
| Emission 2nd | `_Emission2ndBlink` -> `_Emission2ndBlink` | 1:1 |
| Emission 2nd | `_Emission2ndUseGrad` -> `_Emission2ndUseGrad` | 1:1 |
| Emission 2nd | `_Emission2ndGradTex` -> `_Emission2ndGradTex` | 1:1 |
| Emission 2nd | `_Emission2ndGradSpeed` -> `_Emission2ndGradSpeed` | 1:1 |
| Emission 2nd | `_Emission2ndFluorescence` -> `_Emission2ndFluorescence` | 1:1 |
| Main Color | `_Cutoff` -> `_Cutoff` | 1:1; hair heuristic sets 0.5 |
| Main Color | `_AlphaMaskMode` -> `_AlphaMaskMode` | 1:1 |
| Main Color | `_AlphaMask` -> `_AlphaMask` | 1:1 |
| Main Color | `_AlphaMaskScale` -> `_AlphaMaskScale` | 1:1 |
| Main Color | `_AlphaMaskValue` -> `_AlphaMaskValue` | 1:1 |
| Rendering | `_FlipNormal` -> `_FlipNormal` | 1:1 |
| Rendering | `_BackfaceColor` -> `_BackfaceColor` | RGB override after emission; alpha is the blend weight, not opacity |
| Lighting | `_LightMinLimit` -> `_LightMinLimit` | 1:1 |
| Lighting | `_LightMaxLimit` -> `_LightMaxLimit` | 1:1 |
| Lighting | `_MonochromeLighting` -> `_MonochromeLighting` | 1:1 |
| Lighting | `_AsUnlit` -> `_AsUnlit` | 1:1 |
| Shadow | `_UseShadow` -> `_UseShadow` | 1:1 |
| Shadow | `_ShadowMaskType` -> `_ShadowMaskType` | 1:1 |
| Shadow | `_ShadowStrength` -> `_ShadowStrength` | 1:1 |
| Shadow | `_ShadowStrengthMask` -> `_ShadowStrengthMask` | 1:1 |
| Shadow | `_ShadowColor` -> `_ShadowColor` | 1:1 |
| Shadow | `_ShadowColorTex` -> `_ShadowColorTex` | 1:1 |
| Shadow | `_ShadowBorder` -> `_ShadowBorder` | 1:1 |
| Shadow | `_ShadowBlur` -> `_ShadowBlur` | 1:1 |
| Shadow | `_ShadowNormalStrength` -> `_ShadowNormalStrength` | 1:1 |
| Shadow | `_ShadowReceive` -> `_ShadowReceive` | 1:1 |
| Shadow | `_Shadow2ndColor` -> `_Shadow2ndColor` | 1:1 |
| Shadow | `_Shadow2ndColorTex` -> `_Shadow2ndColorTex` | 1:1 |
| Shadow | `_Shadow2ndBorder` -> `_Shadow2ndBorder` | 1:1 |
| Shadow | `_Shadow2ndBlur` -> `_Shadow2ndBlur` | 1:1 |
| Shadow | `_Shadow2ndNormalStrength` -> `_Shadow2ndNormalStrength` | 1:1 |
| Shadow | `_Shadow2ndReceive` -> `_Shadow2ndReceive` | 1:1 |
| Shadow | `_Shadow3rdColor` -> `_Shadow3rdColor` | 1:1 |
| Shadow | `_Shadow3rdColorTex` -> `_Shadow3rdColorTex` | 1:1 |
| Shadow | `_Shadow3rdBorder` -> `_Shadow3rdBorder` | 1:1 |
| Shadow | `_Shadow3rdBlur` -> `_Shadow3rdBlur` | 1:1 |
| Shadow | `_Shadow3rdNormalStrength` -> `_Shadow3rdNormalStrength` | 1:1 |
| Shadow | `_Shadow3rdReceive` -> `_Shadow3rdReceive` | 1:1 |
| Shadow | `_ShadowMainStrength` -> `_ShadowMainStrength` | 1:1 |
| Shadow | `_ShadowEnvStrength` -> `_ShadowEnvStrength` | 1:1 |
| Shadow | `_ShadowBorderColor` -> `_ShadowBorderColor` | 1:1 |
| Shadow | `_ShadowBorderRange` -> `_ShadowBorderRange` | 1:1 |
| Shadow | `_ShadowBorderMask` -> `_ShadowBorderMask` | 1:1 |
| Shadow | `_ShadowAOShift` -> `_ShadowAOShift` | 1:1 |
| Shadow | `_ShadowAOShift2` -> `_ShadowAOShift2` | 1:1 |
| Shadow | `_ShadowPostAO` -> `_ShadowPostAO` | 1:1 |
| Shadow | `_ShadowBlurMask` -> `_ShadowBlurMask` | 1:1 |
| Shadow | `_BackfaceForceShadow` -> `_BackfaceForceShadow` | 1:1 |
| Shadow | `_ShadowFlatBorder` -> `_ShadowFlatBorder` | 1:1 |
| Shadow | `_ShadowFlatBlur` -> `_ShadowFlatBlur` | 1:1 |
| Shadow | No source -> `_UseShadowRamp` | FairyToon default 0 |
| Shadow | No source -> `_ShadowRampTex` | FairyToon default white |
| Shadow | No source -> `_FaceForward` | Default (0, 0, 0, 0); face component writes it |
| Shadow | No source -> `_FaceRight` | Default (0, 0, 0, 0); face component writes it |
| Shadow | No source -> `_CharShadowReceive` | FairyToon default 1 |
| Shadow | No source -> `_SSAOStrength` | FairyToon default 1 |
| Shadow | No source -> `_SSAOLitStrength` | FairyToon default 0.2 |
| Shadow | No source -> `_SSAOMask` | FairyToon default white |
| Normal Map | `_UseBumpMap` -> `_UseBumpMap` | 1:1 |
| Normal Map | `_BumpMap` -> `_BumpMap` | 1:1 |
| Normal Map | `_BumpScale` -> `_BumpScale` | 1:1 |
| Normal Map 2nd | `_UseBump2ndMap` -> `_UseBump2ndMap` | 1:1 |
| Normal Map 2nd | `_Bump2ndMap` -> `_Bump2ndMap` | 1:1 |
| Normal Map 2nd | `_Bump2ndMap_UVMode` -> `_Bump2ndMap_UVMode` | 1:1 |
| Normal Map 2nd | `_Bump2ndScale` -> `_Bump2ndScale` | 1:1 |
| Normal Map 2nd | `_Bump2ndScaleMask` -> `_Bump2ndScaleMask` | 1:1 |
| Anisotropy | `_UseAnisotropy` -> `_UseAnisotropy` | 1:1 |
| Anisotropy | `_AnisotropyTangentMap` -> `_AnisotropyTangentMap` | 1:1 |
| Anisotropy | `_AnisotropyScale` -> `_AnisotropyScale` | 1:1 |
| Anisotropy | `_AnisotropyScaleMask` -> `_AnisotropyScaleMask` | 1:1 |
| Anisotropy | `_AnisotropyTangentWidth` -> `_AnisotropyTangentWidth` | 1:1 |
| Anisotropy | `_AnisotropyBitangentWidth` -> `_AnisotropyBitangentWidth` | 1:1 |
| Anisotropy | `_AnisotropyShift` -> `_AnisotropyShift` | 1:1 |
| Anisotropy | `_AnisotropyShiftNoiseScale` -> `_AnisotropyShiftNoiseScale` | 1:1 |
| Anisotropy | `_AnisotropySpecularStrength` -> `_AnisotropySpecularStrength` | 1:1 |
| Anisotropy | `_Anisotropy2ndTangentWidth` -> `_Anisotropy2ndTangentWidth` | 1:1 |
| Anisotropy | `_Anisotropy2ndBitangentWidth` -> `_Anisotropy2ndBitangentWidth` | 1:1 |
| Anisotropy | `_Anisotropy2ndShift` -> `_Anisotropy2ndShift` | 1:1 |
| Anisotropy | `_Anisotropy2ndShiftNoiseScale` -> `_Anisotropy2ndShiftNoiseScale` | 1:1 |
| Anisotropy | `_Anisotropy2ndSpecularStrength` -> `_Anisotropy2ndSpecularStrength` | 1:1 |
| Anisotropy | `_AnisotropyShiftNoiseMask` -> `_AnisotropyShiftNoiseMask` | 1:1 |
| Anisotropy | `_Anisotropy2Reflection` -> `_Anisotropy2Reflection` | 1:1 |
| Anisotropy | `_Anisotropy2MatCap` -> `_Anisotropy2MatCap` | 1:1 |
| Anisotropy | `_Anisotropy2MatCap2nd` -> `_Anisotropy2MatCap2nd` | 1:1 |
| Backlight | `_UseBacklight` -> `_UseBacklight` | 1:1 |
| Backlight | `_BacklightColor` -> `_BacklightColor` | 1:1 |
| Backlight | `_BacklightColorTex` -> `_BacklightColorTex` | 1:1 |
| Backlight | `_BacklightMainStrength` -> `_BacklightMainStrength` | 1:1 |
| Backlight | `_BacklightNormalStrength` -> `_BacklightNormalStrength` | 1:1 |
| Backlight | `_BacklightBorder` -> `_BacklightBorder` | 1:1 |
| Backlight | `_BacklightBlur` -> `_BacklightBlur` | 1:1 |
| Backlight | `_BacklightDirectivity` -> `_BacklightDirectivity` | 1:1 |
| Backlight | `_BacklightViewStrength` -> `_BacklightViewStrength` | 1:1 |
| Backlight | `_BacklightReceiveShadow` -> `_BacklightReceiveShadow` | 1:1 |
| Backlight | `_BacklightBackfaceMask` -> `_BacklightBackfaceMask` | 1:1 |
| Glitter | `_UseGlitter` -> `_UseGlitter` | 1:1 |
| Glitter | `_GlitterUVMode` -> `_GlitterUVMode` | 1:1 |
| Glitter | `_GlitterColor` -> `_GlitterColor` | 1:1 |
| Glitter | `_GlitterColorTex` -> `_GlitterColorTex` | 1:1 |
| Glitter | `_GlitterColorTex_UVMode` -> `_GlitterColorTex_UVMode` | 1:1 |
| Glitter | `_GlitterMainStrength` -> `_GlitterMainStrength` | 1:1 |
| Glitter | `_GlitterNormalStrength` -> `_GlitterNormalStrength` | 1:1 |
| Glitter | `_GlitterScaleRandomize` -> `_GlitterScaleRandomize` | 1:1 |
| Glitter | `_GlitterApplyShape` -> `_GlitterApplyShape` | 1:1 |
| Glitter | `_GlitterShapeTex` -> `_GlitterShapeTex` | 1:1 |
| Glitter | `_GlitterAtras` -> `_GlitterAtras` | 1:1 |
| Glitter | `_GlitterAngleRandomize` -> `_GlitterAngleRandomize` | 1:1 |
| Glitter | `_GlitterParams1` -> `_GlitterParams1` | 1:1 |
| Glitter | `_GlitterParams2` -> `_GlitterParams2` | 1:1 |
| Glitter | `_GlitterPostContrast` -> `_GlitterPostContrast` | 1:1 |
| Glitter | `_GlitterSensitivity` -> `_GlitterSensitivity` | 1:1 |
| Glitter | `_GlitterEnableLighting` -> `_GlitterEnableLighting` | 1:1 |
| Glitter | `_GlitterShadowMask` -> `_GlitterShadowMask` | 1:1 |
| Glitter | `_GlitterBackfaceMask` -> `_GlitterBackfaceMask` | 1:1 |
| Glitter | `_GlitterApplyTransparency` -> `_GlitterApplyTransparency` | 1:1 |
| Glitter | `_GlitterVRParallaxStrength` -> `_GlitterVRParallaxStrength` | 1:1 |
| Reflection | `_UseReflection` -> `_UseReflection` | 1:1; Gem has intrinsic optical reflection |
| Reflection | `_Smoothness` -> `_Smoothness` | 1:1 |
| Reflection | `_SmoothnessTex` -> `_SmoothnessTex` | 1:1 |
| Reflection | `_Metallic` -> `_Metallic` | 1:1 saved value; `[Gamma]` upload matches lilToon |
| Reflection | `_MetallicGlossMap` -> `_MetallicGlossMap` | 1:1 |
| Reflection | `_Reflectance` -> `_Reflectance` | 1:1 saved value; `[Gamma]` upload matches lilToon |
| Reflection | `_GSAAStrength` -> `_GSAAStrength` | 1:1 |
| Reflection | `_ApplySpecular` -> `_ApplySpecular` | 1:1 |
| Reflection | `_SpecularToon` -> `_SpecularToon` | 1:1 |
| Reflection | `_SpecularNormalStrength` -> `_SpecularNormalStrength` | 1:1 |
| Reflection | `_SpecularBorder` -> `_SpecularBorder` | 1:1 |
| Reflection | `_SpecularBlur` -> `_SpecularBlur` | 1:1 |
| Reflection | `_ApplyReflection` -> `_ApplyReflection` | 1:1; Gem has intrinsic optical reflection |
| Reflection | `_ReflectionNormalStrength` -> `_ReflectionNormalStrength` | 1:1 |
| Reflection | `_ReflectionColor` -> `_ReflectionColor` | 1:1 |
| Reflection | `_ReflectionColorTex` -> `_ReflectionColorTex` | 1:1 |
| Reflection | `_ReflectionApplyTransparency` -> `_ReflectionApplyTransparency` | 1:1 |
| Reflection | `_ReflectionCubeTex` -> `_ReflectionCubeTex` | 1:1 |
| Reflection | `_ReflectionCubeColor` -> `_ReflectionCubeColor` | 1:1 |
| Reflection | `_ReflectionCubeEnableLighting` -> `_ReflectionCubeEnableLighting` | 1:1 |
| Reflection | `_ReflectionCubeOverride` -> `_ReflectionCubeOverride` | 1:1 |
| Reflection | `_ReflectionBlendMode` -> `_ReflectionBlendMode` | 1:1 |
| MatCap / MatCap 2nd | `_UseMatCap` -> `_UseMatCap` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapColor` -> `_MatCapColor` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapTex` -> `_MatCapTex` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapBlend` -> `_MatCapBlend` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapBlendMask` -> `_MatCapBlendMask` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapEnableLighting` -> `_MatCapEnableLighting` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapShadowMask` -> `_MatCapShadowMask` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapBackfaceMask` -> `_MatCapBackfaceMask` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapLod` -> `_MatCapLod` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapBlendMode` -> `_MatCapBlendMode` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapApplyTransparency` -> `_MatCapApplyTransparency` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapNormalStrength` -> `_MatCapNormalStrength` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapZRotCancel` -> `_MatCapZRotCancel` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapPerspective` -> `_MatCapPerspective` | 1:1 |
| MatCap / MatCap 2nd | `_MatCapMainStrength` -> `_MatCapMainStrength` | 1:1 |
| MatCap / MatCap 2nd | `_UseMatCap2nd` -> `_UseMatCap2nd` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndColor` -> `_MatCap2ndColor` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndTex` -> `_MatCap2ndTex` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndBlend` -> `_MatCap2ndBlend` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndBlendMask` -> `_MatCap2ndBlendMask` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndEnableLighting` -> `_MatCap2ndEnableLighting` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndShadowMask` -> `_MatCap2ndShadowMask` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndBackfaceMask` -> `_MatCap2ndBackfaceMask` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndLod` -> `_MatCap2ndLod` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndBlendMode` -> `_MatCap2ndBlendMode` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndApplyTransparency` -> `_MatCap2ndApplyTransparency` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndNormalStrength` -> `_MatCap2ndNormalStrength` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndZRotCancel` -> `_MatCap2ndZRotCancel` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndPerspective` -> `_MatCap2ndPerspective` | 1:1 |
| MatCap / MatCap 2nd | `_MatCap2ndMainStrength` -> `_MatCap2ndMainStrength` | 1:1 |
| Rim Shade | `_UseRimShade` -> `_UseRimShade` | 1:1 |
| Rim Shade | `_RimShadeColor` -> `_RimShadeColor` | 1:1 |
| Rim Shade | `_RimShadeMask` -> `_RimShadeMask` | 1:1 |
| Rim Shade | `_RimShadeNormalStrength` -> `_RimShadeNormalStrength` | 1:1 |
| Rim Shade | `_RimShadeBorder` -> `_RimShadeBorder` | 1:1 |
| Rim Shade | `_RimShadeBlur` -> `_RimShadeBlur` | 1:1 |
| Rim Shade | `_RimShadeFresnelPower` -> `_RimShadeFresnelPower` | 1:1 |
| Rim Light | `_UseRim` -> `_UseRim` | 1:1 |
| Rim Light | `_RimColor` -> `_RimColor` | 1:1 |
| Rim Light | `_RimColorTex` -> `_RimColorTex` | 1:1 |
| Rim Light | `_RimMainStrength` -> `_RimMainStrength` | 1:1 |
| Rim Light | `_RimNormalStrength` -> `_RimNormalStrength` | 1:1 |
| Rim Light | `_RimBorder` -> `_RimBorder` | 1:1 |
| Rim Light | `_RimBlur` -> `_RimBlur` | 1:1 |
| Rim Light | `_RimFresnelPower` -> `_RimFresnelPower` | 1:1 |
| Rim Light | `_RimEnableLighting` -> `_RimEnableLighting` | 1:1 |
| Rim Light | `_RimShadowMask` -> `_RimShadowMask` | 1:1 |
| Rim Light | `_RimBackfaceMask` -> `_RimBackfaceMask` | 1:1 |
| Rim Light | `_RimApplyTransparency` -> `_RimApplyTransparency` | 1:1 |
| Rim Light | `_RimDirStrength` -> `_RimDirStrength` | 1:1 |
| Rim Light | `_RimDirRange` -> `_RimDirRange` | 1:1 |
| Rim Light | `_RimIndirRange` -> `_RimIndirRange` | 1:1 |
| Rim Light | `_RimIndirColor` -> `_RimIndirColor` | 1:1 |
| Rim Light | `_RimIndirBorder` -> `_RimIndirBorder` | 1:1 |
| Rim Light | `_RimIndirBlur` -> `_RimIndirBlur` | 1:1 |
| Rim Light | `_RimBlendMode` -> `_RimBlendMode` | 1:1 |
| Emission | `_UseEmission` -> `_UseEmission` | 1:1 |
| Emission | `_EmissionColor` -> `_EmissionColor` | 1:1 |
| Emission | `_EmissionMap` -> `_EmissionMap` | 1:1 |
| Emission | `_EmissionBlend` -> `_EmissionBlend` | 1:1 |
| Emission | `_EmissionBlendMask` -> `_EmissionBlendMask` | 1:1 |
| Emission | `_EmissionMainStrength` -> `_EmissionMainStrength` | 1:1 |
| Outline | Variant -> `_UseOutline` | Derived (see below) |
| Outline | `_OutlineColor` -> `_OutlineColor` | 1:1 |
| Outline | `_OutlineTex` -> `_OutlineTex` | 1:1 |
| Outline | `_OutlineWidth` -> `_OutlineWidth` | 1:1 |
| Outline | `_OutlineWidthMask` -> `_OutlineWidthMask` | 1:1 |
| Outline | `_OutlineFixWidth` -> `_OutlineFixWidth` | 1:1 |
| Outline | `_OutlineVertexR2Width` -> `_OutlineVertexR2Width` | 1:1 |
| Outline | `_OutlineZBias` -> `_OutlineZBias` | 1:1 |
| Outline | `_OutlineEnableLighting` -> `_OutlineEnableLighting` | 1:1 |
| Outline | `_OutlineCull` -> `_OutlineCull` | 1:1 |
| Outline | No source -> `_OutlineMode`, `_OutlinePixelWidth`, `_OutlineMinPixels`, `_OutlineFadeStart`, `_OutlineFadeEnd`, `_OutlineCornerTaper` | FairyToon defaults (Object mode, so lilToon widths are unchanged) |
| Stencil | `_StencilRef` -> `_StencilRef` | 1:1 |
| Stencil | `_StencilReadMask` -> `_StencilReadMask` | 1:1 |
| Stencil | `_StencilWriteMask` -> `_StencilWriteMask` | 1:1 |
| Stencil | `_StencilComp` -> `_StencilComp` | 1:1 |
| Stencil | `_StencilPass` -> `_StencilPass` | 1:1 |
| Stencil | `_StencilFail` -> `_StencilFail` | 1:1 |
| Stencil | `_StencilZFail` -> `_StencilZFail` | 1:1 |

Properties absent from a source variant use FairyToon's shader defaults; profiles can override them. FairyToon-only defaults above assume no previous serialized values for those names (the same property-copy behavior as phase 2).

| Derived state | Translation |
|---|---|
| Variant -> shader / `_TransparentMode` | Opaque / Cutout / Transparent use FairyToon with coverage 0 / 1 / 2. Refraction and Gem select separate shaders with coverage 2. Fur selects its separate shader; Transparent / 2 Pass body coverage 2, Cutout body coverage 1. Name checks are case-insensitive. |
| Variant -> `_UseOutline` | Outline name or enabled Multi outline toggle = 1. Fur keeps the value dormant and disables its outline keyword. |
| Multi mode | 0 opaque, 1 cutout, 2 transparent, 3 Refraction, 4 Fur Transparent, 5 Fur Cutout, 6 Gem. Named special shaders win over stale default modes 0–2. Known lilToon Multi alpha keywords are a fallback. No installed Multi Blur or Multi Fur Two Pass exists. |
| Gem | Separate scene-colour dispersion, cubemap / probe reflection and particle path; copied reflection switches are retained rather than forced on. |
| Keywords | Clear source keywords; rebuild from FairyToon properties through `FairyToonMaterialSetup.Apply`. |
| Blend / depth | Rendering setup derives `_SrcBlend`, `_DstBlend`, `_SrcBlendAlpha`, `_DstBlendAlpha`; Opaque / Cutout force `_ZWrite = 1`; Transparent preserves copied `_ZWrite`. `_AlphaToMask` is preserved only for Cutout. |
| Queue / tags | Conversion copies the effective lilToon queue. Converting transparent hair to Cutout moves queues above 2500 to 2450. An authored 2460 is preserved. A private `FairyToonManagedHairQueue` tag tracks automatic Hair ordering. Conversion clears that ownership. Explicit Rendering Mode / Fur Mode changes migrate canonical defaults. RenderType follows coverage. |
| Passes | Opaque / Cutout and all Fur modes enable body DepthOnly / DepthNormals / MotionVectors; ordinary Transparent and optical variants disable them. Fur keeps body shadow passes; optical variants disable shadows. Fur body and Gem PRE own SRPDefaultUnlit; other modes derive outline enablement. |

Not converted:

| lilToon feature | Result |
|---|---|
| Main HSVG / gradation, layer distance fade / AudioLink, decal atlas animation | Supported main layers and decals copy by name. Decal atlas vectors are retained on the material, and active non-default playback parameters are reported as unsupported. |
| Emission parallax / AudioLink and gradient-authoring metadata | Both emissions, UV animation, blink and baked gradient textures copy; unsupported sub-options remain reported. |
| Whole-material / layer dissolve | Modes 1/2/3 copy by name: mask, UV point/line, object-space point/line, scrolling noise and gamma-HDR edge colour. Root coverage remains inactive on Opaque/Gem/Fur shells; layer dissolve supports Opaque, but skips Gem/Fur shells. |
| Distance fade, AudioLink, parallax / POM, main 1st UV animation | Dropped; distance-fade strength is additionally checked. |
| Tessellation, Overlay, FakeShadow | Special geometry / rendering dropped; best-effort mode from variant name. |
| Lite / Multi | Shared inputs copied into full FairyToon; specialized layout / shading reported. |
| Two-pass transparent | Two-pass back faces dropped. |
| Extra outline texture adjustment, custom MatCap normals / UVs, independent outline rendering / stencil | Unsupported non-default inputs reported; shared outline lighting, color, width and base stencil remain. |
| Forward-add, encryption / bookkeeping | Explicitly ignored by the dropped-value report. Fur state names copy to the Fur shader; mode-derived blend/depth states are rebuilt. |

## Separate heavy rendering shaders

Rendering Mode offers **Opaque, Cutout, Transparent, Refraction, Fur, Gem**. The first three use `FairyToon`; the others swap to separate shader assets. Named values, texture references and texture scale/offset survive swaps and a return to the main shader. Optical surfaces and the Fur body retain shared toon / `_FT_ENDFIELD` lighting, normals, layers and emissions; Fur shells use the smaller source-style lighting path below. All special shaders expose the parity-fixed shared properties, including third shadows, backface controls and Gamma reflection scalars. The shared input's default-empty `FT_VARIANT_PROPERTIES` adds constants only in the special shaders, identically in every pass of each shader.

| Installed lilToon file / shader name | FairyToon destination |
|---|---|
| `lts_ref` / `Hidden/lilToonRefraction` | `FairyToon/Refraction`, Blur off |
| `lts_ref_blur` / `Hidden/lilToonRefractionBlur` | `FairyToon/Refraction`, Blur on |
| `lts_fur` / `Hidden/lilToonFur` | `FairyToon/Fur`, Transparent |
| `lts_fur_cutout` / `Hidden/lilToonFurCutout` | `FairyToon/Fur`, Cutout |
| `lts_fur_two` / `Hidden/lilToonFurTwoPass` | `FairyToon/Fur`, 2 Pass |
| `lts_gem` / `Hidden/lilToonGem` | `FairyToon/Gem` |
| `Hidden/lilToonMultiRefraction`, `Hidden/lilToonMultiFur`, `Hidden/lilToonMultiGem` | Corresponding special shader; Multi Fur mode 5 selects Cutout |
| `_lil/lilToonMulti`, `Hidden/lilToonMultiOutline` | Stored Multi mode 0–6 as above; outline values retained |
| Optional `lilToonFurOnlyTransparent`, `lilToonFurOnlyCutout`, `lilToonFurOnlyTwoPass` | Corresponding Fur mode; reports that the base body is included |

The installed lilToon has no dedicated Refraction/Fur/Gem outline shaders. The converter uses actual shader names, not filenames, and copies supported values before deriving render state. Its loss audit targets the chosen special shader, so supported special properties are no longer reported dropped.

### Refraction and Gem

**Enable Opaque Texture in the URP asset.** The PC asset already enables it with 2x downsampling. `DeclareOpaqueTexture.hlsl` / `SampleSceneColor` sees opaque objects only; it cannot refract other transparent objects as a grab pass can. Refraction defaults to Transparent-100 (2900), ZWrite On, matching `lts_ref`; authored/converted `_ZWrite` remains editable and is preserved. Gem defaults to Transparent-100 (2900), ZWrite Off, Cull Off, matching `lts_gem`; conversion preserves authored culling/depth. Scene/character shadow, depth, normals and motion-vector auxiliary draws remain disabled for both optical variants. Refraction has an optional outline; Gem reserves that draw tag for PRE and retains dormant outline values. Special shaders do not render FairyToon's custom See-through Hair passes; saved role/mask settings are retained when switching back.

Refraction uses view-space mapped-normal offset and Fresnel strength, with `_RefractionStrength`, `_RefractionFresnelPower`, `_RefractionColorFromMain`, `_RefractionColor`. `_RefractionBlur` enables fragment-only `_FT_REFRACTION_BLUR`. Before GSAA, optical perceptual roughness is `1 - _Smoothness * _SmoothnessTex.r`, multiplied once by `saturate(1.2 - abs(anisotropy))` when anisotropy routes to reflection, even when the ordinary Reflection layer is off. The Blur2 source LOD relation is `min(sqrt(roughness / sqrt(viewDepth) * 5), 10)`, evaluated once before reflection. URP's opaque copy has no mip chain: a nine-tap tent samples a radius of `exp2(LOD)-1` opaque-texture texels. This is an approximation of lilToon's mip / Gaussian filtering, not a full-screen separable blur; near depth is guarded against division by zero. Internal diffuse premultiply then scene lerp matches the installed lilToon Refraction's alpha-squared diffuse term; emission/reflection occur afterwards and are not attenuated by a second output blend.

The common `_SmoothnessTex_ST` is present in the shared material buffer. Refraction blur and ordinary Reflection apply it after the main UV transform, preserving imported transforms; its scale / offset controls are exposed in the inspector. Gem keeps lilToon's intentional bare main-UV smoothness sample. Both MatCap masks apply their own transforms in every body/optical path.

Gem imports `_RefractionStrength`, `_RefractionFresnelPower`, `_GemChromaticAberration`, `_GemEnvContrast`, `_GemEnvColor`, `_GemParticleLoop`, `_GemParticleColor`, `_GemVRParallaxStrength`. It uses three scene-colour offsets, three chromatically shifted reflection directions, contrast/desaturation, Fresnel and backface particle bands. Gem reuses the shared custom-cube selector, trilinear mip sampling, HDR decode, `_ReflectionCubeColor` and `_ReflectionCubeEnableLighting`. All models use position-aware, screen-UV probe sampling for blending / box projection / clustered atlas lookup. Toon retains the lilToon empty-legacy-probe custom-cube fallback only outside Forward+; an empty per-object slot never suppresses clustered probes unless an override is selected. Gamma and Linear projects use their respective dielectric grazing and surface-reduction formulas. Gem's intrinsic reflection replaces the ordinary Reflection layer, so it adds no Reflection keyword dimension. Interior normals are reversed/tilted exactly once before shading and all optical layers, independent of `_FlipNormal`. Refraction uses the same mapped-normal flip, Toon/Endfield view vector and flipped Endfield layer normals as the main shader. Refraction applies `_BackfaceColor` after optical layers with unit framebuffer alpha; Gem and Fur strands retain their source-specific stacks without this tint. The Fur body uses the full shared facing/backface behavior. In a mono PC camera VR parallax is naturally unchanged; stereo code is present but untested. Shared MatCap/Rim/Glitter/emission follow optics, with Gem material alpha applied once to each eligible mask. Like lilToon, `GemPre` / SRPDefaultUnlit clears the silhouette with black under One/Zero, then Forward / UniversalForward adds every front/interior layer under One/One, both using `_Cull`. PRE contributes scene fog once; additive layers fog towards black. Default depth-off additive accumulation is independent of triangle order; authored depth/culling can restrict participating layers.

Gem also retains material alpha in the final output, matching lilToon's separate alpha blend One / OneMinusSrcAlpha; additive RGB is independent of that alpha. FairyToon's shared toon / Endfield diffuse replaces lilToon's original diffuse. Refraction uses FairyToon's shared Reflection implementation rather than lilToon's special environment/background reflection mix. These are lighting-model approximations, not property-copy losses.

### Fur

Geometry shaders require **DX11 or Vulkan**; Metal is unsupported. Like the installed lilToon, fur is a barycentric ribbon/fin strip, not repeated inflated meshes. `_FurLayerNum` 1 / 2 / 3 emits 8 / 14 / 26 vertices per input triangle. Direction uses tangent-space `_FurVector.xyz`, `_FurVector.w` length, the vector normal map, optional vertex colour, triangle-local vertex-ID randomization, gravity and length mask. Noise-mask ST uses raw UV0; the fur mask, vector and length map use transformed main UV. Root offset, signed cubic / quartic taper, cutout remap, mode-specific AO and tip rim retain lilToon's controls.

| Fur draw | LightMode | Behavior |
|---|---|---|
| Forward | SRPDefaultUnlit | Full FairyToon body, ZWrite On; transparent in Transparent / 2 Pass, cutout in Cutout |
| FurForward | UniversalForward | Full transparent fur; full cutout fur in Cutout; shortened depth-writing cutout PRE in 2 Pass |
| FurSecond | UniversalForwardOnly | Full blended fur, ZWrite off; enabled only in 2 Pass through `SetShaderPassEnabled` |

This tag arrangement matches lilToon's URP shaders and the lab's Forward+ renderer. Actual draw scheduling/order needs Unity Frame Debugger confirmation. Deferred opaque rendering does not consume the middle UniversalForward tag and is not supported by this layout. Toon shells use shadow diffuse, rim shade and fur rim with the interpolated vertex normal; they omit normal maps, MatCap, ordinary Rim, Reflection, Glitter, Emission and Backlight. Endfield shells use only shadowed main light plus SH ambient, rim shade and fur rim; the body retains full Endfield shading. FurAO modifies albedo before lighting. Fur rim uses lilToon's `gray(saturate((1-lightColor)*sqrt(lightColor)))` inverse-light mask.

All Fur modes keep body ShadowCaster / CharacterShadow / DepthOnly / DepthNormals / MotionVectors enabled, matching lilToon's material setup. The transparent render queue may still exclude body draws from standard URP camera prepasses. Auxiliary passes cover **body geometry only**; fur silhouette extrusion, source shadow dithering / extra subpass alpha clipping and DepthOnly root-noise coverage are not reproduced. Fur has no outline or See-through Hair draws; their stored settings remain available on a return to another mode.

| Fur Mode | Body RGB blend | Body ZWrite / A2C | First shell RGB blend; ZWrite / A2C | Second shells |
|---|---|---|---|---|
| Transparent | SrcAlpha / OneMinusSrcAlpha | On / Off | SrcAlpha / OneMinusSrcAlpha; Off / Off | Off |
| Cutout | One / Zero | On / On | One / Zero; On / On | Off |
| 2 Pass | SrcAlpha / OneMinusSrcAlpha | On / Off | One / Zero; On / On (shortened PRE) | SrcAlpha / OneMinusSrcAlpha; Off / Off |

All Fur draws use alpha blending One / OneMinusSrcAlpha. lilToon's Transparent / 2 Pass body returns premultiplied RGB under One / OneMinusSrcAlpha; FairyToon's straight RGB under SrcAlpha / OneMinusSrcAlpha produces the same framebuffer contribution. `_FurAlphaToMask` derives to On for Cutout and 2 Pass PRE, Off for Transparent; the second shell draw keeps it Off.

All 44 special names from `lts_fur.shader` are imported, including advanced states:

| Group | Names copied by name |
|---|---|
| Maps / direction | `_FurNoiseMask`, `_FurMask`, `_FurLengthMask`, `_FurVectorTex`, `_FurVectorScale`, `_FurVector`, `_VertexColor2FurVector` |
| Shape / shading | `_FurGravity`, `_FurRandomize`, `_FurAO`, `_FurLayerNum`, `_FurRootOffset`, `_FurCutoutLength`, `_FurTouchStrength`, `_FurRimColor`, `_FurRimFresnelPower`, `_FurRimAntiLight` |
| Blend | `_FurCull`, `_FurSrcBlend`, `_FurDstBlend`, `_FurSrcBlendAlpha`, `_FurDstBlendAlpha`, `_FurBlendOp`, `_FurBlendOpAlpha` |
| Legacy forward-add | `_FurSrcBlendFA`, `_FurDstBlendFA`, `_FurSrcBlendAlphaFA`, `_FurDstBlendAlphaFA`, `_FurBlendOpFA`, `_FurBlendOpAlphaFA` |
| Depth / coverage | `_FurZClip`, `_FurZWrite`, `_FurZTest`, `_FurOffsetFactor`, `_FurOffsetUnits`, `_FurColorMask`, `_FurAlphaToMask` |
| Stencil | `_FurStencilRef`, `_FurStencilReadMask`, `_FurStencilWriteMask`, `_FurStencilComp`, `_FurStencilPass`, `_FurStencilFail`, `_FurStencilZFail` |

Fur Mode derives RGB/alpha blending, ZWrite and alpha-to-coverage. Other fur stencil/cull/depth-test/offset/blend-op/colour-mask settings are used by the independent fur draws. Legacy FA values are retained but URP uses lights in the same forward draw, so no ForwardAdd pass exists. `_FurTouchStrength` is retained but inactive, matching lilToon's URP path (its collision mechanism is Built-in only).

### Offline verification

Run `node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs` and `powershell -NoProfile -ExecutionPolicy Bypass -File Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -PackageCache D:/UNITY_PROJECTS/LiminalNW/Library/PackageCache`. Omit PackageCache to use the current project's `Library/PackageCache`. In PowerShell, `-Shaders @('FairyToon','Refraction','Fur','Gem')` selects shaders; `-RefractionBlur Both|Off|On` selects the optical branch. The compiler reads actual ShaderLab programs, compiles VS/PS/GS, checks every used material-buffer layout per shader and enforces 16 samplers / 128 textures **per stage**. `ReportVariants.cjs` reports material and pipeline source upper bounds, before Unity stripping; conditional pragma branches are included. Source-declared combinations are not actual built variant counts. See [the merged-tree report](Tests~/MergeConsistencyReport.md) for current results and reproduction options; [the pre-merge variant report](Tests~/VariantsReport.md) retains historical resource and variant tables.

## Credits

- lilToon by lilxyzw (MIT, https://github.com/lilxyzw/lilToon): property names and defaults, inspector layout, toon shading, rim, MatCap, reflection, face shadow, refraction / blur, geometry fur and gem formulas. Inspector wording and the copy / paste menu adapted from lilToon 2.4.1 are listed in `THIRD_PARTY_NOTICES.md`. FairyToon's own code still has no license in this repository.
- SeeThroughHair 1.5.2 by goorm (https://goorm.me/docs/see-through-hair), ported with permission: stencil roles, early-Z, view-angle fade and fake shadow.
- Endfield shading model: structure and constants from public analyses of Arknights: Endfield (Hypergryph) character rendering by LRyuki ("终末地角色渲染分析 通用材质") and qiudashu233 ("从零模仿复刻实现自己的终末地人物卡通渲染"), both on Zhihu; hair band layout (head-sphere normal, two lobes, flat top) after 图灵技术手记 ("头发各向异性——仿终末地", Zhihu); hair anti-sparkle lobe widening, the self-shadow N·L fade and the smooth-normal bake (weld, angle weights, coherence) after Ronald Burns' Endfield-style toon renderer write-up; the camera-aligned character projector, its 64-sample filter and the Endfield outline colour after Floraison's frame-capture analysis (floraison.io). Our own code; no game assets or shader code used. Environment BRDF: Brian Karis' analytic fit ("Physically Based Shading on Mobile", Epic Games).
- Wetness ideas: ZMD rain-suite porosity and normal-Y rain weighting, plus KWS2 water-height/soak-line integration. Used only as look and design references; no ZMD assets or code were copied. Wet shading, gravity-UV rivulets, water-film coat, procedural droplet glints and runtime state are our own implementation.
- Tonemap curve form after Timothy Lottes, "Advanced Techniques and Optimization of HDR Color Pipelines" (GDC 2016); the post order and the hue-preserving idea after public Endfield frame-capture and recreation write-ups. Interleaved gradient noise after Jorge Jimenez (SIGGRAPH 2014).
Compatibility: must compile on Unity 6000.6.0f1 (ProjectNW) and 6000.6.7f1.

### Reflection parity and validation

Reflection scalar values remain serialized unchanged: lilToon's `[Gamma]` flags on `_Reflectance` and `_Metallic` perform the Linear-project conversion during upload, with no second HLSL conversion. Custom cubes use trilinear mip filtering and lilToon's Gamma/native-HDR/encoded-HDR decoding. Reflection normal strength affects the unnormalized cubemap-ray normal; environment Fresnel always uses the mapped, facing-corrected normal. Toon primary normals reconstruct Z after scaling XY; Endfield retains its authored primary-only decoder in both Forward and DepthNormals.

Toon and Endfield both use URP position-aware probe blending, box projection and atlas lookup. Only nonclustered Toon retains the lilToon empty-legacy-probe custom-cube fallback; an explicit custom-cube override works in every path. Endfield retains its own environment BRDF and ambient multiplier. Optional shared Reflection layers use the corrected reflectance and cubemap sampling in either model. No material reconversion is needed for these shader fixes. See [SleeveParityAudit.md](Tests~/SleeveParityAudit.md) for material-specific arithmetic, source locations and remaining visual checks. Run `node Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs` for offline regressions; `Tests~/CompileShaders.ps1` also accepts `-ColorSpace Gamma` and `-NativeHDR`. These do not validate Unity import or GPU output.

For scripted mode changes, use `FairyToonMaterialSetup.SetRenderingMode` or `SetFurMode`. Explicit transitions migrate canonical default queues. Ordinary `Apply` preserves From Shader (raw queue -1) and explicit queues. Opaque/Cutout See-through Hair raises explicit queues below 2460 to its managed slot. Special variants bypass the See-through passes.

## Mermaid parity

Both MatCaps support lilToon custom normals (`_MatCapCustomNormal`, `_MatCapBumpMap`, `_MatCapBumpScale` and the `2nd` set), including independent texture transforms, flat defaults, face flipping and override precedence. Alpha masks apply their saved scale/offset after main UVs. Reflection and MatCap/Rim colour tints use the albedo before Rim Shade.

Whole-material and Main Color 2nd / 3rd dissolve support texture mask, UV point/line and object-position point/line modes, raw UV0, scrolling noise, strict threshold coverage and post-emission edge colour. Root dissolve remains inactive on opaque materials, Gem and Fur shells; layer dissolve works on opaque bodies but skips Gem and Fur shells. Masks/noise share sampler_MainTex, adding no sampler or keyword dimension. Existing converted materials retain their saved values; no material rewrite is required. See [MermaidParityAudit.md](Tests~/MermaidParityAudit.md) for per-material findings, limitations and validation. `Tests~/CompileShaders.ps1 -MermaidMatrix` checks full features, custom MatCap normals without primary normals, and disabled MatCaps; use `-Models Toon,Endfield -Coverage Transparent` for the material-focused matrix. Unity validation remains separate.


The shared layer property sets are:

| Layer | Mask / noise | Parameters / edge |
|---|---|---|
| Main 2nd | `_Main2ndDissolveMask`, `_Main2ndDissolveNoiseMask`, `_Main2ndDissolveNoiseMask_ScrollRotate`, `_Main2ndDissolveNoiseStrength` | `_Main2ndDissolveParams`, `_Main2ndDissolvePos`, `_Main2ndDissolveColor` |
| Main 3rd | `_Main3rdDissolveMask`, `_Main3rdDissolveNoiseMask`, `_Main3rdDissolveNoiseMask_ScrollRotate`, `_Main3rdDissolveNoiseStrength` | `_Main3rdDissolveParams`, `_Main3rdDissolvePos`, `_Main3rdDissolveColor` |

Dissolve helper selection follows the installed lilToon global noise features by default, including null gray noise textures. The per-family `Use Noise Helper` controls (`_FT_DissolveUseNoise`, `_FT_Main2ndDissolveUseNoise`, `_FT_Main3rdDissolveUseNoise`) are uniforms, default 1. Disable one only to reproduce a source shader with that noise feature excluded: UV line then uses rotation by `_DissolvePos.w` instead of projection along normalized `_DissolvePos.xy`. Zero noise strength does not switch formulas. Noise rotation ZW is intentionally ignored, matching lilToon. Layer edges are added after emission, independently of layer alpha, then receive the ordinary final material alpha (refraction uses its optical coverage).

`Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -DissolveMatrix -ProbeVolumes` compiles runtime and uniform-specialized dissolve cases across every variant/pass, without starting Unity. See Round 3 of [MermaidParityAudit.md](Tests~/MermaidParityAudit.md) for material walkthroughs, retained Gem auxiliary-pass differences, zero-width policy and offline-only validation.
