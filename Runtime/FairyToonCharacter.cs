using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NW.FairyToon
{
    /// <summary>Character-level lighting for Endfield-mode FairyToon materials: sun / shade state, the top fill light, a
    /// main-light override, a terminator offset, a directional ambient with an optional cap, an exposure for the main light
    /// and up to four small character lights.</summary>
    /// <remarks>Put it on the avatar root. The values go out as shader globals, so they apply to every Endfield material
    /// in view: fine for one lab character; per-character values are an open item. Without this component the shaders
    /// use full sun, no top light, the scene's light and ambient, and no small lights.</remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FairyToonCharacter : MonoBehaviour
    {
        private static readonly int LightingId = Shader.PropertyToID("_FT_EFLighting");
        private static readonly int TopColorId = Shader.PropertyToID("_FT_EFTopColor");
        private static readonly int TopFillId = Shader.PropertyToID("_FT_EFTopFill");
        private static readonly int LightOverrideId = Shader.PropertyToID("_FT_EFLightOverride");
        private static readonly int ParamsId = Shader.PropertyToID("_FT_EFParams");
        private static readonly int AmbientSkyId = Shader.PropertyToID("_FT_EFAmbientSky");
        private static readonly int AmbientGroundId = Shader.PropertyToID("_FT_EFAmbientGround");
        private static readonly int ExposureId = Shader.PropertyToID("_FT_EFExposure");
        private static readonly int AmbientCapId = Shader.PropertyToID("_FT_EFAmbientCap");
        private static readonly int SmallLightPositionId = Shader.PropertyToID("_FT_EFSmallLightPosition");
        private static readonly int SmallLightColorId = Shader.PropertyToID("_FT_EFSmallLightColor");
        private static readonly int SmallLightRoleId = Shader.PropertyToID("_FT_EFSmallLightRole");
        /// <summary>Small lights the shader takes per character; further ones are ignored.</summary>
        public const int MaxSmallLights = 4;
        private static readonly Vector4[] smallPositions = new Vector4[MaxSmallLights];
        private static readonly Vector4[] smallColors = new Vector4[MaxSmallLights];
        private static readonly Vector4[] smallRoles = new Vector4[MaxSmallLights];
        private readonly List<FairyToonCharacterLight> smallLights = new List<FairyToonCharacterLight>();

        private static readonly List<FairyToonCharacter> registry = new List<FairyToonCharacter>();
        private static uint lastShadowOwner;
        // (created on first use: ShaderTagId may not be built while Unity constructs the component)
        private static ShaderTagId lightModeTag, shadowTag;
        private static bool tagsReady;

        [Tooltip("Draw this character's opaque renderers into the FairyToon character shadow atlas.")]
        [SerializeField] private bool castCharacterShadow = true;
        [Tooltip("Skinned meshes are fitted by their bones plus this padding (m): avatar tools often widen renderer bounds " +
            "to stop culling, which would spread the shadow map thin. Raise it if a hair tip or skirt edge loses its shadow.")]
        [Range(0f, 0.5f)] [SerializeField] private float shadowBoundsPadding = 0.15f;
        private RendererCache[] rendererCache = System.Array.Empty<RendererCache>();
        private readonly Dictionary<Renderer, Transform[]> boneCache = new Dictionary<Renderer, Transform[]>();
        private readonly List<Material> materialScratch = new List<Material>();
        private readonly Dictionary<Material, PassCache> passCache = new Dictionary<Material, PassCache>();
        private readonly List<ShadowDraw> shadowDraws = new List<ShadowDraw>();

        internal static IReadOnlyList<FairyToonCharacter> Registry => registry;
        internal IReadOnlyList<ShadowDraw> ShadowDraws => shadowDraws;
        internal uint ShadowOwner { get; private set; }
        public bool CastCharacterShadow { get => castCharacterShadow; set => castCharacterShadow = value; }

        internal struct ShadowDraw
        {
            public Renderer renderer;
            public Material material;
            public int submesh;
            public int pass;
        }

        private sealed class RendererCache
        {
            public Renderer renderer;
            public MeshFilter filter;
            public Mesh mesh;
            public int submeshCount;
            public uint previousShaderUserValue;
            public readonly List<Material> materials = new List<Material>();
        }

        private struct PassCache
        {
            public Shader shader;
            public int passCount;
            public int pass;
        }

        [Tooltip("1 = in the sun. 0 = in shade: the main light's direct term is removed and the top fill light carries the look.")]
        [Range(0f, 1f)] [SerializeField] private float dayStrength = 1f;
        [Tooltip("Fill light from straight above, added on top of the ambient light.")]
        [ColorUsage(false, true)] [SerializeField] private Color topColor = new Color(0.78f, 0.82f, 0.9f);
        [Tooltip("Top light strength in the sun (DayStrength 1).")]
        [Range(0f, 2f)] [SerializeField] private float topStrength = 0.2f;
        [Tooltip("Top light strength in the shade (DayStrength 0), where it replaces the sun as the shaping light.")]
        [Range(0f, 2f)] [SerializeField] private float shadeTopStrength = 0.9f;
        [Tooltip("Shifts the top light's falloff: positive reaches further down the sides.")]
        [Range(-1f, 1f)] [SerializeField] private float topOffset = 0.2f;
        [Tooltip("Constant part of the top light that reaches every side, ignoring AO. In the shade it keeps faces and " +
            "sideways surfaces readable instead of leaving them to the ambient light alone.")]
        [Range(0f, 2f)] [SerializeField] private float topFill = 0.35f;

        [Header("Main light")]
        [Tooltip("0 = the scene's main light. 1 = a light fixed to the camera (yaw and pitch below), so the character keeps a " +
            "chosen light whatever the sun does. In between blends the two directions.")]
        [Range(0f, 1f)] [SerializeField] private float lightOverride;
        [Tooltip("Override light: degrees to the camera's right (negative: left).")]
        [Range(-180f, 180f)] [SerializeField] private float overrideYaw = 30f;
        [Tooltip("Override light: degrees above the horizon.")]
        [Range(-30f, 90f)] [SerializeField] private float overridePitch = 35f;
        [Tooltip("Moves the light-dark boundary on every part shaded by its normal (not the face SDF). Negative: more shade.")]
        [Range(-1f, 1f)] [SerializeField] private float terminatorOffset;

        [Header("Directional ambient")]
        [Tooltip("0 = the scene's ambient probe. 1 = the sky / ground gradient below, which keeps the character's top bright " +
            "and its underside dark in any scene.")]
        [Range(0f, 1f)] [SerializeField] private float ambientBlend;
        [Tooltip("Ambient from above.")]
        [ColorUsage(false, true)] [SerializeField] private Color ambientSky = new Color(0.5f, 0.56f, 0.66f);
        [Tooltip("Ambient from below.")]
        [ColorUsage(false, true)] [SerializeField] private Color ambientGround = new Color(0.24f, 0.22f, 0.21f);
        [Tooltip("Caps the ambient light's luminance (hue kept), so a bright or strongly coloured probe cannot wash the " +
            "character out. 0 = no cap.")]
        [Range(0f, 2f)] [SerializeField] private float ambientMaxLuminance;
        [Tooltip("Saturation of the ambient light around its luminance: 1 as the probe or gradient gives it, 0 grey. A " +
            "low value keeps a hint of the sky's colour without tinting the character.")]
        [Range(0f, 1f)] [SerializeField] private float ambientSaturation = 1f;
        [Tooltip("Soft ceiling on ambient + top + direct light (luminance): passes values below half of it, rolls off " +
            "towards it above. 0 = no ceiling.")]
        [Range(0f, 4f)] [SerializeField] private float lightCeiling;

        [Header("Exposure")]
        [Tooltip("Scales the main light's direct term (diffuse, specular, hair band, light rim, backlight, outline) " +
            "without changing the sun / shade state. Use it to darken a character at night; DayStrength below 1 moves " +
            "her into the shade look instead.")]
        [Range(0f, 2f)] [SerializeField] private float exposure = 1f;
        [Tooltip("0 = the main light counts as a colour only (normalised, as in Endfield). 1 = its luminance over the " +
            "reference below scales the direct term too (capped at 1), so a dim moon gives a dim key light.")]
        [Range(0f, 1f)] [SerializeField] private float followLightIntensity;
        [Tooltip("Main light luminance (colour x intensity) that counts as full strength for Follow Light Intensity.")]
        [Min(0.0001f)] [SerializeField] private float referenceLuminance = 1f;

        public float DayStrength
        {
            get => dayStrength;
            set { dayStrength = Mathf.Clamp01(value); Push(); }
        }

        public Color TopColor
        {
            get => topColor;
            set { topColor = value; Push(); }
        }

        public float TopStrength
        {
            get => topStrength;
            set { topStrength = Mathf.Max(0f, value); Push(); }
        }

        public float ShadeTopStrength
        {
            get => shadeTopStrength;
            set { shadeTopStrength = Mathf.Max(0f, value); Push(); }
        }

        public float TopFill
        {
            get => topFill;
            set { topFill = Mathf.Max(0f, value); Push(); }
        }

        public float LightOverride
        {
            get => lightOverride;
            set { lightOverride = Mathf.Clamp01(value); Push(); }
        }

        public float OverrideYaw
        {
            get => overrideYaw;
            set { overrideYaw = value; Push(); }
        }

        public float OverridePitch
        {
            get => overridePitch;
            set { overridePitch = value; Push(); }
        }

        public float TerminatorOffset
        {
            get => terminatorOffset;
            set { terminatorOffset = Mathf.Clamp(value, -1f, 1f); Push(); }
        }

        public float AmbientBlend
        {
            get => ambientBlend;
            set { ambientBlend = Mathf.Clamp01(value); Push(); }
        }

        public Color AmbientSky
        {
            get => ambientSky;
            set { ambientSky = value; Push(); }
        }

        public Color AmbientGround
        {
            get => ambientGround;
            set { ambientGround = value; Push(); }
        }

        public float AmbientMaxLuminance
        {
            get => ambientMaxLuminance;
            set { ambientMaxLuminance = Mathf.Max(0f, value); Push(); }
        }

        public float AmbientSaturation
        {
            get => ambientSaturation;
            set { ambientSaturation = Mathf.Max(0f, value); Push(); }
        }

        public float LightCeiling
        {
            get => lightCeiling;
            set { lightCeiling = Mathf.Max(0f, value); Push(); }
        }

        public float Exposure
        {
            get => exposure;
            set { exposure = Mathf.Max(0f, value); Push(); }
        }

        public float FollowLightIntensity
        {
            get => followLightIntensity;
            set { followLightIntensity = Mathf.Clamp01(value); Push(); }
        }

        public float ReferenceLuminance
        {
            get => referenceLuminance;
            set { referenceLuminance = Mathf.Max(0.0001f, value); Push(); }
        }

        /// <summary>The small lights registered on this character, in the order the shader takes them.</summary>
        public IReadOnlyList<FairyToonCharacterLight> SmallLights => smallLights;

        internal void Register(FairyToonCharacterLight light)
        {
            if (!smallLights.Contains(light)) smallLights.Add(light);
            Push();
        }

        internal void Unregister(FairyToonCharacterLight light)
        {
            smallLights.Remove(light);
            Push();
        }

        internal void SmallLightChanged() => Push();

        private void OnEnable()
        {
            if (ShadowOwner == 0)
            {
                ShadowOwner = ++lastShadowOwner;
                if (ShadowOwner == 0) ShadowOwner = ++lastShadowOwner;
            }
            if (!registry.Contains(this)) registry.Add(this);
            RefreshRenderers();
            Push();
        }
        private void OnValidate() => Push();
        private void LateUpdate() => Push();

        private void OnDisable()
        {
            registry.Remove(this);
            RestoreShadowOwners();
            Shader.SetGlobalVector(LightingId, Vector4.zero);
            // (zero is "no change" for each of these)
            Shader.SetGlobalVector(LightOverrideId, Vector4.zero);
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Shader.SetGlobalVector(AmbientSkyId, Vector4.zero);
            Shader.SetGlobalVector(ExposureId, Vector4.zero);
            Shader.SetGlobalVector(AmbientCapId, Vector4.zero);
        }

        private void OnTransformChildrenChanged() => RefreshRenderers();

        public void RefreshRenderers()
        {
            RestoreShadowOwners();
            var renderers = GetComponentsInChildren<Renderer>(true);
            rendererCache = new RendererCache[renderers.Length];
            boneCache.Clear();
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                rendererCache[i] = new RendererCache
                {
                    renderer = renderer,
                    filter = renderer.GetComponent<MeshFilter>(),
                    previousShaderUserValue = ReadShaderUserValue(renderer)
                };
                if (isActiveAndEnabled) WriteShaderUserValue(renderer, ShadowOwner);
                if (renderers[i] is SkinnedMeshRenderer skinned) boneCache[skinned] = skinned.bones;
            }
            passCache.Clear();
            UpdateShadowDraws(true);
        }

        private void RestoreShadowOwners()
        {
            uint owner = ShadowOwner;
            if (owner == 0) return;
            foreach (var entry in rendererCache)
            {
                var renderer = entry.renderer;
                if (renderer != null && ReadShaderUserValue(renderer) == owner)
                    WriteShaderUserValue(renderer, entry.previousShaderUserValue);
            }
        }

        private static uint ReadShaderUserValue(Renderer renderer) => renderer switch
        {
            SkinnedMeshRenderer skinned => skinned.GetShaderUserValue(),
            MeshRenderer mesh => mesh.GetShaderUserValue(),
            _ => 0u
        };

        private static void WriteShaderUserValue(Renderer renderer, uint value)
        {
            if (renderer is SkinnedMeshRenderer skinned) skinned.SetShaderUserValue(value);
            else if (renderer is MeshRenderer mesh) mesh.SetShaderUserValue(value);
        }

        internal bool TryGetShadowBounds(out Bounds bounds)
        {
            UpdateShadowDraws(false);
            bounds = default;
            bool any = false;
            Renderer previous = null;
            for (int i = 0; i < shadowDraws.Count; i++)
            {
                var draw = shadowDraws[i];
                if (!CanDraw(draw) || draw.renderer == previous) continue;
                Bounds rendererBounds = ShadowBoundsOf(draw.renderer);
                if (any) bounds.Encapsulate(rendererBounds);
                else bounds = rendererBounds;
                any = true;
                previous = draw.renderer;
            }
            return any;
        }

        private Bounds ShadowBoundsOf(Renderer renderer)
        {
            Bounds bounds = renderer.bounds;
            if (!boneCache.TryGetValue(renderer, out var bones) || bones == null) return bounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                Vector3 position = bones[i].position;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
            if (min.x > max.x) return bounds;
            // (clamped to the renderer's own bounds, in case those are the tighter ones)
            Vector3 padding = Vector3.one * shadowBoundsPadding;
            min = Vector3.Max(min - padding, bounds.min);
            max = Vector3.Min(max + padding, bounds.max);
            if (min.x > max.x || min.y > max.y || min.z > max.z) return bounds;
            bounds.SetMinMax(min, max);
            return bounds;
        }

        internal static bool CanDraw(ShadowDraw draw)
        {
            var renderer = draw.renderer;
            return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy
                && renderer.shadowCastingMode != ShadowCastingMode.Off && draw.material != null
                && draw.material.renderQueue <= 2500;
        }

        private void UpdateShadowDraws(bool rebuild)
        {
            for (int i = 0; i < rendererCache.Length; i++)
            {
                var cache = rendererCache[i];
                if (cache.renderer == null) continue;
                cache.renderer.GetSharedMaterials(materialScratch);
                Mesh mesh = cache.renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : cache.filter != null ? cache.filter.sharedMesh : null;
                int count = mesh != null ? mesh.subMeshCount
                    : cache.renderer is SkinnedMeshRenderer || cache.renderer is MeshRenderer ? 0 : 1;
                bool changed = mesh != cache.mesh || count != cache.submeshCount || materialScratch.Count != cache.materials.Count;
                for (int m = 0; m < materialScratch.Count && !changed; m++)
                    changed = materialScratch[m] != cache.materials[m];
                // (shader swaps keep the material reference, but can change its caster pass)
                for (int m = 0; m < materialScratch.Count; m++)
                {
                    var material = materialScratch[m];
                    if (material == null) continue;
                    if (!passCache.TryGetValue(material, out var entry) || entry.shader != material.shader
                        || entry.passCount != material.passCount) changed = true;
                }
                if (!changed && !rebuild) continue;
                cache.mesh = mesh;
                cache.submeshCount = count;
                cache.materials.Clear();
                cache.materials.AddRange(materialScratch);
                rebuild = true;
            }
            if (!rebuild) return;
            if (!tagsReady)
            {
                lightModeTag = new ShaderTagId("LightMode");
                shadowTag = new ShaderTagId(FairyToon.CharacterShadowLightMode);
                tagsReady = true;
            }
            shadowDraws.Clear();
            for (int i = 0; i < rendererCache.Length; i++)
            {
                var cache = rendererCache[i];
                if (cache.renderer == null || cache.renderer is ParticleSystemRenderer
                    || cache.renderer is TrailRenderer || cache.renderer is LineRenderer) continue;
                for (int s = 0; s < cache.submeshCount && s < cache.materials.Count; s++)
                {
                    var material = cache.materials[s];
                    if (material == null) continue;
                    if (!passCache.TryGetValue(material, out var entry) || entry.shader != material.shader
                        || entry.passCount != material.passCount)
                    {
                        entry = new PassCache { shader = material.shader, passCount = material.passCount, pass = -1 };
                        if (entry.shader != null)
                            for (int p = 0; p < entry.passCount; p++)
                                if (entry.shader.FindPassTagValue(p, lightModeTag) == shadowTag) { entry.pass = p; break; }
                        passCache[material] = entry;
                    }
                    shadowDraws.Add(new ShadowDraw { renderer = cache.renderer, material = material, submesh = s, pass = entry.pass });
                }
            }
        }

        private void Push()
        {
            if (!isActiveAndEnabled) return;
            // (w = 1 tells the shader the globals are set; colours as linear vectors, no implicit conversion)
            Shader.SetGlobalVector(LightingId, new Vector4(dayStrength, topStrength, topOffset, 1f));
            Color top = topColor.linear;
            Shader.SetGlobalVector(TopColorId, new Vector4(top.r, top.g, top.b, shadeTopStrength));
            Shader.SetGlobalVector(TopFillId, new Vector4(topFill, 0f, 0f, 0f));
            float yaw = overrideYaw * Mathf.Deg2Rad, pitch = overridePitch * Mathf.Deg2Rad;
            Shader.SetGlobalVector(LightOverrideId, new Vector4(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch), lightOverride));
            Color sky = ambientSky.linear, ground = ambientGround.linear;
            Shader.SetGlobalVector(AmbientSkyId, new Vector4(sky.r, sky.g, sky.b, ambientBlend));
            Shader.SetGlobalVector(AmbientGroundId, new Vector4(ground.r, ground.g, ground.b, 0f));
            Shader.SetGlobalVector(ExposureId, new Vector4(exposure, followLightIntensity, Mathf.Max(referenceLuminance, 0.0001f), 1f));
            Shader.SetGlobalVector(AmbientCapId, new Vector4(ambientMaxLuminance, ambientSaturation, lightCeiling, 1f));
            int count = 0;
            for (int i = 0; i < smallLights.Count && count < MaxSmallLights; i++)
            {
                var light = smallLights[i];
                if (light == null || !light.isActiveAndEnabled) continue;
                Vector3 position = light.transform.position;
                Color color = light.Color.linear;
                smallPositions[count] = new Vector4(position.x, position.y, position.z, light.Range);
                smallColors[count] = new Vector4(color.r, color.g, color.b, light.Intensity);
                smallRoles[count] = new Vector4((float)light.Role, 0f, 0f, 0f);
                count++;
            }
            // (always the full array: Unity fixes a global array's size the first time it is set)
            for (int i = count; i < MaxSmallLights; i++) smallPositions[i] = smallColors[i] = smallRoles[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(SmallLightPositionId, smallPositions);
            Shader.SetGlobalVectorArray(SmallLightColorId, smallColors);
            Shader.SetGlobalVectorArray(SmallLightRoleId, smallRoles);
            Shader.SetGlobalVector(ParamsId, new Vector4(terminatorOffset, count, 0f, 0f));
        }
    }
}
